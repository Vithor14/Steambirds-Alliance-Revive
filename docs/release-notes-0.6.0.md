# Steambirds Alliance Revive 0.6.0

Client build **201219.1413** and plugin **1.22.0** are unchanged. Settings and maps from 0.5.0 keep working.
Features marked *not played* were built and checked against the client's code, but not yet tested with several players. Please report anything odd.

## New

- **Safe trade** *(not played)*: player menu > Trade or `/trade NAME`. Up to 4 cargo items and some gold per side, with a fee that is recalculated on every change. Both inventories are checked again before the swap. `social.json`: `tradeEnabled`, `tradeTaxes`.
- **Map scripts**: a map can have a `script` section (spawns, rules, conditions, actions, `/step` jumps). The walker tutorial now runs from `maps\tutorial_walker.json`, and dungeons can run scripts too. New action `sound`. See `docs/map-script.md`.
- **Map Maker catalog maps** *(not played)*: three small maps with every floor piece, decoration and enemy unit, each labeled on the floor. Open one in the map editor and click **Play test**.
- **Accounts with a password (optional)**: set `passwordLogin: true` in `network.json`. New Steam players register in the game; accounts that already exist keep logging in through Steam. Hosts reset passwords with `/passwd`.
- **Iron for pilot XP**: destroyed planes (like retired ones) now pay currency at the game's rate, with the in-game notification.
- **Admin and debug tools** *(not played)*: `/debug`, `/god`, `/watchai`, `/sound`, `/soundall`, `/screenshot`, `/stats`, `/serverstats`, `/audit`, `/admin`. The server also answers the client's own debug messages and `ServerQuery`. Details in `docs/admin-tools.md`.
- **Server statistics and inventory audit**: loop time per server (also in the game's server time chart) and a check of the audit ids the client reports.

## Dungeons

- New map value `values.maxEnemies` raises the 140-enemy limit.
- **Iron Lair** rebuilt as a donut arena. It was too deadly in the 23/11 test and is still being tuned. SN-Z shots no longer home, and its bombs are now **mines** (new unit behavior `mine`).
- Bomb Factory, Crypt of Cathulhu, Laser Playground and The Arena were completed by 3 players; the Command Center was played up to the boss room.

## Launcher

- **Play test** (`F5`) in the map editor: starts a tiny private server with only your map and opens the game straight into it. *(Not played.)*
- **Discord Rich Presence** (plane, level, rank, place; server name and pilots online while hosting). Never the IP, port or account. Off in Settings > General.
- **Chat monitor** page in the Server tab, separated by map, with filter and copy.
- PLAY asks for confirmation when the plugin or BepInEx is missing.
- `WARNING: ... did not arrive` lines show in the warning color.

## Fixes (23/11 test)

- Server flood of "data of other players" answers (28,000 per test) is now one per second per player.
- The friend list reaches the client on each server entry, so friends show on the radar in the open world.
- The server closes a stale connection 6 s after a server switch, and warns which UDP port to open when a player does not arrive within 20 s.
- Quest list requests are answered instead of ignored.
- **Boss damage share** is now real, and a boss only drops loot for players above the unit's `damageThreshold`.
- Log fixes for the finished-dungeon line and the death position.

## Docs

New `docs/radmin-vpn-guide.md`, `docs/admin-tools.md` and updated `CHECKLIST.md` (104 done, 86 partial, 36 missing).
