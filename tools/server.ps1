# Runs the relay server: .\tools\server.ps1 [--port 7777] [--password X] [--max-players 8]
# Players connect to UDP <your public IP>:7777, so forward that port for internet play.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $root 'pc\src\GalaxyCoop.Server') -c Debug --nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "server build failed" }
dotnet fsi (Join-Path $root 'pc\tools\run.fsx') server @args
exit $LASTEXITCODE
