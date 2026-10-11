# One-time toolchain setup for Galaxy Co-op.
#
#  - Kamek linker (prebuilt release)              -> third_party/Syati/deps/Kamek
#  - CodeWarrior PPC command line tools           -> third_party/Syati/deps/CodeWarrior
#    (from NXP's free "CodeWarrior for MPC55xx/MPC56xx v2.10 Special Edition";
#     if you own the Wii-licensed 4.3.0.172 compiler, copy its mwcceppc.exe /
#     mwasmeppc.exe there instead and the build will use -proc gekko)
#  - SMG2 main.dol + file list extracted from your own disc image, used only
#    for offline hook verification (tools/verify_addresses.py)
#
# Needs: git, python 3.12+, .NET 8 SDK, 7-Zip.
param(
    [string]$Smg2Iso = "D:\Games\Dolphin-x64\wii games\Super Mario Galaxy 2 (USA) (En,Fr,Es).nkit.iso",
    [string]$DolphinDir = "D:\Games\Dolphin-x64"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$deps = Join-Path $root 'third_party\Syati\deps'
$dl = Join-Path $root 'third_party\_downloads'
New-Item -ItemType Directory -Force $deps, $dl | Out-Null

git -C $root submodule update --init --recursive

# Kamek
$kamekDir = Join-Path $deps 'Kamek'
if (-not (Test-Path "$kamekDir\Kamek.exe")) {
    Write-Host 'Downloading Kamek...'
    $zip = Join-Path $dl 'kamek-win-x64.zip'
    Invoke-WebRequest 'https://github.com/Treeki/Kamek/releases/download/2026-02-24/win-x64.zip' -OutFile $zip
    $x = Join-Path $dl 'kamek'
    Expand-Archive $zip -DestinationPath $x -Force
    New-Item -ItemType Directory -Force $kamekDir | Out-Null
    Copy-Item "$x\Kamek.exe", "$x\Kamek.dll.config" $kamekDir
}

# CodeWarrior (free Special Edition): pull only the command line tools out of the MSI
$cwDir = Join-Path $deps 'CodeWarrior'
if (-not (Test-Path "$cwDir\mwcceppc.exe")) {
    $sevenZip = 'C:\Program Files\7-Zip\7z.exe'
    if (-not (Test-Path $sevenZip)) { throw '7-Zip is required (winget install 7zip.7zip)' }
    $exe = Join-Path $dl 'CW55xx_v2_10_SE.exe'
    if (-not (Test-Path $exe)) {
        Write-Host 'Downloading CodeWarrior Special Edition from NXP (~250 MB)...'
        Invoke-WebRequest 'http://cache.nxp.com/lgfiles/devsuites/PowerPC/CW55xx_v2_10_SE.exe' -OutFile $exe -UseBasicParsing
    }
    $cwx = Join-Path $dl 'cwx'
    & $sevenZip x $exe "-o$cwx" -y | Out-Null
    $msi = Get-ChildItem $cwx -Filter *.msi | Where-Object Name -like 'CodeWarrior*' | Select-Object -First 1
    $map = python "$root\tools\msi_files.py" $msi.FullName
    $wanted = @{}
    foreach ($line in $map) {
        $k, $p = $line -split "`t"
        if ($p -match 'Command_Line_Tools/[^/]+\.(exe|dll|txt)$' -or $p -match '2\.10/license\.dat$') { $wanted[$k] = Split-Path $p -Leaf }
    }
    $keys = Join-Path $dl 'cw_keys'
    & $sevenZip e (Join-Path $cwx 'Data1.cab') "-o$keys" -y @($wanted.Keys) | Out-Null
    New-Item -ItemType Directory -Force $cwDir | Out-Null
    foreach ($k in $wanted.Keys) { Copy-Item (Join-Path $keys $k) (Join-Path $cwDir $wanted[$k]) -Force }
}

# Disc extraction for offline verification
$disc = Join-Path $dl 'smg2_disc'
if (-not (Test-Path "$disc\DATA\sys\main.dol")) {
    if (-not (Test-Path $Smg2Iso)) { throw "SMG2 image not found: $Smg2Iso" }
    Write-Host 'Extracting SMG2 data partition (for hook verification only)...'
    & (Join-Path $DolphinDir 'DolphinTool.exe') extract -i $Smg2Iso -o $disc -g -q
}

python "$root\tools\verify_addresses.py" | Select-Object -Last 1
Write-Host 'Setup complete. Build with: .\tools\build-smg2.ps1'
