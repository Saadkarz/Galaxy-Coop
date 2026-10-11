# SMG2 compatibility checklist

Fill this in while playing with two or more players. Stage names are the
internal `StageData` names; the companion prints the same name in its status
line (`game IslandFleetGalaxy #1 ...`), so you never need to translate them.

Legend: `ok` works, `-` broken (add a note), blank = not tested yet.

What each column checks:

| Column | Check |
|---|---|
| Ghost | The other player is visible, animates and stands on spherical/rotated gravity correctly |
| Coins | A coin taken by one player disappears for the other |
| Bits | Star bits, including the ones that come from groups (rings, rails) |
| Stars | With `--share-stars`, one player's Power Star gives the other the star too |
| Switches | With `--sync-switches`, doors/platforms/gates opened by one open for both |
| Enemies | With `--sync-enemies`, an enemy defeated by one is gone for the other |
| Follow | Non-hosts are taken to the host's galaxy and scenario |

When something is wrong, the mailbox debug counters are the first thing to
look at (`companion attach` prints them): a rising `key mismatch` count means
the two games build objects in a different order in that galaxy and the
object keys don't match.

## Galaxies

| Stage | Ghost | Coins | Bits | Stars | Switches | Enemies | Follow | Notes |
|---|---|---|---|---|---|---|---|---|
| IslandFleetGalaxy | | | | | | | | |
| YosshiHomeGalaxy | | | | | | | | Yoshi riding is not mirrored on the ghost |
| FlowerHighlandGalaxy | | | | | | | | |
| BigTree2Galaxy | | | | | | | | |
| TamakoroPlanetGalaxy | | | | | | | | Star Ball: ghost shows Mario only |
| TamakoroSliderGalaxy | | | | | | | | |
| CloudGardenGalaxy | | | | | | | | Cloud power-up not mirrored |
| MokumokuValleyGalaxy | | | | | | | | |
| HoneyBeeVillageGalaxy | | | | | | | | Bee power-up not mirrored |
| GoroRockGalaxy | | | | | | | | Rock power-up not mirrored |
| WhiteSnowGalaxy | | | | | | | | |
| DigMineGalaxy | | | | | | | | Spin Drill: other side of planets |
| StarCreekGalaxy | | | | | | | | |
| TwisterTowerGalaxy | | | | | | | | |
| SwitchMoveBlockGalaxy | | | | | | | | Switch-heavy |
| TimerSwitchingPlatformGalaxy | | | | | | | | Timed switches: expect desyncs |
| RedBlueExGalaxy | | | | | | | | Flip-swap panels |
| AbekobeGalaxy | | | | | | | | Upside-down gravity |
| ChaosGravityGalaxy | | | | | | | | |
| BigWaterFallGalaxy | | | | | | | | |
| QuicksandGalaxy | | | | | | | | |
| TropicalResortGalaxy | | | | | | | | |
| JungleGliderGalaxy | | | | | | | | Fluzzard races |
| ChallengeGliderGalaxy | | | | | | | | |
| GhostConveyorGalaxy | | | | | | | | |
| TeresaLabyrinthGalaxy | | | | | | | | Boo power-up not mirrored |
| UnderGroundDangeonGalaxy | | | | | | | | |
| AncientExcavationGalaxy | | | | | | | | |
| KachikochiLavaGalaxy | | | | | | | | |
| MagicGalaxy | | | | | | | | |
| MysteryCandyGalaxy | | | | | | | | |
| OmoteuLandGalaxy | | | | | | | | |
| WanwanFactoryGalaxy | | | | | | | | |
| MadnessOnimasuGalaxy | | | | | | | | |
| MarioOnMarioGalaxy | | | | | | | | |
| MemoryRoadGalaxy | | | | | | | | |
| LongForCastleGalaxy | | | | | | | | |
| DimensionBigCastleGalaxy | | | | | | | | |
| ThunderFleetGalaxy | | | | | | | | |
| BigGalaxy | | | | | | | | |
| KingOfKingsGalaxy | | | | | | | | |
| HomeCenterGalaxy | | | | | | | | |
| VsKoopaJrLv1Galaxy | | | | | | | | Boss: each player fights their own copy |
| KoopaJrLv2Galaxy | | | | | | | | Boss |
| VsKoopaJrLv3Galaxy | | | | | | | | Boss |
| VsKoopaLv1Galaxy | | | | | | | | Boss |
| VsKoopaGravityGalaxy | | | | | | | | Boss |
| VsKoopaLv3Galaxy | | | | | | | | Final boss |

## Hubs and special scenes

The mod's per-frame work runs from `MarioActor`, so scenes without a Mario
actor (world maps, file select, staff roll) show no ghosts and don't react to
travel requests; a pending "follow the host" request is carried out once the
player is in a scene that has Mario.

| Stage | Expected | Result | Notes |
|---|---|---|---|
| MarioFaceShipGalaxy | Ghosts visible; follow works from here | | Starship Mario |
| WorldMap01Galaxy ... WorldMap08Galaxy | No ghosts, follow waits | | |
| FileSelect | Nothing | | |
| PeachCastleGalaxy | Ghosts visible | | Intro |
| ChildRoomGalaxy | Ghosts visible | | |
| StaffRollGalaxy | Nothing | | |

## Known limits (all galaxies)

- Enemy sync only covers the classes listed in `GCOOP_ENEMY_TABLE`
  (`game/smg2/modules/GalaxyCoop/include/GCoop/Addresses.h`). Bosses and
  everything else stay per-player.
- Power-up forms, Yoshi, and held objects are not mirrored on the ghost; it
  plays Mario's/Luigi's normal animations.
- The ghost doesn't collide with anything and can't be hurt.
- Coins, star bits and stars collected while a player is in a different
  scenario of the same galaxy are not shared (state is per stage + scenario).
