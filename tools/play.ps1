# Starts Dolphin with the Galaxy Co-op patch and serves the in-game co-op menu
# (opens by itself in the first galaxy, then press -): pick Mario or Luigi,
# host a private or public game, or join one.
#   .\tools\play.ps1                 # builds the game patch and the PC side first
#   .\tools\play.ps1 -NoBuild
#   .\tools\play.ps1 -Second         # a second player on this PC (own settings and Dolphin user folder)
#   .\tools\play.ps1 -Web            # the old browser page instead of the in-game menu
param(
    [switch]$NoBuild,
    [switch]$Second,
    [switch]$Web
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$runFsx = Join-Path $root 'pc\tools\run.fsx'

if (-not $NoBuild) {
    try {
        & (Join-Path $PSScriptRoot 'build-smg2.ps1') -NoDeploy
    }
    catch {
        if (-not (Test-Path (Join-Path $root 'build\smg2\GalaxyCoop.xml'))) { throw }
        Write-Warning "game patch build failed ($_); using the previous build in build\smg2"
    }
    dotnet build (Join-Path $root 'pc\GalaxyCoop.sln') -c Debug --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "PC build failed" }
}

$launcherArgs = @()
if ($Second) {
    $dolphinDir = if ($env:GCOOP_DOLPHIN_DIR) { $env:GCOOP_DOLPHIN_DIR } else { 'D:\Games\Dolphin-x64' }
    $mainUser = if (Test-Path (Join-Path $dolphinDir 'portable.txt')) { Join-Path $dolphinDir 'User' } else { Join-Path $env:APPDATA 'Dolphin Emulator' }
    $userDir = Join-Path $root 'build\dolphin-users\p2'
    if (-not (Test-Path (Join-Path $userDir 'Config')) -and (Test-Path (Join-Path $mainUser 'Config'))) {
        New-Item -ItemType Directory -Force -Path $userDir | Out-Null
        Copy-Item -Recurse (Join-Path $mainUser 'Config') (Join-Path $userDir 'Config')
    }
    $launcherArgs = @('--settings', (Join-Path $env:APPDATA 'GalaxyCoop\launcher-p2.json'), '--dolphin-user', $userDir)
}
if ($Web) { $launcherArgs += '--web' }

dotnet fsi $runFsx launcher @launcherArgs
