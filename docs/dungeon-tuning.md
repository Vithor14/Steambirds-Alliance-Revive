# Dungeon tuning, Mission Transport and Nexus collision (22/11/2026)

Compiled, not yet tested in the game. Written after the test report of 22/11 (Nexus and the dungeons Iron Lair, Bomb Factory,
Laser Playground, The Arena and Home for Special Children).

## Host settings (`Server\dungeons.json`, also in the launcher: Server tab, "Maps and dungeons")

| Key | Default | What it does |
|---|---|---|
| `nexusMap` | `nexus` | Map file (`maps\*.json`, no extension) of the Nexus (Rebel City). A name without a file falls back to `nexus` (logged once) |
| `tutorialMap` | `tutorial_walker` | Map file of the walker tutorial, same rules. The tutorial map must keep what the tutorial script needs (checked when the file is read) |
| `entryMode` | `both` | How players enter dungeons: `both` (carriers outside the city and the Mission Transport), `portals`, `missionTransport` |
| `lockBossRooms` | `false` | `true`: the doors of a boss room close behind the player until the boss dies (the original behaviour) |
| `fireRateScale` | `1` | Multiplies every fire rate of dungeon enemies |
| `maxBulletsPerSecond` | `10` | Most bullets per second one dungeon unit may put out; its active guns slow down together until it fits (`0` = no limit) |

Why the bullet limit: the original units picked ONE gun pattern at a time with a state machine that is in no client build
(only the 190426 build had part of it; see `docs/dungeon-server.md`). The server fires every gun of the template together, so the Slicer (`mazedrawer`, four
guns) put out ~70 bullets per second and SN-Z ~50. `Enemies.Ai.cs` `DungeonFireScale` does the math per unit, per shot.

## Mission Transport

The mega carrier at the end of the main ramp (`nexus.json`, prefab 30) opens the client's mission list. `Dungeons\MissionTransport.cs`
answers `ServerListMissions` (12000) with one mission per open dungeon (mission id of the game's own `missions` config, taken from
the `configBinary` by terrain: `dungeon_bomb`, `DQ_KEB_sneezy`...) and `ServerMissionJoin` (12001) with `SWITCH_SERVER`, then sends
the player to the dungeon server like a carrier does. Mission and squad ids are stable hashes of the terrain name.
The game itself hides missions of a much higher tier than the plane while there are missions of the plane's tier.

## Dungeon maps: 4x smaller

`tools\scale_dungeon_maps.py` multiplied every u/v of the 17 maps by 0.5 and resampled the drawing (2x2 sub-cells to 1:
`#` if 3 or more are walls, `.` if there is free floor). Then the drawing was moved so it starts on a tile border. The files carry
`"scaled": 0.5` and the script refuses to scale twice. Checked: every point, checkpoint and unit is on free floor and everything is
reachable from the spawn. Enemy flight distances (`preferredDistance`, `aggroRadius`) were not scaled: they are real distances.
To scale a community map the same way: `python tools\scale_dungeon_maps.py Server\maps\my_map.json`.

## Map and unit fields added

- Map `collisions`: `[{name, minU, maxU, minV, maxV}]`, solid scenery. Every wall sub-cell (3 u) whose center is inside the rectangle
  is closed in the floor message (`Maps.AddCollision`, `WriteFloor`). Works on tiles without a drawn floor (the Nexus has none).
- Unit type `chainReaction` `{radius, delaySeconds}`: when one unit with it dies, every other unit with it within `radius` goes off
  `delaySeconds` later (Bomb Factory barrels and loose bombs).
- Existing fields now used by the maps: `aimLead` (lead aim: `1` = full), `shotRange`, `guns`, `fireRateMultiplier`, `invulnerable`.
- Units whose art is `!EnemyInvisible` and that have no armed gun (controllers of the original server, like `trapbutton`) are not
  created in dungeons (`Enemies.IsControllerOnly`).
- Units of a wave and summoned units hunt the player from the start (`Aggro` is set at creation); before, they wandered until the
  player came within `aggroRadius`.
- When 1 to 3 units are left in a room and nothing changes, the chat says which ones, every 30 s.

## Known limits

- Electric Belt and Bullet Belt: 1 hp, hit radius 0, tiny art and bullets that live ~3 u (client data). They cannot be shot or made
  bigger from the server. Electric Belt is invulnerable and does not count to clear a room.
- Slicer and Bouncer without a name in the game: the text exists in the client table (`Texts/en-US/units`); no cause was found in the server.
- Nexus collision walls use the wall art of territory 37 (`wallTag nexus`). If it has none, the game logs "No walls found" and the
  colliders still exist.
