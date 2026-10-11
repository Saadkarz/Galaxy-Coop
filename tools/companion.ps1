# Runs the companion: .\tools\companion.ps1 connect --server 1.2.3.4 --name Fahd
# (also: attach, loopback --offset 0,0,150). Goes through pc/tools/run.fsx so
# Smart App Control cannot block the locally built DLLs.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $root 'pc\src\GalaxyCoop.Companion') -c Debug --nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "companion build failed" }
dotnet fsi (Join-Path $root 'pc\tools\run.fsx') companion @args
exit $LASTEXITCODE
