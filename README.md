# Galaxy Co-op

Online co-op for **Super Mario Galaxy 2** (USA, `SB4E01`) on Dolphin, in the
spirit of sm64coopdx: every player runs their own game, and you see each
other and share a world. SMG1 support is planned to follow, using the same PC
side.

Three parts:

```
 Dolphin + SMG2 + GalaxyCoop module  <-- memory mailbox -->  companion  <-- UDP -->  server
 (game/smg2, C++ via Syati/Kamek)                            (pc/, C#)              (pc/, C#)
```

- The **game module** writes Mario's state into a mailbox in game RAM every
  frame, draws the other players as Mario/Luigi ghosts, and reports and
  applies world events (coins, star bits, Power Stars, switches, enemy
  defeats, deaths).
- The **companion** attaches to Dolphin's memory, finds the mailbox, and
  bridges it to the network. It also smooths remote players over time
  (interpolation).
- The **launcher** (`tools\play.ps1`) starts Dolphin with the patch and
  answers the **in-game co-op menu**: pick Mario or Luigi, host a private or
  public game or join one, all from inside the game. It runs the server and
  the companion for you.
- The **server** relays player positions and decides which world events
  count. For example, if two players grab the same coin, only one event goes
  through. It also remembers what has already happened in each galaxy so
  late joiners catch up, and tells everyone else to follow the host (the
  first player to join) when the host changes galaxy.

## What is synced

| Thing | Default | Flag |
|---|---|---|
| Player position, rotation, gravity, animation, Mario/Luigi | on | |
| Coins, star bits | on | |
| Following the host to their galaxy and scenario | on | `--no-follow-host` to turn off |
| Power Stars (one player gets it, everyone gets it) | off | `--share-stars` |
| Stage switches (doors, gates, moving platforms) | off | `--sync-switches` |
| Enemy defeats (Goombas, Boos, Octoombas, ...) | off | `--sync-enemies` |
| Deaths | logged in the companion window | |

Each companion passes its flags to its own game, and a game only sends and
applies an opt-in event type when its flag is on. Turn the same flags on for
every player. See
[docs/compatibility-smg2.md](docs/compatibility-smg2.md) for the per-galaxy
test checklist and known limits.

## Requirements

- Windows, Dolphin (tested layout: `D:\Games\Dolphin-x64`) and your own SMG2
  USA disc image.
- Git, Python 3.12+, .NET 8 SDK, 7-Zip.

## One-time setup

```powershell
.\tools\setup.ps1 -Smg2Iso "D:\Games\Dolphin-x64\wii games\<your SMG2 image>"
```

This fetches the Syati submodule and the Kamek linker, extracts the
CodeWarrior command-line compiler from NXP's free Special Edition, and
extracts `main.dol` from your disc so every hook address can be checked
offline (`python tools/verify_addresses.py`).

The free compiler isn't licensed for the Wii's Gekko CPU, so the build uses
`-proc generic` with patched copies of the headers. The generated code is
plain PowerPC and runs fine on the Wii. If you own the Wii-licensed
CodeWarrior (4.3.0.172), put `mwcceppc.exe`/`mwasmeppc.exe` in
`third_party/Syati/deps/CodeWarrior` and the build switches to `-proc gekko`
by itself.

## Build

```powershell
.\tools\build-smg2.ps1            # verify addresses, build, install into Dolphin's Load\Riivolution
.\tools\build-smg2.ps1 -Debug     # same, with extra OSReport logging
dotnet build pc\GalaxyCoop.sln
.\tools\test-pc.ps1               # PC-side unit + end-to-end tests
```

## Playing

### With the in-game menu (recommended)

```powershell
.\tools\play.ps1              # builds, starts Dolphin with the patch, serves the in-game menu
.\tools\play.ps1 -NoBuild
```

Keep the launcher's console window open while you play. Load your save as
usual. The **Galaxy Co-op menu** opens by itself the first time you're in
control of Mario, and **-** (minus) opens and closes it after that. Use the
D-pad to move, **A** to select, **Left/Right** to change a value, and **B** to
go back. Mario stands still while it's open.

- **Character: Mario / Luigi.** The choice is remembered. Closing the menu
  reloads the stage to apply it. That reload currently crashes Dolphin. See
  [CONTRIBUTING.md](CONTRIBUTING.md) before changing it.
- **Host a private game:** your PC runs the server and nothing is listed. The
  menu shows the invite address to send to your friends. The launcher tries
  to open the UDP port on your router with UPnP. If the router doesn't answer,
  the menu says so, and you forward the port (default 7777) by hand.
- **Host a public game:** the same, and the game is also listed on the lobby
  server so anyone can find it under *Join a public game*.
- **Join a public game:** the list of public games (with *Refresh*). Games
  with a password ask for it.
- **Join by address:** type the host's address (`203.0.113.7:7777`) with the
  D-pad: Up/Down change the letter under the cursor, Left/Right move it, 1
  deletes. You're then asked for a password; leave it empty if there's none.
- **Shared Power Stars / enemy defeats / switches:** on by default. Every
  player sets them for their own game, so agree on them.

While you're in a game, the menu shows its status, the invite address, the
players and their characters, and **Leave the game** (**Stop hosting** for
the host).

The settings file (`%APPDATA%\GalaxyCoop\launcher.json`) holds your name, the
Dolphin folder, the game image, the lobby server, the port and the room name.
`.\tools\play.ps1 -Web` opens the old browser page instead, which edits the
same settings.

#### The public room list (lobby)

Public rooms are listed by a small lobby server. Run it on a machine everyone
can reach, such as a cheap VPS or a PC with UDP 7700 forwarded:

```powershell
.\tools\lobby.ps1             # UDP 7700
```

Then set that machine's address (`lobby.example.com:7700`) as the lobby server
in each player's launcher settings. The default, `127.0.0.1:7700`, only works
for testing on one PC. Hosts re-register every 5 seconds, and a room
disappears 20 seconds after its host stops. Joining always goes straight to
the host; the lobby only hands out addresses.

#### Two players on one PC through the launcher

```powershell
.\tools\lobby.ps1                   # optional, for testing public games
.\tools\play.ps1                    # player 1: host from the in-game menu
.\tools\play.ps1 -NoBuild -Second   # player 2: own settings file and Dolphin user folder
```

Player 2 joins with *Join by address* `127.0.0.1:7777`, or through the public
list.

### By hand (server + companion)

1. **One person runs the server** (forward UDP 7777 for internet play):
   ```powershell
   .\tools\server.ps1 --port 7777 --password secret
   ```
2. **Every player** starts SMG2 in Dolphin with the patch: right-click the
   game, choose *Start with Riivolution Patches*, enable **Galaxy Co-op**, then
   *Launch*.
3. **Every player** starts their companion:
   ```powershell
   .\tools\companion.ps1 connect --server 203.0.113.7 --name Fahd --password secret
   .\tools\companion.ps1 connect --server 203.0.113.7 --name Bro --character luigi --sync-enemies --sync-switches --share-stars
   ```
   `--character mario|luigi` forces the character. Leave it out to keep the
   save file's character. A public server is
   `.\tools\server.ps1 --public "Room name" --lobby lobby.example.com`.
4. The first player to connect is the host. When the host enters a galaxy,
   everyone else is taken there. Players see each other whenever they are in
   the same galaxy and scenario.

Other companion modes, useful for checking that one game is working on its own:

```powershell
.\tools\companion.ps1 attach                         # live mailbox dump and debug counters
.\tools\companion.ps1 loopback --offset 0,0,150      # your own Mario echoed back as a ghost
```

### Local test with several Dolphins on one PC

```powershell
.\tools\launch-two.ps1                                   # server + 2 Dolphins + 2 companions
.\tools\launch-two.ps1 -Players 3 -NoBuild -CompanionArgs --sync-enemies,--sync-switches
```

Each Dolphin gets its own user directory under `build\dolphin-users\pN`,
copied from your main Dolphin config so controllers are already mapped.

## Smart App Control

On Windows 11 with Smart App Control on, Windows refuses to load unsigned
programs and DLLs it doesn't recognise. That includes freshly built .NET
assemblies, and on some setups Dolphin itself.

- The scripts above run the companion, server and tests through
  `dotnet fsi pc\tools\run.fsx`. F# Interactive is signed by Microsoft and
  loads our assemblies from memory, so Smart App Control doesn't block them.
- If Dolphin won't start, the only fix is to turn Smart App Control off
  (Windows Security > App & browser control > Smart App Control). Windows
  doesn't let you turn it back on afterwards without reinstalling.

## Repository layout

| Path | What |
|---|---|
| `shared/mailbox.h` | Mailbox layout shared by the game module and the companion (`pc/src/GalaxyCoop.Protocol/Mailbox.cs` mirrors it; a test checks they match) |
| `game/smg2/modules/GalaxyCoop` | The game module: hooks, ghosts, collectibles, world sync |
| `game/smg2/modules/GalaxyCoop/include/GCoop/Addresses.h` | Every SMG2 address the module touches, each annotated with the symbol it must match |
| `pc/src/GalaxyCoop.Protocol` | Mailbox layout, packet format, reliable channel, interpolation |
| `pc/src/GalaxyCoop.Companion` | Dolphin attach + mailbox bridge + network client |
| `pc/src/GalaxyCoop.Server` | UDP relay, event arbitration, per-galaxy state, public room lobby (`--lobby-mode`) |
| `pc/src/GalaxyCoop.Launcher` | The launcher: starts Dolphin, answers the in-game menu (`InGameLauncher.cs`), private/public hosting, UPnP; optional web page (`wwwroot/`, `--web`) |
| `pc/tests` | Unit and end-to-end tests (fake game memory, fake network) |
| `tools/` | Setup, build, launch and offline verification scripts (`dolinspect.py` disassembles and looks up symbols in `main.dol`) |

## Status

Open bugs, and where to start on them, are in
[CONTRIBUTING.md](CONTRIBUTING.md).

Everything except in-game behaviour has been checked offline: hook addresses
against `main.dol`, the mailbox layout on both sides, and the network stack
end-to-end with fake games. In-game behaviour still needs a real play test,
especially:

- ghost animation sync: the ghost replays the same body animation, frame,
  speed and blend weights as the real player using Mario's own animation
  table, plus the upper-body layer (carrying, spin, waving) on Spine1;
- the in-game menu's drawing (it draws in the layout pass with raw GX) and
  the stage reload after a character change;
- choosing Luigi before Luigi is unlocked in the save;
- Luigi's model loading in galaxies where you play as Mario;
- object keys matching between two players' games in every galaxy;
- remote Power Star collection;
- switch side effects in switch-heavy galaxies.
