# Steambirds Alliance Revive 0.7.0

Client build **201219.1413** and plugin **1.22.0** are unchanged. Settings and maps from 0.6.0 keep working, except the old boss `phases` (see *Enemy AI*).

## Enemy AI

- **Every unit runs an AI.** The plain `behavior` of a unit type is gone: each type names an AI file. Eleven **generic AIs** (`generic_chase`, `_circle`, `_charge`, `_turret`, `_patrol`, `_escort`, `_idle`, `_fixed`, `_kamikaze`, `_mine`, `_route`) replace the old built-in flights, so no unit lost its movement. Old maps without an `ai` still work.
- **AI files live in folders** (`generic`, `world`, `dungeons\<dungeon>`) and can say which units they were made for.
- **Summons, shields and effects** inside a state: summon units (with a cap), put up a shield, change the unit's look, switch homing bullets. New condition `summonsAtMost`.
- **Boss phases are now AI states** reached by `healthBelow` transitions. The 24 shipped bosses were converted. A community map that uses a boss AI must ship its brain file (the launcher's *Export...* packs it).
- **Editor:** new **Graph** view (drag states, see what it takes to get from one state to another), a separate **Info** tab, expandable weapons, a unit picker grouped by origin, embedded behaviors and *Duplicate / Delete* on the AI list.
- **Simulator:** shows the conditions of the state's exits and the weapon ranges on the map. Editing an AI no longer restarts the simulation.

## Open world

- New **Enemies** tab (*Server > World map*): spawn numbers, the enemy groups of each of the 12 biomes, and the AI of each enemy. Saved in `world-enemies.json`.
- **Spawn a boss or a dungeon carrier** from the world map (right click, or the *Spawn dungeon* button). Needs a player in the open world. Bosses use the default AI for now; the original boss AI is still missing.

## Server tab

- **Console commands:** run any chat command as one player, all players or the game of this PC, with a list of the ~70 commands.
- **Choose the Nexus and tutorial maps** (`nexusMap`, `tutorialMap` in `dungeons.json`), with a **map preview** and an *Open in editor* button for each map.
- The first-run window now runs `SteambirdsGameFiles.exe` by itself after the Steam download.

## Map editor

- Conditions are drop-downs (gates, rooms, groups). Checkpoints can trigger by column (`activeAtU`).
- Tabs and tools show what is missing instead of failing; the palette layout, decoration selection and unit-type names were fixed.
- Painting floor no longer freezes the editor.

## Fixes

- **Iron Lair mines** no longer explode when dropped or when shot; they arm once the players leave their ring.
- **Safe trade** requests are no longer declined by themselves 1.3 s after they are sent.
- Launcher list rows no longer lose their text on hover. Open world enemies show their unit picture.

Full details (English and Portuguese): `docs/update-notes-0.7.0.md`.
