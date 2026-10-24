#requires -Version 7
<#
.SYNOPSIS
    Télécharge et vérifie les binaires externes déclarés dans third_party/deps.json.

.DESCRIPTION
    - Tor Expert Bundle (Windows x86_64) : signature GPG valide, faite par la clé dont
      l'empreinte est figée dans deps.json, ET empreinte SHA-256 figée.
    - Wintun : empreinte SHA-256 figée, ET signature Authenticode valide de wintun.dll
      au nom de WireGuard LLC.
    - tun2socks : empreinte SHA-256 figée (le projet ne signe pas ses fichiers).
    Le moindre écart arrête le script.
#>
[CmdletBinding()]
param(
    [string] $OutDir = (Join-Path $PSScriptRoot '..' 'third_party' 'bin')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$deps = Get-Content (Join-Path $PSScriptRoot '..' 'third_party' 'deps.json') -Raw | ConvertFrom-Json

function Test-Installed([string] $Dir, [string] $Version) {
    $marker = Join-Path $Dir '.version'
    (Test-Path $marker) -and ((Get-Content $marker -Raw).Trim() -eq $Version)
}

# Télécharge le fichier depuis la première adresse qui répond (et sa signature .asc si demandé).
function Save-Dependency($Dep, [string] $FileName, [string] $Work, [switch] $WithSignature) {
    $path = Join-Path $Work $FileName
    foreach ($baseUrl in $Dep.baseUrls) {
        $url = "$($baseUrl -replace '\{version\}', $Dep.version)/$FileName"
        try {
            Write-Host "Téléchargement de $url"
            Invoke-WebRequest -Uri $url -OutFile $path
            if ($WithSignature) { Invoke-WebRequest -Uri "$url.asc" -OutFile "$path.asc" }
            return $path
        }
        catch {
            Write-Warning "Échec : $($_.Exception.Message)"
        }
    }
    throw "Impossible de télécharger $FileName."
}

function Assert-Sha256([string] $Path, [string] $Expected) {
    $hash = (Get-FileHash -Algorithm SHA256 $Path).Hash.ToLowerInvariant()
    $name = Split-Path $Path -Leaf
    if ([string]::IsNullOrWhiteSpace($Expected)) {
        throw "Aucune empreinte SHA-256 dans deps.json pour $name. Empreinte du fichier : $hash"
    }
    if ($hash -ne $Expected.ToLowerInvariant()) {
        throw "Empreinte SHA-256 inattendue pour $name : $hash (attendu : $Expected)."
    }
    Write-Host "Empreinte SHA-256 conforme ($name)."
}

function Install-Tor($Tor, [string] $Work) {
    $version = $Tor.version
    $fileName = $Tor.fileName -replace '\{version\}', $version
    $torDir = Join-Path $OutDir 'tor'
    if (Test-Installed $torDir $version) {
        Write-Host "Tor $version déjà présent dans $torDir."
        return
    }

    $archive = Save-Dependency $Tor $fileName $Work -WithSignature

    # 1. Signature GPG, avec un trousseau temporaire qui ne contient que la clé attendue
    $gpgCommand = Get-Command gpg -ErrorAction SilentlyContinue
    $gpg = if ($gpgCommand) { $gpgCommand.Source } else { Join-Path $env:ProgramFiles 'Git' 'usr' 'bin' 'gpg.exe' }
    if (-not (Test-Path $gpg)) { throw 'gpg introuvable : installer Git for Windows ou GnuPG.' }

    New-Item -ItemType Directory -Path (Join-Path $Work 'gnupg') | Out-Null
    Invoke-WebRequest -Uri $Tor.signingKeyUrl -OutFile (Join-Path $Work 'signing-key.asc')

    # Chemins relatifs : le gpg de Git for Windows (MSYS) comprend mal les chemins C:\...
    Push-Location $Work
    try {
        # gpg peut sortir en erreur faute de gpg-agent, inutile pour une clé publique :
        # seule compte la vérification VALIDSIG ci-dessous.
        & $gpg --homedir gnupg --batch --import signing-key.asc
        $status = & $gpg --homedir gnupg --batch --status-fd 1 --verify "$fileName.asc" $fileName 2>$null
    }
    finally {
        Pop-Location
    }
    $validSig = $status | Where-Object { $_ -match '^\[GNUPG:\] VALIDSIG ' } | Select-Object -First 1
    $expectedKey = $Tor.signingKeyFingerprint.ToUpperInvariant()
    if (-not $validSig -or (($validSig -split ' ')[-1]) -ne $expectedKey) {
        $status | ForEach-Object { Write-Host $_ }
        throw "Signature GPG invalide pour $fileName."
    }
    Write-Host "Signature GPG valide (clé $expectedKey)."

    # 2. Empreinte SHA-256 figée
    Assert-Sha256 $archive $Tor.sha256

    # 3. Extraction (tar.exe de Windows : celui de Git ne comprend pas les chemins D:\...)
    if (Test-Path $torDir) { Remove-Item $torDir -Recurse -Force }
    New-Item -ItemType Directory -Path $torDir -Force | Out-Null
    & (Join-Path $env:SystemRoot 'System32' 'tar.exe') -xzf $archive -C $torDir
    if ($LASTEXITCODE -ne 0) { throw 'Extraction de l''archive échouée.' }

    Get-ChildItem $torDir -Recurse -File | ForEach-Object { Write-Host "  $([IO.Path]::GetRelativePath($torDir, $_.FullName))" }
    if (-not (Test-Path (Join-Path $torDir 'tor' 'tor.exe'))) { throw "tor.exe absent de l'archive." }

    Set-Content -Path (Join-Path $torDir '.version') -Value $version -NoNewline
    Write-Host "Tor $version installé dans $torDir."
}

function Install-Wintun($Wintun, [string] $Work) {
    $version = $Wintun.version
    $fileName = $Wintun.fileName -replace '\{version\}', $version
    $wintunDir = Join-Path $OutDir 'wintun'
    if (Test-Installed $wintunDir $version) {
        Write-Host "Wintun $version déjà présent dans $wintunDir."
        return
    }

    # 1. Empreinte SHA-256 figée
    $archive = Save-Dependency $Wintun $fileName $Work
    Assert-Sha256 $archive $Wintun.sha256

    # 2. Signature Authenticode de la DLL, au nom attendu
    $extracted = Join-Path $Work 'wintun-extract'
    Expand-Archive -Path $archive -DestinationPath $extracted
    $dll = Join-Path $extracted $Wintun.dllPath
    if (-not (Test-Path $dll)) { throw "$($Wintun.dllPath) absent de l'archive." }
    $signature = Get-AuthenticodeSignature $dll
    $subject = $signature.SignerCertificate.Subject
    if ($signature.Status -ne 'Valid' -or -not $subject.StartsWith("$($Wintun.authenticodeSubject),")) {
        throw "Signature Authenticode invalide pour wintun.dll : $($signature.Status), $subject."
    }
    Write-Host "Signature Authenticode valide ($($Wintun.authenticodeSubject))."

    # 3. Copie de la DLL et de sa licence
    if (Test-Path $wintunDir) { Remove-Item $wintunDir -Recurse -Force }
    New-Item -ItemType Directory -Path $wintunDir -Force | Out-Null
    Copy-Item $dll $wintunDir
    Copy-Item (Join-Path $extracted 'wintun' 'LICENSE.txt') (Join-Path $wintunDir 'LICENSE-wintun.txt')

    Set-Content -Path (Join-Path $wintunDir '.version') -Value $version -NoNewline
    Write-Host "Wintun $version installé dans $wintunDir."
}

function Install-Tun2Socks($Tun2Socks, [string] $Work) {
    $version = $Tun2Socks.version
    $fileName = $Tun2Socks.fileName -replace '\{version\}', $version
    $tun2socksDir = Join-Path $OutDir 'tun2socks'
    if (Test-Installed $tun2socksDir $version) {
        Write-Host "tun2socks $version déjà présent dans $tun2socksDir."
        return
    }

    # 1. Empreinte SHA-256 figée
    $archive = Save-Dependency $Tun2Socks $fileName $Work
    Assert-Sha256 $archive $Tun2Socks.sha256

    # 2. Extraction, sous un nom fixe : tun2socks.exe
    $extracted = Join-Path $Work 'tun2socks-extract'
    Expand-Archive -Path $archive -DestinationPath $extracted
    $exe = Join-Path $extracted $Tun2Socks.exePath
    if (-not (Test-Path $exe)) { throw "$($Tun2Socks.exePath) absent de l'archive." }

    if (Test-Path $tun2socksDir) { Remove-Item $tun2socksDir -Recurse -Force }
    New-Item -ItemType Directory -Path $tun2socksDir -Force | Out-Null
    Copy-Item $exe (Join-Path $tun2socksDir 'tun2socks.exe')

    Set-Content -Path (Join-Path $tun2socksDir '.version') -Value $version -NoNewline
    Write-Host "tun2socks $version installé dans $tun2socksDir."
}

$work = Join-Path ([IO.Path]::GetTempPath()) "torvpn-deps-$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $work | Out-Null
try {
    Install-Tor $deps.tor $work
    Install-Wintun $deps.wintun $work
    Install-Tun2Socks $deps.tun2socks $work
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
