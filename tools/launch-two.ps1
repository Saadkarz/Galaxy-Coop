# Local co-op test: one server, N Dolphin instances (each with its own user
# directory and the Galaxy Co-op patch enabled) and one companion per Dolphin.
#   .\tools\launch-two.ps1                    # 2 players, builds everything first
#   .\tools\launch-two.ps1 -Players 3 -NoBuild
#   .\tools\launch-two.ps1 -Iso "D:\Games\...\SMG2.iso"
#   .\tools\launch-two.ps1 -CompanionArgs --sync-enemies,--sync-switches
param(
    [int]$Players = 2,
    [string]$DolphinDir = $(if ($env:GCOOP_DOLPHIN_DIR) { $env:GCOOP_DOLPHIN_DIR } else { 'D:\Games\Dolphin-x64' }),
    [string]$Iso = '',
    [string]$UsersRoot = '',
    [int]$Port = 7777,
    [switch]$NoBuild,
    [switch]$NoServer,
    [string[]]$CompanionArgs = @()
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$runFsx = Join-Path $root 'pc\tools\run.fsx'
if (-not $UsersRoot) { $UsersRoot = Join-Path $root 'build\dolphin-users' }

$dolphin = Join-Path $DolphinDir 'Dolphin.exe'
if (-not (Test-Path $dolphin)) { throw "Dolphin.exe not found in $DolphinDir (set -DolphinDir or GCOOP_DOLPHIN_DIR)" }

if (-not $Iso) {
    $Iso = Get-ChildItem (Join-Path $DolphinDir 'wii games') -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match 'SB4E|Galaxy 2|Galaxy2' -and $_.Extension -match '\.(iso|rvz|wbfs|gcz|wia|ciso)$' } |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $Iso) { throw "could not find a Super Mario Galaxy 2 image; pass -Iso" }
}
Write-Host "game image: $Iso"

# Seed each player's user directory with the main Dolphin config so controllers are mapped.
$mainUser = if (Test-Path (Join-Path $DolphinDir 'portable.txt')) { Join-Path $DolphinDir 'User' } else { Join-Path $env:APPDATA 'Dolphin Emulator' }
$userDirs = @()
for ($i = 1; $i -le $Players; $i++) {
    $dir = Join-Path $UsersRoot "p$i"
    if (-not (Test-Path (Join-Path $dir 'Config')) -and (Test-Path (Join-Path $mainUser 'Config'))) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Copy-Item -Recurse (Join-Path $mainUser 'Config') (Join-Path $dir 'Config')
        Write-Host "seeded $dir from $mainUser\Config"
    }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $userDirs += $dir
}

if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot 'build-smg2.ps1') -DeployTo $userDirs
    dotnet build (Join-Path $root 'pc\GalaxyCoop.sln') -c Debug --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "PC build failed" }
}
else {
    & (Join-Path $PSScriptRoot 'build-smg2.ps1') -DeployOnly -DeployTo $userDirs
}

if (-not $NoServer) {
    Start-Process dotnet -ArgumentList @('fsi', "`"$runFsx`"", 'server', '--port', $Port) | Out-Null
    Start-Sleep -Seconds 4
}

for ($i = 1; $i -le $Players; $i++) {
    $dir = $userDirs[$i - 1]
    $riivRoot = Join-Path $dir 'Load\Riivolution'
    $xml = Join-Path $riivRoot 'riivolution\GalaxyCoop.xml'
    if (-not (Test-Path $xml)) { throw "$xml missing; run without -NoBuild once" }

    $descriptor = [ordered]@{
        type = 'dolphin-game-mod-descriptor'
        version = 1
        'base-file' = $Iso
        'display-name' = "Galaxy Co-op (player $i)"
        riivolution = @{
            patches = @(@{
                xml = $xml
                root = $riivRoot
                options = @(@{ 'section-name' = 'Galaxy Co-op'; 'option-name' = 'Galaxy Co-op'; choice = 1 })
            })
        }
    }
    $json = Join-Path $dir 'GalaxyCoop.json'
    [IO.File]::WriteAllText($json, ($descriptor | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding $false))

    $proc = Start-Process $dolphin -ArgumentList @('-u', "`"$dir`"", '-e', "`"$json`"") -PassThru
    Write-Host "player ${i}: Dolphin pid $($proc.Id), user dir $dir"
    Start-Sleep -Seconds 2
    $companionArgList = @('fsi', "`"$runFsx`"", 'companion', 'connect', '--server', "127.0.0.1:$Port", '--name', "P$i", '--pid', $proc.Id) + $CompanionArgs
    Start-Process dotnet -ArgumentList $companionArgList | Out-Null
}

Write-Host "launched $Players player(s). Enter a galaxy in the first window (the host); the others follow."
