#requires -Version 7
<#
.SYNOPSIS
    Publie le service et l'interface en versions autonomes (.NET embarqué), pour l'installateur.

.DESCRIPTION
    Résultat dans publish/OnionGuard/service et publish/OnionGuard/app, comme dans la CI. Vérifie que
    Tor, lyrebird, tun2socks et Wintun sont bien à côté du service : lancer scripts/fetch-deps.ps1
    avant.
#>
[CmdletBinding()]
param(
    [string] $OutDir = (Join-Path $PSScriptRoot '..' 'publish' 'OnionGuard')
)

$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'

foreach ($project in @(
        @{ Path = 'src/OnionGuard.Service/OnionGuard.Service.csproj'; Out = 'service' },
        @{ Path = 'src/OnionGuard.App/OnionGuard.App.csproj'; Out = 'app' })) {
    $target = Join-Path $OutDir $project.Out
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    dotnet publish (Join-Path $root $project.Path) -c Release -r win-x64 --self-contained -o $target
    if ($LASTEXITCODE -ne 0) { throw "Publication échouée : $($project.Path)" }
}

$required = @(
    'service/OnionGuard.Service.exe',
    'service/tor-bundle/tor/tor.exe',
    'service/tor-bundle/tor/pluggable_transports/lyrebird.exe',
    'service/tor-bundle/tor/pluggable_transports/pt_config.json',
    'service/tunnel/tun2socks.exe',
    'service/tunnel/wintun.dll',
    'app/OnionGuard.exe'
)
foreach ($file in $required) {
    if (-not (Test-Path (Join-Path $OutDir $file))) { throw "$file manquant dans la publication." }
}
$size = (Get-ChildItem $OutDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Publication prête dans {0} ({1:N0} Mo)." -f (Resolve-Path $OutDir), $size)
