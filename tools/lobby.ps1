# Runs the public room list that "Public" hosts register with and the Join tab
# reads. Run it on a machine everyone can reach (a VPS, or a PC with UDP 7700
# forwarded), then set that address as the lobby server in each launcher's Settings.
#   .\tools\lobby.ps1
#   .\tools\lobby.ps1 -Port 7701
param([int]$Port = 7700)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet fsi (Join-Path $root 'pc\tools\run.fsx') server --lobby-mode --port $Port
