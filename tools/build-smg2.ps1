# Builds the SMG2 module + Syati loader and deploys them to Dolphin.
#   .\tools\build-smg2.ps1                      # default Dolphin user dir
#   .\tools\build-smg2.ps1 -Debug               # extra OSReport logging
#   .\tools\build-smg2.ps1 -DeployTo D:\DolphinUsers\p1,D:\DolphinUsers\p2
#   .\tools\build-smg2.ps1 -DeployOnly          # install the last build without compiling
param(
    [switch]$Debug,
    [switch]$NoDeploy,
    [switch]$DeployOnly,
    [string[]]$DeployTo = @()
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

python "$root\tools\verify_addresses.py" | Select-Object -Last 1
if ($LASTEXITCODE -ne 0) { throw "hook address verification failed" }

$pyArgs = @("$root\tools\build_smg2.py")
if ($Debug) { $pyArgs += '--debug' }
if ($NoDeploy) { $pyArgs += '--no-deploy' }
if ($DeployOnly) { $pyArgs += '--deploy-only' }
foreach ($d in $DeployTo) { $pyArgs += @('--deploy-to', $d) }
python @pyArgs
if ($LASTEXITCODE -ne 0) { throw "build failed" }
