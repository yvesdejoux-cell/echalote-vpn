#requires -Version 7
<#
.SYNOPSIS
    Publie le service et l'interface en versions autonomes (.NET embarqué), pour l'installateur.

.DESCRIPTION
    Résultat dans publish/EchaloteVpn/service et publish/EchaloteVpn/app, comme dans la CI. Vérifie que
    Tor, lyrebird, tun2socks et Wintun sont bien à côté du service : lancer scripts/fetch-deps.ps1
    avant.
#>
[CmdletBinding()]
param(
    [string] $OutDir = (Join-Path $PSScriptRoot '..' 'publish' 'EchaloteVpn')
)

$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'

foreach ($project in @(
        @{ Path = 'src/EchaloteVpn.Service/EchaloteVpn.Service.csproj'; Out = 'service' },
        @{ Path = 'src/EchaloteVpn.App/EchaloteVpn.App.csproj'; Out = 'app' })) {
    $target = Join-Path $OutDir $project.Out
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    dotnet publish (Join-Path $root $project.Path) -c Release -r win-x64 --self-contained -o $target
    if ($LASTEXITCODE -ne 0) { throw "Publication échouée : $($project.Path)" }
}

$required = @(
    'service/EchaloteVpn.Service.exe',
    'service/tor-bundle/tor/tor.exe',
    'service/tor-bundle/tor/pluggable_transports/lyrebird.exe',
    'service/tor-bundle/tor/pluggable_transports/pt_config.json',
    'service/tunnel/tun2socks.exe',
    'service/tunnel/wintun.dll',
    'app/EchaloteVpn.exe'
)
foreach ($file in $required) {
    if (-not (Test-Path (Join-Path $OutDir $file))) { throw "$file manquant dans la publication." }
}
$size = (Get-ChildItem $OutDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Publication prête dans {0} ({1:N0} Mo)." -f (Resolve-Path $OutDir), $size)
