# Open world (terrain `Basic`, ServerRole.World)

Status (22/11/2026): tested in the game (the Nexus portal, the floor, the zones, enemies, carriers and the launcher map worked). Also tested with a test client that speaks the game's messages (joins port 1114, receives and
parses the whole floor, region names, enemies and carriers; the Nexus portal switches the plane to the world).
Code in `Server\World\`, settings in `Server\world.json`, port `worldPort` (1114) in `network.json`.

## What the client brings and what was rebuilt

The client has the data structures (`WorldLayout`, `GroundAssetContainer`, `TerritoryTileSerializer`) and the tables in the
`configBinary`, but the program that filled the map ran on the original server and is in no client build (`island` and
`named_spawn_rules` are empty files, like the Nexus map).

| From the client's data (exact) | Rebuilt (reconstruction) |
|---|---|
| `territories` -> `Basic`: `originElevation` 100, `distancePerElevation` 8.575, disk radius 24.5 to 61.25, `maxAllowedOverlapOnSubterritory` 0.9, `islandRequiredPreTerritories` beach, the regions and their elevation bands, `tier`, `secondsPerStandardEnemyIn10x10`, wall `strip` rules | Where the land is: disks placed at random from the seed (`landFraction` of `world.json`), each disk a variant of the region at its center |
| `territoryTiles`: IDs, `continuityGroup`, `closeCanyonGroup`, `groundTag`, `cliffTag`, `fenceTag`, `scatterTag`, scatter probability, scale and rotation (with `basedOn` inheritance) | Wall strips: how the `strip` numbers (distance, length, straightness) are laid out on the 3-unit wall grid |
| `groundAssets` + `ground_assets.txt`: IDs, weights, flips of every floor piece | Which enemies live in which region and where (`WorldEnemyPools`, spawn sites) |
| Canyon floor between regions (`WorldLayout.MakeWideCanyons`) and the choice of the floor pieces (`PlaceGroundAssetsForCell`, `FourPieceTileUtils`): ported from the client code | Dungeon carriers: which one, where, for how long |

## Flow

1. **Way in:** the Nexus sends a Portal (prefab 11, flags `2`, dungeon type `Basic`) at `world.json` > `nexusPortal` (default `u 72, v 9`, just past the Mission Transport carrier); flying into
   it sends `ServerSendMeThroughPortal`, answered with `ClientPortalResult` and `ClientSwitchToServerWithPlane` to port 1114.
   `/world` does the same from anywhere.
2. **Welcome:** role `1` (World), terrain and scenario `Basic`. The map is built here when the world is empty (about 100 ms for
   a seed, `WorldGenerator`), and kept while at least one player is in it. The last player to leave discards it.
3. **Floor:** `ClientReceiveTerrainTiles` in chunks of 200 tiles, the nearest to the spawn point first (about 9000 tiles,
   300 KB). The client adds every chunk to its grid.
4. **Spawn:** `world.json` > `spawn` (the center of the starting beach), a few units apart for each player.
5. **While playing** (`WorldServer.Tick`, every 0.5 s):
   - region name: `ReceiveCurrentSubterritoryName` (`62683`) with the base name (`beach`, `lowlands`, `midlands`, `highlands`,
     `mountains`, `highmountains`, the keys of `Texts/<lang>/territories`) when it changes;
   - enemies: spawn sites near the players (see below);
   - carriers: every `carriers.everySeconds` one open dungeon of `dungeons.json` gets a carrier on free land, for
     `lifetimeSeconds` (the client shows the countdown); the chat says the direction. `ServerSendMeThroughPortal` on a carrier sends
     the player to that dungeon, which returns to the Nexus as always.
6. **Way out:** the menu's "Return to Rebel City" (`ServerSendMeToNexus`) or `/leave`.

## The map (`WorldGenerator`)

- `sizeScale` of `world.json` (default 2) sets the radius: 650 x scale, so 1300 units and 217 x 217 cells of 12 units. The client sizes its tile grid from the radius of the welcome message (`WorldServer.RadiusFor`), and every region band grows with the scale (elevation uses distance / scale), so the layout is the same, only bigger, with more islands. The grid is the client's (rotated 45 degrees, `Ground.cs`); only the cells whose center is inside the
  circle exist; the last 36 units are water.
- Elevation falls with the distance from the center: `100 - distance / 8.575`. Bands: beach 92..100 (about 0..69 units), lowlands
  72..92, midlands 51..72, highlands 31..51, mountains 18..31, highmountains 0..18 (past the land limit, so rare or none).
  Each band has variants (`lowlands`, `lowlands2`, `lowlands3`): the variant of a disk is random.
- Land = disks of radius 24.5 to 61.25 until `landFraction` of the circle is land; a disk may overlap existing land up to 90%.
  The start is one big beach disk and a ring of six more.
- Canyon floor (`canyonFloor`) takes the cell between two regions of different `closeCanyonGroup` (the cell of the higher tile ID),
  so every island except the beach has a one-cell canyon ring. Cliff pieces appear where the `continuityGroup` changes.
- Walls: strips of 3-unit sub-cells inside each region that has `wallPlacement: strip`; none within 36 units of the spawn point.
- The floor pieces are chosen with a seeded generator per cell, so the same seed gives the same world.

## Enemies (`WorldEnemyPools`, `WorldServer.Sites.cs`)

- The game's units are filed by region: `Units/01 - Beach`, `11/12/13` lowlands, `21/22/23` midlands, `31/32/33` highlands, `41`
  mountains, `51` highmountains. The first digit is the band and the second the variant (`lowlands2` -> `12`). `GRP<n>_x` +
  `GRP<n>_xleader` are a group, `SLO<n>_x` a solo enemy, `BOS` bosses and their parts (a boss is created only when the host asks, see
  "Lists, bosses and requests" below). Helpers of other units
  (controllers, grenades, clones, exploders...) are left out by name.
- A region has "spawn sites" on a jittered grid (`siteSpacingTiles`). A player within `spawnRadius` makes the site create one
  group (a leader and followers, or one solo) of the region's pool, asleep: it wakes when a player is within `wakeRadius`, or on a hit.
  Farther than `despawnRadius` from every player the group is taken away. After the group dies the site rests
  `secondsPerStandardEnemyIn10x10 x enemies x respawnScale`.
- The type is `world/<template>` (the game's unit, `chase` behavior, free to roam, no area limit). **No new AI was written:** put a type
  with the same key in `enemies.json`, or an AI file in the Enemy AI tab, to change it.
- The server limit is 140 enemies per instance; `maxAlive` (100) stays under it.

### What each client is told about (`Enemies.Interest.cs`, `viewRadius`)

The client draws an arrow at the edge of the screen for **every** enemy it knows (`Radar.GetRadarEntities` has a distance cutoff
only for other players, `radarPlayerCutoff`), so telling everybody about every enemy within `despawnRadius` (260) filled the screen
with arrows. The original server did not do that: the client's own PVS code (`PVSCollection`, `EntityDescriptor.visibleRadius`,
`GlobalConfig.networkCutoff`) sends a unit only to the viewpoints within its radius. The open world does the same with
`enemies.viewRadius` (default 90, just above `wakeRadius`):

- Each tick, for every player with a plane, an enemy within `viewRadius` of it is sent with the normal spawn message and is
  remembered in `Instance.Known[peer]`. One that is beyond `viewRadius` + 15% (at least +8 units, so it does not flicker at the edge) is
  taken away with a kill message of the "vanished" kind (`Fizzled`, no explosion), which also removes its arrow.
- Updates, shots and kills of an enemy go only to the players who know it. The turrets and mines of a player stay known to the owner
  at any distance (the engineer's teleport to a turret needs them).
- `viewRadius` 0 turns it off (everybody gets everything, as before). It is read every tick, so editing `world.json` applies at once.
  Dungeons and the tutorial do not ask for it and keep sending everything. It is a reconstruction: the value of `networkCutoff` is in the
  game's config but not read here.

## Tests

- `world.json` is created on the first start; `/worldinfo` and `/carrier` help to see what the world is doing.
- To check the generator without the game: build `Server`, then a small program that references `SteambirdsServer.csproj`, sets the
  current directory to `Server` and calls `ConfigBinary.Load`, `WorldData.Load` and `WorldGenerator.Build`.

## Edge of the world (invisible wall)

Every wall tag of the client has art, so there is no invisible wall in the client's own data. The server keeps the border instead
(`WorldServer.Edge.cs`): ten times per second a plane closer than `edgeMargin` (24) units to the border is put back 14 units inside
with no velocity (`ClientTeleportToCoordinatesAfterTime`), and the chat says so every 8 s. `edgeWall` turns it off. It is a soft wall: the
plane crosses a few units before the server notices (a client with a very high ping crosses more).

## Nexus carriers (22/11)

`dungeons.json` > `nexusPortals` now starts at `u -18, v -21` with a `step` of 15 (half of the old values). The portal of the open world is
at `u 72, v 9` (`world.json` > `nexusPortal`).

## Original zones layout (22/11, default)

`world.json` > `layout` is `"original"` by default: the world is the twelve zones of the game's own world map (the picture the
Sergeant shows in the tutorial conversation) instead of random islands (`"random"` keeps the old disks, with `biomeScale` and
`landFraction`). `WorldZones.cs` lays one or more weighted Voronoi sites per zone, with the border shaken by a smooth noise:

| Zone (game text) | Tile | Folder of its enemies | Place |
|---|---|---|---|
| City Ruins (Ruined City) | `beach` | `01` | center |
| Mecha Hunting Grounds | `lowlands` | `11` | north of the center |
| Local Guard Post | `lowlands2` | `12` | east of the center |
| Poison Farms | `lowlands3` | `13` | south-west of the center |
| Dog Base (and Dog Base South) | `midlands` | `21` | north and south |
| Graveyard | `midlands2` | `22` | west band |
| Fishing Zone | `midlands3` | `23` | east |
| Robot Factory | `highlands` | `31` | south-east corner |
| Hospital Area | `highlands2` | `32` | south-west corner |
| Egyptian Dig (Archeological Dig Site) | `highlands3` | `33` | north-west corner |
| Noble Estates | `mountains` | `41` | north-east |
| Imperial Palace | `highmountains` | `51` | far north-east |

The names come from `Texts/lore` (`territory_tile_<tile>_name`) and the ingredient texts ("Found on Island in ... area"); the client
itself shows "Now entering: <zone>" when the plane flies over a tile. The positions were read from the picture of the map; Hospital
Area and Imperial Palace are not labeled in it, so they sit in the unlabeled corners. `sizeScale` is now 1.3333 (radius about 867).

## World map page of the launcher (22/11)

Server tab > World map (`Launcher\UI\WorldMapPage.cs`, `WorldMapView.cs`). The server writes two files in its folder
(`Server\World\WorldLive.cs`): `world-map.json` when the world is built (the zone of every cell) and `world-live.json` every second
while somebody is in it (players, carriers, counts; `"active": false` when empty, also at server start). The page draws the world with
one color per zone, the spawn point, the carriers and the players (a dot with its heading and name). Hovering a player shows plane,
level, pilot rank, zone, position, distance from the center, health, shots, hits and time in the world; hovering a carrier shows its
countdown; hovering the map shows the coordinates and the zone. Clicking a player selects it (and the list at the right). The lists
at the right show the players and the carriers. It reads the server folder of the launcher (`server\`), so it follows the server the
launcher starts.

## Built in advance, settings and reroll (22/11)

- **Standby world** (`WorldServer.Standby.cs`, `pregenerate` in `world.json`, on by default): the server builds the world when it starts,
  before anybody joins, and writes `world-map.json` and a `world-live.json` with `"standby": true`. The first player takes that world over
  (the log says "the world built in advance goes to its first player"). When the last player leaves, the world is discarded and the next
  standby is built at once (a new random seed when `seed` is 0). If the map settings of `world.json` change (seed, size, layout,
  biome scale, land fraction, spawn point, site spacing, skipped units) while nobody is in the world, the standby is built again.
  With players inside nothing changes until the world is empty.
- **Launcher, Server > World map**: two tabs, Map and World settings. The settings tab edits the scalar keys of `world.json` (on/off, name,
  maximum players, how it is generated, size, seed, build in advance, island size and land fraction for the random layout, edge wall).
  The nested keys (enemies, carriers, spawn point, Nexus portal) stay in the file (Settings tab, Files). **Reroll world** writes a new random
  seed in `world.json`; the server rebuilds the standby world within a second and the Map tab shows it. With players inside it asks first.

## Lists, bosses and requests (24/11)

Launcher: Server > World map > **Enemies** tab. Server: `World\WorldEnemyLists.cs`, `WorldEnemyPools.cs`, `WorldServer.Requests.cs`.

- **Numbers.** The tab edits `world.json` > `enemies` in place (`JsoncFile.ApplyChangesIn`: the comments stay, a key the object lacks is added):
  `spawnRadius`, `despawnRadius`, `wakeRadius`, `viewRadius`, `maxAlive`, `siteSpacingTiles`, `groupSizeMax`, `respawnScale`,
  `healthMultiplier` and `enabled`. The server rereads them every second (`siteSpacingTiles` only matters when the world is built).
  `tierOffset` is in the file but nothing reads it yet, so the tab does not show it.
- **Lists per biome.** `world-enemies.json` (written by the launcher, gitignored, reread when it changes) holds what the host changed:
  `regions.<tile name>` (`beach`, `lowlands`, `lowlands2`...) with `groups` and `boss`. A group is `{ "name", "solo", "leaders", "members" }`:
  a leader and its followers, or `solo: true` (a spawn site creates one enemy of `members`). A region with `groups` in the file uses
  them instead of the game's groups; a region that is not in the file keeps the game's (`WorldEnemyPools.For` / `DefaultsFor`). Units
  that are not in the catalog, walls, player planes and the `enemies.skip` of `world.json` are ignored. A site picks the group when it
  spawns, so a change reaches new spawns only; groups already on the map stay until they die. Regions with no enemies keep their sites
  (the host may fill them later) and look again every 20 s.
- **Game's lists.** The server writes `world-enemy-defaults.json` (gitignored) every time it builds the pools: per region the zone name,
  tier, the game's groups and the boss choices. The launcher reads it to show what is default and to go back to it.
- **Bosses.** The `BOS<n>` units of a folder (`Units/<code> - .../BOS<n>_<unit>`) with the same number are one boss with its parts and
  minions (`WorldEnemyPools.BossGroup`): Gargantua with its fists and laser, Clockboss with its hands and twelve numbers, the train with
  its cars, the Hypnotist with its minion and pet... The boss is the strongest unit of the group that is not a part (by name: hands, fists,
  numbers, shell, minions...); the rest come with it, except the death model, what it fires or drops (mines, missiles, chests, grenades),
  clones and the controllers/masters of the original scripts, and the invisible units (the claws of the Berserker are attack boxes).
  A region offers the bosses of its folder, the strongest first, except Egyptian Dig (`pharaoh`), Noble Estates (`hypnotist`) and Imperial
  Palace (`notra`) first, which `PreferredBoss` sets; the first is the biome's boss. The host picks another (any unit of the game, which
  then comes alone if it is not a boss of the game) per region in the tab (`regions.<tile>.boss`). Reconstruction: no original boss AI: the
  boss is the unit with the `chase` behavior (type `world/boss/<unit>`) and each part or minion another one with `chase` too
  (`world/part/<unit>`), all replaceable in `enemies.json` like the others. The server writes the groups with their parts in
  `world-enemy-defaults.json` (`bosses`).
- **Requests from the launcher.** Right click on the map (a cross marks the point): *Spawn boss here* (submenu with the bosses of the biome
  under the mouse, each with how many parts come along, the biome's boss marked) and *Spawn dungeon carrier here* (submenu with the active
  dungeons of `dungeons.json`); the *Spawn dungeon* button creates a carrier at a random place. The
  launcher writes `world-requests\<id>.json` (`{ id, action: boss|carrier, x, y, dungeon, boss }`); the world server reads the folder twice per
  second, does it, deletes the file and writes the answer to `world-request-result.json` (`{ id, ok, message }`), which the launcher shows. The
  boss appears awake at the point (or at the closest free land within 36 units) and its parts and minions on a ring of 9 to 15 units around
  it, all in one group `boss:<id>` (up to 30 parts); the carrier stays `carriers.lifetimeSeconds` next to
  the point (ignores `maxAtOnce`; a closed dungeon is refused). It needs a player in the open world: the server creates enemies and
  carriers on a player's connection, so with nobody inside (or only the world built in advance) the answer says so.
- Tested: building, the server writing the lists and bosses of the 12 regions, the refusal with nobody in the world, and the edit of
  `world.json`. Not tested in the game: the boss and the carrier appearing, the tab with real clicks.
