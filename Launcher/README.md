# Steambirds Alliance Revive — Launcher

Launcher of the project (Portuguese version: `README.md`), with four tabs:

- **Client**: downloads the game with Steam, sets the player name, lists the servers (this PC, the local network and VPNs like Radmin and Hamachi, or a typed IP) and opens the game on the chosen server. Its console shows `BepInEx\LogOutput.log` and the Unity `Player.log` while the game runs.
- **Server**: hosts the server of this PC (what `Run server.ps1` did) for this PC and for players of other PCs, shows its output in a console and edits its settings, its network (name, ports, who can join) and the accounts of the players.
- **Settings**: theme, launcher language, game folder and language, server search port, and buttons to open folders and files.
- **Map editor**: creates, opens, edits and saves the map files of the server (dungeons, Nexus, tutorial) with the pieces of the game. See `Editor\README.md`.

The launcher opens in **English**. Other languages: *Settings → Appearance → Launcher language* (`lang\README.md` explains how to add one).

Features the project does not have yet are **grayed out**, with a *(coming soon)* tag that explains why.

## Building

In the project folder (`C:\Steambirds`), run **`Build launcher.ps1`** (right click → *Run with PowerShell*). It:

1. looks for duplicated `.cs` files (copies like `Name-1.cs`, or the same file in two folders);
2. builds the server (`Server`), the plugin (`QuieterPlugin`) and the launcher as *self-contained* executables (nobody needs .NET installed);
3. assembles **`Launcher\dist`**, with the languages of `Launcher\lang` in `dist\lang` and the pictures of the map editor (`Launcher\editor-assets`, made by `tools\editor\build_editor_assets.py` from the extracted game assets, not in git) in `dist\editor-assets`.

Then open `Launcher\dist\SteambirdsLauncher.exe`. Run the script again whenever the code of the server, the plugin or the launcher changes. The build log is `Launcher\build\build.log`.

## What the launcher keeps in its folder

| Path | What it is |
|---|---|
| `launcher.json` | launcher settings (game folder, player name, chosen server, typed IPs, search port, theme, languages, window) |
| `lang\` | launcher languages (`pt-BR.json`, `_template.json`, `README.md`) |
| `editor-assets\` | pictures and name lists the map editor draws (optional: without them it draws shapes) |
| `logs\` | server logs per session, client console logs, `server-report.txt` (report) and launcher errors |
| `server\` | the server and its data: `maps`, `data`, `loot_tables`, `catalog` and the settings (`progression.json`, `items.json`, `dungeons.json`, `enemies.json`, `network.json`) |
| `server\accounts\<key>\` | one account per player (`steam_<SteamID>`): `account.json`, `inventory.json`, `acceptances.json` |
| `server\_backup\` | copies made before files are replaced or accounts are saved, removed or moved |
| `plugin\QuieterPlugin.dll` | the plugin, which *Install plugin* copies to the game's `BepInEx\plugins` |

The old single account (`account.json` in the server folder, from before the accounts per player) goes to the **first player who joins from the server's own PC**, with a copy in `server\_backup\migration-<date>`.

Before sharing the `dist` folder, delete the accounts and logs (`server\accounts`, `server\account.json`, `server\acceptances.json`, `server\inventory.json`, `server\client-uploads`, `server\_backup`, `logs`, `launcher.json`, `lang\*.missing.json`).

## Client tab

- **Download the game with Steam**: copies `download_depot 386010 386011 58813776496064724`, opens the Steam console (`steam://open/console`) and follows the download (folder size and the `Depot download complete` / `failed` lines of Steam's `logs\console_log.txt`). When it is done, `steamapps\content\app_386010\depot_386011` becomes the game folder and gets `steam_appid.txt` (386010), so the game logs in with Steam outside the library. The Steam account needs the game in its library. The depot has no BepInEx: without it the game runs without the Quieter plugin.
- **Player**: the name the other players see. Empty = the name of the Steam account (read from Steam's `loginusers.vdf`). The **account** on a server is always the Steam account (the Steam ID comes from the login ticket), so changing the name keeps the pilot and the planes. The name goes in the path of `OZY_URL` (`http://<ip>:<port>/n/<name as hex>/ozy`), only for servers that accept it.
- **Servers**: the first row is **this PC**; below it, the servers found on the local network and on VPNs, and the typed IPs.
  - The search (*Search again*, or every minute with *Search by itself*) sends a UDP packet to port 6444 on every network of the PC: broadcast, the PCs of the ARP table and, on small (home) networks, every address. Radmin VPN and Hamachi are reached by broadcast and the ARP table.
  - *Connect by IP*: type the IP of the server PC (plus `:port` when the login is not 6443). The launcher checks `http://<ip>:<port>/revive/info` and adds the server to the list (typed IPs are saved).
  - Clicking a row chooses the server (bold row); a double click opens the game. *Remove from list* removes a typed IP.
- **PLAY**: opens the game on the chosen server. **This PC**: `OZY_URL=http://localhost:<port>/ozy` and `-connecttolocal`; with *Start the server of this PC with the game* checked, the server starts first. **Another PC**: `OZY_URL=http://<ip>:<port>/ozy` without `-connecttolocal` — the login answers with the Nexus of that server. While another PC is chosen, *Start the server of this PC with the game* is unchecked and grayed out.

## Map editor tab

Draws the map of the server (`server\maps\*.json`) with the floor pieces and units of the game. New map from a dungeon of the game or an empty dungeon, open, save (the file name is the map id), undo and redo, and a check before saving that asks for the required objects (floor, entrance, exit point, something that ends the dungeon). A map with errors can be saved only as a draft (`"rascunho": true`). Every kind of data the server reads from a dungeon map has a place in it: floor and walls, entrance, exit, checkpoints, gates, rooms with enemies and waves, unit types with phases and summons, points, areas, lines, routes, signs, decoration and buildings. The *Enemy AI* editor is grayed out for now; the slot (`"ia"` in each unit type) is already kept. Details, keys and code map: `Editor\README.md`.

## Server tab

- **Start / Stop / Restart**, the state of the server, its name, ports and whether it accepts other PCs.
- **Console**, **Progression**, **Items and loot**, **Enemies and difficulty**, **Maps and dungeons**: as before (screens built from the server's own `.json` files).
- **Accounts**: the starting state of new accounts, and the **list of accounts** (one per player: name, Steam ID, last played, rank, planes, kills...), with the planes of the selected one. *Edit account...* and *Remove account* only work while the launcher's server is stopped.
- **Network and multiplayer**: the **addresses to share** with other players (one per network: local network, Radmin VPN, Hamachi...), the **Windows Firewall** rule (*Allow in the Windows Firewall* creates an inbound rule for `server\SteambirdsServer.exe` on every network; Windows asks for administrator permission) and the settings of `network.json`: server name, players of other PCs, server search, maximum players, who can use chat commands (`everyone`, `host`, `none`), welcome message and the **ports** (TCP login, UDP Nexus, tutorial, dungeon and search). Ports and "players of other PCs" change when the server restarts; the server reads the rest again by itself.

## Playing with other people

1. **The host** opens *Server → Network and multiplayer*, checks that other PCs are accepted, clicks *Allow in the Windows Firewall* (once) and starts the server. The addresses to share are on the same page.
2. **The other players** open the *Client* tab: the server shows up in the list (same network or VPN) or is added with *Connect by IP*. Choose the row and click **PLAY**.
3. On the internet (no VPN), the host's router must forward the login port (TCP 6443) and the game ports (UDP 1111, 1112 and 1113) to the host's PC, and the players type the host's public IP.

Every player has their own account on the host's PC (pilot, planes, items, finished dungeons). For now **each player plays in their own world**: enemies, chests and loot are per player, and the players do not see each other yet (relaying the planes, shots and skills of the others is the next step of the multiplayer). The chat says who joins and leaves, and `/players` lists who is online.

## Code

`Launcher\` (WinForms, .NET 8, no designer files):

- `Core\`: everything that is not a screen — server and game processes, logs, report, `.json` with comments, `dungeons.json`, `network.json` (`ServerNetworkFile`), accounts (`AccountStore`, `AccountFile`), server search (`ServerBrowser`, `LocalNetworks`), the Steam account (`SteamAccount`), the depot download (`DepotDownload`), the firewall rule (`FirewallRule`), languages (`Lang`).
- `Core\GameData\`: reading the game's configBinary (classes, unlocks, currencies, ingredients, XP tables, languages).
- `Editor\`: the map editor tab (the document, the canvas, the forms and the checks; see `Editor\README.md`).
- `UI\`: the screens — `MainForm`, `ClientTab`, `DepotDownloadForm`, `ServerTab`, `NetworkPage`, `SettingsTab`, `AccountsPage`, `AccountEditorForm`, `MapsPage`, `SettingsPage`, `ConsoleView`, `Theme` and `LauncherContext` (state shared by the tabs, including the server list).

Every text shown on screen goes through `L.T("...")` / `L.F("... {0}", value)`, and its translation goes into `Launcher\lang\pt-BR.json` and `_template.json`.
