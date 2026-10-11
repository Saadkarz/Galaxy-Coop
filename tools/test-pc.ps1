# Builds and runs the PC unit tests: .\tools\test-pc.ps1 [name filter]
# Uses the in-memory runner because Smart App Control may block the test DLL
# from the regular `dotnet test` host.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $root 'pc\GalaxyCoop.sln') -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "build failed" }
Push-Location $root
try { dotnet fsi (Join-Path $root 'pc\tools\run.fsx') test @args } finally { Pop-Location }
exit $LASTEXITCODE
