# Helping with Galaxy Co-op

This file is for the next person who wants to move the mod forward. It
covers what the project is trying to be, where the code lives, and the bugs
that are still open after playing it.

Setup, launch commands, and the sync table are in [README.md](README.md).
The per-galaxy checklist is in [docs/compatibility-smg2.md](docs/compatibility-smg2.md).
That checklist is older than the boss, comet, and Yoshi code below. Treat a
blank cell as "not played", not as "known good".

## Goal

Online co-op for **Super Mario Galaxy 2** (USA disc `SB4E01`) on Dolphin,
in the spirit of sm64coopdx. Each person runs their own game. Players in the
same galaxy see each other, and the world stays in step: coins, star bits,
Power Stars, switches, enemy defeats, comet stars, and bosses. A second
player who joins a save that is already on the Starship should arrive on the
Starship, not in the opening story.

SMG1 is planned later, on the same PC side. The game module is SMG2-only.

The game itself is not in this repository. You need your own USA disc image.
Do not commit ISOs, extracted discs, or the CodeWarrior compiler.
`tools/setup.ps1` fetches those locally.

## How a frame works

```
Dolphin + SMG2 + GalaxyCoop module  --mailbox in RAM-->  companion  --UDP-->  server
```

1. The module hooks the running game and, once per frame, writes the local
   player into a mailbox (version 5, `shared/mailbox.h`).
2. The C# companion finds that mailbox inside Dolphin's memory and sends a
   snapshot. It writes the other players, chat, boss pose, and world events
   back.
3. The server is authoritative for events. Two players grabbing one coin
   produce one event. It remembers galaxy state so a late joiner can catch
   up, and it tells everyone to follow the host when the host changes galaxy.

The in-game menu does not open sockets. It only reads and writes the mailbox.
`tools/play.ps1` starts Dolphin and the companion. Hosting and joining happen
from the menu after you are in a galaxy (the Starship counts).

## Where the code is

### Game module

`game/smg2/modules/GalaxyCoop`, C++ on [Syati](https://github.com/SMGCommunity/Syati)
and Kamek, built with CodeWarrior (`tools/build-smg2.ps1`). Every address the
module patches is in `include/GCoop/Addresses.h` with a `@sym`, `@call`, or
`@branch` note. `python tools/verify_addresses.py` checks those notes against
`main.dol` and `third_party/Syati/symbols/SB4E.txt` before the build. If you
add a hook, add the note. `python tools/dolinspect.py dis 0x80451480 40`
disassembles; the second number is an instruction count, not an end address.

Syati compiles every `.cpp` under `source/` by itself.

| File | Job |
|---|---|
| `Hooks.cpp` | `MarioActor::init` / `movement`. Scene start and the per-frame tick. |
| `Mailbox.cpp` | Creates the mailbox. Does not wipe an existing one on the next scene. |
| `LocalPlayerSync.cpp` | Writes Mario's pose, animation, and the Yoshi flag. |
| `RemotePlayers.cpp` | Ghost actors (`GCoopRemotePlayer`), body and upper-body animation, Yoshi mount. |
| `ObjectRegistry.cpp` | Stable object keys from placement data. Cleared at the start of `GameScene::init`. |
| `Collectibles.cpp` | Coins, star bits, Power Stars. |
| `Events.cpp` | Incoming event apply. Sets `gApplyingRemote` so a hook does not echo the event back out. |
| `WorldSync.cpp` | Switches, enemy defeats, deaths, follow-the-host. `requestChangeStageInGame` is `0x804D8510`. |
| `Boss.cpp` | Finds bosses by name when they are registered, copies pose, summons a far player once. |
| `Character.cpp` | Replaces `isLuigiPlayer` (`0x804D3040`). Locks Mario or Luigi for the scene about to be built. |
| `Menu.cpp` | Galaxy-style menu, name card, chat, name tags. Drawn in the layout pass. |

`GameScene::init` (`0x80451480`, the call that mounts the player archive is
at `0x804514A8`) always asks `isPlayerLuigi`, then
`requestChangeArchivePlayer` (`true` keeps Mario, `false` loads Luigi), and
later waits with `waitEndChangeArchivePlayer` before actors load.
`lockSceneCharacter` runs at the start of our `GameScene::init` hook so that
call sees the choice. The model cannot change in the middle of a scene.
Physics and voices go through the same `isLuigiPlayer` hook, so they have to
keep matching the model that was loaded.

### PC

| Project | Job |
|---|---|
| `pc/src/GalaxyCoop.Protocol` | Mailbox offsets (`Mailbox.cs` must match `shared/mailbox.h`; a test parses every `#define`), packets, reliable channel, interpolation. |
| `pc/src/GalaxyCoop.Companion` | Find Dolphin, read and write the mailbox, UDP client. |
| `pc/src/GalaxyCoop.Server` | Relay, event arbitration, per-galaxy stage state, lobby mode. |
| `pc/src/GalaxyCoop.Launcher` | Starts Dolphin, answers the in-game menu, UPnP, optional `--web` page. |
| `pc/tests` | Unit and end-to-end tests with fake memory and a fake network. |

`dotnet test pc/tests/GalaxyCoop.Tests` (or `tools/test-pc.ps1`) should stay
green. On Windows 11 with Smart App Control, run PC programs through
`dotnet fsi pc/tools/run.fsx`, which is what the scripts already do.

Network protocol version is 2. Chat and rename are reliable. Boss pose is an
unreliable message and is not part of `PlayerState`. The server does not
echo chat to the sender. The game draws its own sent line locally.

## What has been seen working

Played with two Dolphins on one PC (`tools/play.ps1` and
`tools/play.ps1 -NoBuild -Second`), both on the Starship:

- The co-op menu opens in a galaxy. The letters are all there (it uses
  `MR::getFontOnCurrentLanguage()`, the dialogue font, not `MenuFont64`).
- Hosting a public game, the player list, and name tags over each player.
- The companion attaches and finds the mailbox.

`MenuFont64` is a scan font with only some letters. Do not switch the menu
back to it. The missing letters were B, J, K, M, O, Q, V, X, Z, and most
lowercase.

## Current bugs

These are ordered by how badly they block play.

### 1. Changing character crashes Dolphin

**What you do.** In a galaxy, open the menu, Left/Right on `Character`, close
the menu.

**What happens.** Dolphin reports `GFX FIFO: Unknown Opcode (0x60)` and then
a purple exception. The stack runs through `MarioAnimator`'s constructor,
`J3DModel::calc`, and `loadTexNo`, during `GameScene::calcAnim`. The new
scene is building Mario's model from a player archive that is still being
swapped.

**Where.** `Menu.cpp` `applyCharacter` runs when the menu closes.
`Character.cpp` `beginCharacterReload` calls
`requestChangeArchivePlayer` while the old scene is still live, then
`GCoop::requestChangeStageInGame` (`WorldSync.cpp`, `0x804D8510`).

**What not to call.**

- `requestChangeStage` at `0x804D8410`. That is the scenario-select entry.
  Calling it from a live galaxy jumps to unmapped memory (`ISI`, `SRR0`
  `0x91913D70`).
- `restartCurrentStageAndClearAfterMiss` (`0x804D6840`). It does reach the
  right stage-change entry, but `clearAfterMiss` zeros the selected scenario
  and dumps the player on the early Starship garden (the compass with Lubba).
  It also never mounts Luigi's archive, so the player stays Mario.

**The safe place to change character** is the next `GameScene::init`, which
already waits for the archive before it creates actors. `lockSceneCharacter`
reads `menu.character` (1 Mario, 2 Luigi) at the start of that init. A choice
stored in the mailbox before the next real scene load (entering a galaxy,
returning to the Starship, continuing from the title) is the path that uses
the game's own loader. Reloading the scene you are standing in, from
`MarioActor::movement`, is what crashes.

The title screen (`Press A and B`) is stage `FileSelect`. It has no
`MarioActor`, and the menu is created from `MarioActor::init`, so nothing
draws there. A character select drawn from `GameScene::update` was tried and
did not show up on that screen, and it was reverted. A select that appears
on the title, stores the choice, and lets the Starship's `GameScene::init`
mount the archive is the fix to build. Do not reload the current galaxy to
do it.

### 2. The title screen has no name entry and no character choice

The name card (Minus to edit, top-left, so it does not cover the logo) only
exists in scenes that have Mario. `FileSelect` does not. Players cannot type
a name or pick Luigi before the story starts. The full menu must stay off
this screen. It covers the logo, and calling `offPlayerControl` here eats
`Press A and B`.

### 3. A joiner on a used save still needs a real play test

If player 2 joins while the host is already on the Starship, player 2 should
leave `PeachCastleGalaxy` or a `Storybook` stage and land on the host's
stage. `WorldSync.cpp` allows that travel during the intro and refuses it on
`FileSelect` and world maps. `EndToEndTests.JoinerInTheOpeningStoryIsSentToTheShip`
covers the messages. Nobody has confirmed it in the game yet.

### 4. Bosses, comet stars, and Yoshi are implemented and unconfirmed

- **Bosses** (`Boss.cpp`). Registered from `NameObjRegister::add`
  (`0x80343E34`) when the name looks like a boss. The player near one writes
  `bossOut`. Others copy translation and rotation, zero velocity, and teleport
  to the boss once per key if they are more than about 1800 units away. The
  host's pose wins while the host is fighting. Two open risks: a `NameObj`
  that matches the name filter but is not a `LiveActor` will crash when the
  pose is applied, and a boss that does not keep its pose in `mTranslation` /
  `mRotation` will not move. `docs/compatibility-smg2.md` still says each
  player fights their own copy. That was true before this code.
- **Comet stars.** `MR::startGalaxyCometEvent` / `endGalaxyCometEvent`
  (`0x80024F20`, `0x80024F30`) are hooked and replayed to late joiners while
  the comet is on. If the star already spawned without the comet, turning the
  event on may not reveal it. A stage reload would, and that reload is not
  implemented.
- **Yoshi.** A ghost with the Yoshi flag spawns a `LiveActor` named `Yoshi`
  and plays `YoshiWait` or `YoshiRun`. The model name was not checked against
  `Yoshi.arc` on a machine that had the archive. The compatibility notes still
  say Yoshi is not shown.

### 5. Ghost animation was wrong once and has not been rechecked

The ghost is supposed to replay the body clip (name, frame, speed, blend
weights) and the upper-body layer. That was added after animation was
reported out of sync. The last play session did not look at it again.

### 6. UPnP often does not open the port

Hosting a public game can show `no UPnP router answered; forward the port by
hand` and a LAN invite such as `192.168.100.42:7777`. Same-PC play with
`127.0.0.1:7777` still works. Internet play needs a manual forward of UDP
7777, or a better NAT path.

### 7. The checklist and a few comments are behind the code

`docs/compatibility-smg2.md` says bosses, Yoshi, and power-ups are not
synced. Boss pose sync and the Yoshi mount exist now. Power-ups (bee, cloud,
rock, boo, spin drill) still are not mirrored. The in-game menu turns on
shared stars, enemy defeats, and switches by default. The command-line
companion still requires `--share-stars`, `--sync-enemies`, and
`--sync-switches`.

## Working on a fix

- Build with `tools/build-smg2.ps1` and launch with `tools/play.ps1`. A
  Dolphin that is already running keeps the previous module until you close
  it.
- Player 2 on the same PC is `tools/play.ps1 -NoBuild -Second`. It has its
  own Dolphin user folder and `launcher-p2.json`.
- Keep `python tools/verify_addresses.py` and `dotnet test pc/tests/GalaxyCoop.Tests`
  green.
- Hook the symbol `tools/dolinspect.py` shows, not a guessed address. The
  stage-change crash above was a guessed entry point.
- The menu's orthographic matrix is `C_MTXOrtho(0, 480, 0, width)` with width
  853 in 16:9 and 640 in 4:3. Name-tag positions are
  `MR::calcScreenPosition` scaled into that space. Do not switch the menu to
  `getScreenWidth()` on its own.
