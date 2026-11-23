# Steambirds Alliance Revive — Launcher

Launcher of the project (version 0.5.0). The header has the five main tabs, grouped by what they are for — **play** (Client, Server), **make** (Map editor, Enemy AI) and, at the right end, **Settings** — and the state of the server and of the game:

- **Client**: three sub-tabs and a **Play bar** that stays in view.
  - *Servers*: lists the servers (this PC, the local network and VPNs like Radmin and Hamachi, or a typed IP).
  - *Game*: the game folder, the Steam state, the Quieter plugin and the player name. *Download the game with Steam* only shows while the game is missing (or is not the build the server was made for).
  - *Console*: `BepInEx\LogOutput.log` and the Unity `Player.log` while the game runs.
- **Server**: hosts the server of this PC for this PC and for players of other PCs. The header has Start / Stop / Restart and the state; a side navigation groups the pages: *Monitor* (Console), *Game rules* (Progression, Items and loot, Enemies and difficulty, Store), *Players* (Accounts, New accounts, Friends and flocks), *World* (Maps and dungeons, World map with its World settings) and *Connection* (Network and multiplayer).
- **Map editor**: creates, opens, edits and saves the map files of the server (dungeons, Nexus, tutorial) with the pieces of the game. See `Editor\README.md`.
- **Enemy AI**: builds the AI of the enemies (states, movement, when to shoot) and its reusable behaviors, each saved as its own `.json` file in `server\ai`, and simulates them with the art and guns of any game unit. See below.
- **Settings**: four sub-tabs — *General* (theme, launcher language), *Game* (game folder and language), *Server* (report, server search port) and *Folders and files* (buttons to open folders and files).

The launcher opens in **English**. Other languages: *Settings → General → Launcher language* (`lang\README.md` explains how to add one).

Features the project does not have yet are **grayed out**, with a *(coming soon)* tag that explains why.

## Look and feel

The look uses the colors of the project branding (`Branding\README.md`): plate black `#151019`, steam yellow `#F8BA24` (the main action of a screen, the selected tab, the "on" state) and ember `#F2622A`. The **dark** theme is the plate; the **light** one is parchment with the same black header. Only the project's own emblem is used (`Branding\icon.png`, compiled into the exe): nothing from the game itself — no icon, picture or font.

- Every main tab, sub-tab and side-navigation page has a small **icon** (`UI\Controls\Icons.cs`) to find it at a glance. They come from the icon font that ships with Windows 10 and 11 (Segoe MDL2 Assets), not from the game; without that font the tabs show only their names.
- Long explanations are behind a round **?** button (click it): game plugin, language files, what each setting does. The screens keep short labels.
- Settings pages built from the server's `.json` files show one **tile per setting**, flowing over the whole width (a wide window shows several columns), with a filter box. On/off settings are switches; the unlock items of new accounts are **pills** with a tick (*All* / *None* shortcuts).
- The header adapts to a narrow window (the state pills shrink to a dot and a word, then the tabs get closer, then only the emblem is left); the minimum window is 1000 × 660.
- `Ctrl+Tab` goes through the main tabs. Tooltips, scroll bars and the title bar (Windows 11) follow the theme.

## Building

In the project folder (`C:\Steambirds`), run **`Build launcher.ps1`** (right click → *Run with PowerShell*). It:

1. looks for duplicated `.cs` files (copies like `Name-1.cs`, or the same file in two folders);
2. builds the server (`Server`), the plugin (`QuieterPlugin`) and the launcher as *self-contained* executables (nobody needs .NET installed);
3. assembles **`Launcher\dist`**, with the languages of `Launcher\lang` in `dist\lang` and the pictures of the map editor (`Launcher\editor-assets`, drawn by `SteambirdsGameFiles.exe` from your own copy of the game, not in git) in `dist\editor-assets`.

Then open `Launcher\dist\SteambirdsLauncher.exe`. Run the script again whenever the code of the server, the plugin or the launcher changes. The build log is `Launcher\build\build.log`.

## What the launcher keeps in its folder

| Path | What it is |
|---|---|
| `launcher.json` | launcher settings (game folder, player name, chosen server, typed IPs, search port, theme, languages, window) |
| `lang\` | launcher languages (`pt-BR.json`, `_template.json`, `README.md`) |
| `editor-assets\` | pictures and name lists the map editor draws (optional: without them it draws shapes) |
| `logs\` | server logs per session, client console logs, `server-report.txt` (report) and launcher errors |
| `server\` | the server and its data: `maps`, `data`, `loot_tables`, `ai`, `catalog` and the settings (`progression.json`, `items.json`, `dungeons.json`, `enemies.json`, `network.json`, `stores.json`, `world.json`, `social.json`; `flocks.json` is written by the server) |
| `server\accounts\<key>\` | one account per player (`steam_<SteamID>`): `account.json`, `inventory.json`, `acceptances.json`, plus `store.json` (purchases) and `social.json` (friends) |
| `SteambirdsGameFiles.exe` | takes the game's `configBinary`, the plugin data and the map editor pictures from your own copy of the game (`GameFiles\README.md`) |
| `server\_backup\` | copies made before files are replaced or accounts are changed or removed |
| `plugin\QuieterPlugin.dll` | the plugin, which *Install plugin* copies to the game's `BepInEx\plugins` |

Before sharing the `dist` folder, delete the accounts and logs (`server\accounts`, `server\client-uploads`, `server\_backup`, `logs`, `launcher.json`, `lang\*.missing.json`).

## First run: the game

When the launcher opens and `Steambirds.exe` is not in the game folder, a window opens **before the main window** with two ways to get the game: *Download the game with Steam* (the same flow as the Client tab) or *Choose the folder where the game is* (a game that is already on the PC; the launcher warns when its build is not 201219.1413). The window says clearly that the build is **not the latest version of the game**: the latest version has all the content of the game removed, so only the build of the Steam console command works. *Continue without the game* opens the launcher anyway (the Client tab and the map editor then have nothing to show until the game is set).

## Client tab

- **Download the game with Steam** (*Game* sub-tab, only while the game is missing or is the wrong build): copies `download_depot 386010 386011 58813776496064724`, opens the Steam console (`steam://open/console`) and follows the download (folder size and the `Depot download complete` / `failed` lines of Steam's `logs\console_log.txt`). When it is done, `steamapps\content\app_386010\depot_386011` becomes the game folder and gets `steam_appid.txt` (386010), so the game logs in with Steam outside the library. The Steam account needs the game in its library. The depot has no BepInEx: without it the game runs without the Quieter plugin.
- **Quieter plugin**: its state (*Installed*, *Update available*, *Not installed*, *BepInEx missing*) and an *Install plugin* / *Update plugin* button. The **?** explains what the plugin is (client bug fixes, quieter logs, no telemetry errors) and what happens without it (the game works, with those bugs and noisy logs; a PC that only joins other servers loses nothing else, because the server ships its own data tables in `server\data`). **PLAY** asks for confirmation when the plugin (or BepInEx) is missing, because the Flock Hall building, for one, only shows with the plugin.
- **Player**: the name the other players see. Empty = the name of the Steam account (read from Steam's `loginusers.vdf`). The **account** on a server is always the Steam account (the Steam ID comes from the login ticket), so changing the name keeps the pilot and the planes. The name goes in the path of `OZY_URL` (`http://<ip>:<port>/n/<name as hex>/ozy`), only for servers that accept it.
- **Servers**: the first row is **this PC**; below it, the servers found on the local network and on VPNs, and the typed IPs.
  - The search (*Search again*, or every minute with *Search by itself*) sends a UDP packet to port 6444 on every network of the PC: broadcast, the PCs of the ARP table and, on small (home) networks, every address. Radmin VPN and Hamachi are reached by broadcast and the ARP table.
  - *Connect by IP*: type the IP of the server PC (plus `:port` when the login is not 6443). The launcher checks `http://<ip>:<port>/revive/info` and adds the server to the list (typed IPs are saved).
  - Clicking a row chooses the server (bold row); a double click opens the game. *Remove from list* removes a typed IP.
- **PLAY**: opens the game on the chosen server. **This PC**: `OZY_URL=http://localhost:<port>/ozy` and `-connecttolocal`; with *Start the server of this PC with the game* checked, the server starts first. **Another PC**: `OZY_URL=http://<ip>:<port>/ozy` without `-connecttolocal` — the login answers with the Nexus of that server. While another PC is chosen, *Start the server of this PC with the game* is unchecked and grayed out.

## Discord Rich Presence

The launcher can show on the Discord profile of the player what they are doing (*Settings → General → Discord Rich Presence*, on by default; Discord must be open on the same PC). It talks to the Discord desktop app through its local pipe (`Core\DiscordIpc.cs`), with no SDK or DLL, and updates every 15 seconds only when something changed.

- **Playing** (the game opened by the launcher): the plane class and level, the pilot rank, where the player is (Rebel City, Training Grounds, the open world or the name of the dungeon) and "N of M" players of the server. The launcher asks the server it joined (`/revive/info`, field `playerDetails`) and picks the entry with the player name; without a name it uses the only player of the server.
- **Hosting** (the server of this PC is running): "Hosting <server name>", how many pilots are online and how many are in dungeons.
- Never shown: IP address, ports, account, Steam name or ID, or the player name. A button opens the community Discord invite.

Setup (once, by the maintainer): create an application at <https://discord.com/developers/applications>, put its *Application ID* in `LauncherInfo.DiscordApplicationId` (or in `launcher.json` as `"discordApplicationId"`) and, in *Rich Presence → Art Assets*, upload `Branding\icon.png` with the key `logo`. Without the ID the launcher shows "Needs the Discord application ID" in Settings and does nothing.

## Map editor tab

Draws the map of the server (`server\maps\*.json`) with the floor pieces and units of the game. New map from a dungeon of the game or an empty dungeon, open, save (the file name is the map id), undo and redo, and a check before saving that asks for the required objects (floor, entrance, exit point, something that ends the dungeon). A map with errors can be saved only as a draft (`"draft": true`). Every kind of data the server reads from a dungeon map has a place in it: floor and walls, entrance, exit, checkpoints, gates, rooms with enemies and waves, unit types with phases and summons, points, areas, lines, routes, signs, decoration and buildings. The lists of unit types, decorations and floor pieces show the game picture of each one. A unit type picks its AI in *Units → Enemy AI* (`"ai": "<id>"`), and *Open in the AI editor* goes to the Enemy AI tab with that type already in the simulator; how a unit moves and shoots is made there, not in the map editor. *Help* (F1) explains every tool, menu and tab and what it does in the game. Details, keys and code map: `Editor\README.md`.

## Enemy AI tab

Every AI and every behavior is its own file (`server\ai\brains\<id>.json`, `server\ai\behaviors\<id>.json`), so they can be edited, shared and loaded as needed; the server reads the folder again by itself when a file changes. Format: `server\ai\README.md`; code map: `docs\enemy-ai.md`.

- **Unit bar** (top): the unit the simulator flies, and the only AI the editor shows. The list holds the enemy types of `enemies.json` (`type: ai_chaser (AI Chaser) - AI chaser`, with the unit of their `template` and their multipliers, aim lead and free aim) and then the plain game units. Picking a type puts its own AI in the editor (the banner says which file it is). A plain unit, or a type with a built-in `behavior`, has no AI of its own: to edit any other AI or a behavior, select it in the list and press **Load on the unit** (or double-click it); it runs on that unit, you edit and save it there, and **Back to the unit's AI** returns the unit to its own AI (unsaved changes ask first). Changing the unit drops what was loaded.
- **Menu bar** (the same kind as the map editor's): *File* (New AI, New behavior, Save, Save all, Revert, Import..., Export..., Open folder, Reload), *Edit* (Duplicate, Rename..., Delete; the file goes to `server\_backup`) and *Help* (F1: what every part does and what it causes in the game). *Export...* writes one file with an AI and the behaviors it uses; *Import...* adds a shared file to yours without overwriting anything.
- **Left**: the files (the list is for choosing what to load; the menu acts on the selected file).
- **Middle**: the editor. An **AI** is a set of *states*; each state uses a *behavior* file or its own values (movement, weapons, contact) and has *transitions* with conditions (player closer than, time in state, health below, was hit, chance...). A **behavior** is one way of acting. Below it, the problems of the file (the same checks the server makes).
- **Right**: the simulator. It loads the art, speed, turning and guns of any game unit and runs the AI against a player plane that follows your clicks, flies in a circle or stands still, with the unit type's multipliers, health loss and "player gone" to test the conditions. It runs the same code as the server (`Shared\EnemyAi`).

To use an AI in the game, write `"ai": "<id>"` in a unit type of `enemies.json` or of a map; in the game chat `/brains` lists what the server loaded, and `enemies.json` has demo types (`/enemy ai_chaser`, `ai_bomber`, `ai_charger`, `ai_skirmisher`, `ai_sentry`, `ai_berserker`).

## Server tab

- **Start / Stop / Restart**, the state of the server, its name, ports and whether it accepts other PCs. The pages are in the side navigation (see the groups above).
- **Console**, **Progression**, **Items and loot**, **Enemies and difficulty**: screens built from the server's own `.json` files (one tile per setting).
- **Maps and dungeons**: the dungeon settings (entry mode, boss-room locking, fire-rate limits) and the table of dungeons (`dungeons.json`: map file, on/off, carrier in the Nexus, health multiplier, opening date); the maps of the Nexus and the tutorial are grayed out (the server uses fixed files for now).
- **World map**: the open world (`world.json`, `docs\open-world.md`). *Map* draws the world by zone with the players and the dungeon carriers (data on hover; read from `world-map.json` / `world-live.json`, which the server writes); *World settings* edits `world.json` (on/off, name, maximum players, how the world is built, size, seed, build in advance, edge wall) and *Reroll world* writes a new seed. The nested keys (enemies, carriers, spawn point) are still edited in the file.
- **Friends and flocks**: edits `social.json` (friends, flocks, founder token, teleport and Join Server) and lists the flocks and their members from `flocks.json` (read only: flocks are managed by the players in the Flock Hall; `docs\social.md`).
- **Store**: everything of `stores.json`. *General* (purchases on/off, gold and iron or free, discount on every price, pre-release products), *Prices and products* (the 294 products of the game with their price, the host's discount, stock and limit per account, the price the player pays "now", search, category filter and bulk changes on the products shown) and *Shelves* (which stores are open and what each section sells, with a product picker for include / exclude). Prices can only go down (the game draws the price itself): the discount, 0 to 100%, is the dial. Saving keeps the comments of the file and the server reads it again by itself.
- **New accounts**: the starting state of new accounts — pilot rank, the unlocked plane classes (pills), ingredients and storage items — with its own **starting money** panel (gold, iron...).
- **Accounts**: the **list of accounts** (one per player: name, Steam ID, last played, rank, planes, **portrait**, kills...), with the planes of the selected one. *Edit account...*, *Reroll portrait* and *Remove account* only work while the launcher's server is stopped. The money of an account (and its portrait, planes, unlocks...) is edited in the *Edit account* window; with the server running, give money with `/currency gold 5000` in the game chat. The **portrait** is the set of pieces of the pilot (slot, piece name, id); *Reroll portrait* picks a new one with the game's own rules from `server\data\portraits_config.txt` (the pictures of the pieces are Unity prefabs, so the launcher shows their names, not drawings).
- **Network and multiplayer**: the **addresses to share** with other players (one per network: local network, Radmin VPN, Hamachi...), the **Windows Firewall** rule (*Allow in the Windows Firewall* creates an inbound rule for `server\SteambirdsServer.exe` on every network; Windows asks for administrator permission) and the settings of `network.json`: server name, players of other PCs, server search, maximum players, who can use chat commands (`everyone`, `host`, `none`), welcome message and the **ports** (TCP login, UDP Nexus, tutorial, dungeon, open world and search). Ports and "players of other PCs" change when the server restarts; the server reads the rest again by itself.

## Playing with other people

1. **The host** opens *Server → Network and multiplayer*, checks that other PCs are accepted, clicks *Allow in the Windows Firewall* (once) and starts the server. The addresses to share are on the same page.
2. **The other players** open the *Client* tab: the server shows up in the list (same network or VPN) or is added with *Connect by IP*. Choose the row and click **PLAY**.
3. On the internet (no VPN), the host's router must forward the login port (TCP 6443) and the game ports (UDP 1111, 1112, 1113 and 1114) to the host's PC, and the players type the host's public IP. When a player on another PC is sent to another server of the host and does not arrive within 20 s, the server console shows `WARNING: ... did not arrive` with the UDP port to check.

Every player has their own account on the host's PC (pilot, planes, items, finished dungeons). Players **see each other** in the Nexus, the open world and the dungeons (planes, shots, chat and skills are relayed; players of the same dungeon type share one instance, and each one gets their own XP and loot); the tutorial stays private. The chat says who joins and leaves, and `/players` lists who is online. Friends, flocks and "Join Server" are in `docs\social.md`.

## Code

`Launcher\` (WinForms, .NET 8, no designer files):

- `Core\`: everything that is not a screen — server and game processes, logs, report, `.json` with comments, `dungeons.json`, `network.json` (`ServerNetworkFile`), accounts (`AccountStore`, `AccountFile`), server search (`ServerBrowser`, `LocalNetworks`), the Steam account (`SteamAccount`), the depot download (`DepotDownload`), the firewall rule (`FirewallRule`), languages (`Lang`), the files of the world map, the store and the flocks (`WorldMapFiles`, `StoreRules`, `FlockFiles`).
- `Core\GameData\`: reading the game's configBinary (classes, unlocks, currencies, ingredients, XP tables, languages).
- `Editor\`: the map editor tab (the document, the canvas, the forms and the checks; see `Editor\README.md`).
- `EnemyAi\`: the Enemy AI tab (`AiTab`, `AiStore`, the brain and behavior editors, `AiSimView`, `AiHelp`). The AI core itself is `..\Shared\EnemyAi`, compiled into the launcher and the server.
- `UI\`: the screens — `MainForm`, `ClientTab`, `DepotDownloadForm`, `ServerTab`, `NetworkPage`, `SettingsTab`, `AccountsPage`, `NewAccountsPage`, `AccountEditorForm`, `MapsPage`, `SettingsPage`, `StorePage` (with `StoreProductsView` and `StoreShelvesView`), `WorldPage` and `WorldMapPage` (with `WorldMapView`), `SocialPage`, `GameSetupForm` (first run), `HelpWindow` (shared by both editors), `SettingLabels`, `MoneyPanels`, `ConsoleView`, `Theme` (palettes, `Theme.P`; `Theme.AttachWindow` colors a window and what is added to it) and `LauncherContext` (state shared by the tabs, including the server list).
- `UI\Controls\`: the controls the launcher draws itself, all reading `Theme.P` when they paint — `MainNav` (header and main tabs), `NavPages` (side navigation), `ThemedTabControl`, `Card`, `ThemedButton` (`Ui.Button`, `Ui.Primary`, `Ui.Danger`, `Ui.Quiet`), `ThemedTextBox`, `ThemedComboBox`, `NumberBox` (replaces `NumericUpDown`), `ThemedCheckBox` (box or switch), `ThemedListView` (`Ui.Grid`), `ChipList` (the pills), `HelpButton` (the **?**), `StatusPill`, `SectionHeader`, `EllipsisLabel`, `NativeTheme` (dark scroll bars, title bar) and `Dpi`. New screens should use these (and `Ui.*`) instead of the plain WinForms controls, so they follow both themes.

Every text shown on screen goes through `L.T("...")` / `L.F("... {0}", value)`, and its translation goes into `Launcher\lang\pt-BR.json` and `_template.json`.
