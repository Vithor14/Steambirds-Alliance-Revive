# Steambirds Alliance Revive 0.4.0

Launcher 0.4.0 and server update. Client build: **201219.1413** (Steam depot `386010 386011 58813776496064724`).

## Before you update

- **Old data is not read anymore.** Files, folders, map fields and settings were renamed to English, and the
  compatibility layer for the Portuguese names was removed. Accounts, maps and `.json` settings from 0.3.x do not open
  in 0.4.0. Start from the new package; do not copy old files over it.
- Run `SteambirdsGameFiles.exe` again: the plugin changed and the data files it writes must be regenerated.

## New

- **Stores.** The four stores (General, Hangar, Storage, Pilot School) sell from the game's own 294 products. Gold and
  iron are charged from the account, and items, unlocks, hangar slots, loadouts and storage space are delivered. Purchases
  are saved per account. Hosts set prices, discounts, stock and per-account limits in `stores.json` or in the new
  **Server → Store** page of the launcher.
- **Enemy AI editor.** New launcher tab to build enemy AI as data files (`server\ai\brains` and `behaviors`), with a
  simulator that runs the same code as the server. Six AIs and ten behaviors come as examples; any unit type can use one
  with `"ai": "<id>"`. The server reloads the files when they change.
- **Mission Transport.** The giant carrier at the end of the Nexus ramp lists the open dungeons as missions.
  `entryMode` in `dungeons.json` chooses portals, missions or both.
- **Nexus collision.** The city backdrop, hills, towers and props now block planes. The buildings stay open so you can dock.
- **Hangar: retire plane.** The *Retire plane* button works: it pays the pilot XP not yet counted and the currency, moves
  the equipped items to storage and ends the plane's buffs.
- **Starting money for new accounts** and a per-account money editor in the launcher's Accounts page.

## Changes

- **Dungeons are 4× smaller** (half the width and height); rooms, gates, checkpoints and signs moved with them.
- **Dungeon enemy fire is limited**: `maxBulletsPerSecond` (default 10) and `fireRateScale` in `dungeons.json`, also in
  the *Maps and dungeons* page. This fixes very fast shots in the Iron Lair, Slicer and Zombie.
- Boss rooms no longer lock the player in (`lockBossRooms`, default off).
- **Bomb Factory**: barrels and bombs now explode in a chain reaction.
- **The Arena**: wave and summoned enemies chase the player from the moment they appear.
- **Home for Special Children**: the Electric Belt is now an invulnerable hazard and no longer blocks clearing the room.
- **Laser Playground**: the invisible `trapbutton` controller is no longer created.
- When a room will not open its gate, the chat tells you which enemies are still holding it.
- Fixed item dropping, retiring a plane and plane buffs.

## Known issues

- Missing names for Slicer and Bouncer in some dungeons, and a few unnamed small discs in the Laser Playground.
- Stores, Mission Transport, Nexus collision, the Enemy AI in game and the dungeon changes were built and checked
  without a full in-game test. Tell us what breaks: open an issue with `server-report.txt` from the launcher's logs.
- Not yet: open world, quests and missions, flocks, trading, Combat Sim and most dungeon-specific scripts.
  See `CHECKLIST.md`.

## Install

1. Download the zip below and extract it into a new folder.
2. Run `SteambirdsLauncher.exe`, then use the **Client** tab for the game (Steam download) and **PLAY**, or the **Server**
   tab to host.
