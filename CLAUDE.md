# Steambirds Alliance Revive — CLAUDE.md

> Talk to the user (Vithor) in **Brazilian Portuguese**.
> Code identifiers, comments and READMEs in **English** (a Portuguese version is optional).

## What this project is

Community revival of **Steambirds Alliance** (Unity client, original servers shut down in 2021). We rebuild the
server the client expects, as close as possible to the original game, plus a launcher and a map editor.
Maintained 100% by the community, with some additions and improvements.

- **MVP:** understand and implement every standard game feature the way it worked when the game was hosted.
- **Intermediate goal:** a base launcher published on GitHub so the community can play, change, improve and make maps.
- **Final goal:** a standalone launcher. On a PC that never ran the game it opens the Steam console with
  `download_depot 386010 386011 58813776496064724` ready to paste, places the files the game needs to log in
  through Steam, and then runs the game.
  - **Client tab:** player name, server list (LAN and VPNs such as Radmin and Hamachi), button to open the game.
    The in-game server list should mirror the launcher list and let the player move between servers
    (depends on the original architecture).
  - **Server tab:** host a server that other players (or the host's own client) join; accounts saved on the host PC;
    XP, difficulty and other settings; accounts page (initial state of new accounts: level, items, unlocks; view,
    edit and delete accounts); choose the map file for the Nexus, the tutorial and each dungeon, with a preview;
    choose when each dungeon becomes available; world map with each player's position.
  - **Map editor:** dungeons and the world map, built from the game's own assets, exporting files the server reads,
    so the community creates many dungeon versions and hosts pick the ones they want.

## Where things stand

- **`CHECKLIST.md` (repo root) is the source of truth**: done / partial / missing per area, and the suggested MVP order.
  Update it when a feature changes state (keep its Portuguese style).
- Design notes per area live in `docs/` (Portuguese). They are snapshots up to 16/11/2026: when they disagree with
  the code or the checklist, trust the code and the checklist. Read only the one you need:

| File | Topic |
|---|---|
| `docs/multiplayer-and-accounts.md` | login flow, one account per player, `network.json`, server search, launcher 0.3.0, next multiplayer step |
| `docs/dungeon-server.md` | dungeon server (port 1113): architecture, flow, Iron Lair / Bomb Factory test |
| `docs/dungeons-190828.md` | the 17 recreated dungeons of build 190828; what is original and what is rebuilt |
| `docs/dungeon-enemies-and-loot.md` | kamikaze bombs, weapon reach vs orbit distance, dungeon loot tables |
| `docs/dungeon-tuning.md` | 22/11 fixes: `dungeons.json` host settings, Mission Transport, 4x smaller maps, bullet limit, chain reaction, Nexus collision |
| `docs/items-and-loot.md` | items, authoritative inventory, loot bags, workshop, bit-alignment fix |
| `docs/progression.md` | plane and pilot XP, permadeath, skills, career rewards, `progression.json` |
| `docs/map-format.md` | map JSON format, coordinates, dungeon fields, `Maps.cs` |
| `docs/nexus-rebel-city.md` | Rebel City (Nexus) rebuild, buildings, docking, client uploads |
| `docs/tutorial.md` | walker tutorial (Training Grounds), ClientMapInfo, exit portal |
| `docs/enemies.md` | enemy AI protocol, scripted enemies, chat commands |
| `docs/enemy-ai.md` | Enemy AI editor: AI/behavior files (`Server\ai`), shared core `Shared\EnemyAi`, launcher tab and simulator (English) |
| `docs/stores.md` | stores: shelves, `ClientReceiveStoreContent` bytes, purchase, slots, `stores.json` (English) |
| `docs/open-world.md` | open world (`Basic`, port 1114): map generator, regions, enemy spawn sites, carriers, `world.json` (English) |
| `docs/social.md` | friends, flocks (Flock Hall), whispers, flock chat, teleport and "Join Server", `social.json`, `flocks.json` (English) |

## Repository layout

- `Server\` — the server (.NET 8: ASP.NET for the HTTP "Ozy" API, Lidgren UDP for "Copper"). Project `SteambirdsServer.csproj`.
  - Folders by topic: `GameData`, `Progression`, `Items`, `Dungeons`, `World`, `Network`, `Planes`, `Skills`, `Compat`.
    Top-level files: `Program*.cs` (`Program.cs` startup and `Main`, `Program.Transfers.cs` server switches and plane spawn,
    `Program.Messages.cs` host messages, `Program.Udp.cs` network loop, `Program.Http.cs` login), `Game.cs` (client messages), `Enemies.cs` (AI, waves),
    `Tutorial.cs` (walker tutorial script), `Maps.cs` (map files), `Accounts.cs`, `PlayerData.cs`, `Ground.cs`, `MapInfo.cs`,
    `Nexus.cs`, `WorldObjects.cs`, `PlaneSpawn.cs`, `CopperReader.cs`, `MessageCatalog.cs`, `MessageLog.cs`.
  - Host configs: `progression.json`, `items.json`, `enemies.json`, `dungeons.json`, `world.json`, `network.json`.
  - Maps: `maps\*.json` (format: `docs/map-format.md`). Loot: `loot_tables\`.
  - Game data exported by the plugin (gitignored, copied from the game): `data\`. Item catalog written by the server: `catalog\`.
  - Accounts: `accounts\<key>\` (gitignored). Chat messages of the client: `Game.cs`; message IDs: `MessageCatalog.cs`.
- `Launcher\` — WinForms .NET 8 launcher: `Core\`, `UI\`, `Editor\` (map editor tab), `EnemyAi\` (Enemy AI tab and simulator), `lang\`.
  Docs: `Launcher\README.md`, `Launcher\Editor\README.md`.
- `Shared\EnemyAi\` — enemy AI core (file formats, runtime, flight, gun aim, simulator), compiled into **both** `Server` and `Launcher`
  (`<Compile Include="..\Shared\EnemyAi\*.cs">`). The AI files themselves live in `Server\ai\` (format: `Server\ai\README.md`).
- `QuieterPlugin\` — BepInEx plugin for the game client (net46): diagnostics, data exports, client-side fixes, and the
  export mode (`GameFilesExport.cs`, env `STEAMBIRDS_EXPORT`) that draws the map editor pictures and closes the game.
- `GameFiles\` — `SteambirdsGameFiles.exe` (.NET 8 console): takes configBinary, the plugin's data files and the editor
  pictures from each player's own copy of the game (installs BepInEx + plugin, opens the game once). See its README.
- `Branding\` — project logo and square emblem (SVG, PNG, ICO), our own drawing in the style of the original logo.
  Generated by `tools\make_logo.py` (Pillow only); see `Branding\README.md`. Never copy the game's own logo into the repo.
- `docs\` — design notes (Portuguese snapshots, see the table above).
- `decompiled\` — decompiled client (Assembly-CSharp and firstpass). Reference only, very large, gitignored:
  **search it** (`rg` / `Select-String`), never read whole folders.
- `OldVersions\`, `Assets\`, `Asset ripper\` — older builds and extracted assets (gitignored, not ours).
- `Chat commands.md` — in-game chat commands.

## Build and verify — do it yourself after every change

The old workflow (Claude in the claude.ai chat) could not compile, so code was reviewed by hand at a high token cost.
Here you can compile: **after each change, build, read the errors, fix, and repeat until it builds.**

Quick, non-interactive compile checks (run from `C:\Steambirds`):

```powershell
dotnet build Server\SteambirdsServer.csproj -nologo -v q -clp:ErrorsOnly
dotnet build Launcher\SteambirdsLauncher.csproj -nologo -v q -clp:ErrorsOnly
dotnet build QuieterPlugin\QuieterPlugin.csproj -c Release -nologo -v q -clp:ErrorsOnly
dotnet build GameFiles\SteambirdsGameFiles.csproj -nologo -v q -clp:ErrorsOnly
```

- Build only the projects you touched.
- The plugin references the game's DLLs at
  `C:\Program Files (x86)\Steam\steamapps\content\app_386010\depot_386011` (BepInEx and `Steambirds_Data\Managed`).
- If the server build fails because a file is locked (MSB3027 / MSB3021), the server is running: ask the user to stop it.
- Duplicate `.cs` copies (`Name-1.cs`, `Name (1).cs`, `Name - Copy.cs`, or the same file in two folders, such as
  `LootDrops.cs` in the root and in `Items\`) break the build with CS0101 / CS0111. Never leave copies behind.

The user's scripts are **interactive** (`Read-Host`). Do not run them yourself unless the user asks, or the session hangs:

- `Build launcher.ps1` — publishes server and launcher (self-contained) into `Launcher\dist`, copies maps, data,
  configs and the plugin. Log: `Launcher\build\build.log`.
- `Build plugin.ps1` — builds the plugin and copies it into the game's `BepInEx\plugins`.
- `Build launcher release.ps1` — builds `Release\SteambirdsLauncher-<version>-win-x64.zip` for GitHub Releases (launcher,
  server, `SteambirdsGameFiles.exe`, plugin, settings, maps; refuses to zip game files, accounts or logs).
- `Push to GitHub.ps1 "message"` — commit and push.
- `Clean project.ps1` — removes build output, logs, duplicate copies and other garbage (`-WhatIf` only lists them).

The server is run and the game opened from the launcher (Server and Client tabs; the old `Run server.ps1` and `Run game.ps1`
were removed). Server output and game logs go to `Launcher\dist\logs\` and, on stop, to `server-report.txt`
(large: search it instead of reading it all).

In-game testing is done by the user. When they report a test, read `server-report.txt`,
`Launcher\dist\logs\` and `Player.log` with targeted searches.

## Rules

- Identifiers, comments and READMEs in English (Portuguese optional). There is no compatibility layer for the old
  Portuguese names: files, folders, map fields and plugin data written by older versions are not read. Portuguese only
  survives in the `docs\` snapshots, the launcher's translation (`Launcher\lang\pt-BR.json`, `LEIA-ME.md`) and the game's own data.
- Split different parts of the project into different files so each one can be maintained on its own. Don't grow big
  files (`Enemies.cs`, `Game.cs` and the like are split into `partial` files by topic): read only the ranges you need.
- Maps and game content go in data files, never hardcoded (map editor, community dungeons). Map field names are in
  English (`floor`, `decor`, `buildings`...).
- Launcher: a feature that does not exist yet in the game/server still gets its menu, greyed out and disabled.
- Stay as close as possible to the original game. Anything rebuilt without original data must be documented as a
  reconstruction and, when it makes sense, be configurable by the host.
- Never commit game files (assets, DLLs, `configBinary`, decompiled code); `.gitignore` covers them.
  Commit / push only when the user asks.
- Write complete changes in place, then tell the user which files changed and what to test in the game.

## Key facts

- Client build **201219.1413**, depot `download_depot 386010 386011 58813776496064724`.
  Game folder: `C:\Program Files (x86)\Steam\steamapps\content\app_386010\depot_386011`.
  Player.log: `%USERPROFILE%\AppData\LocalLow\SpryFox\Steambirds Alliance\Player.log`.
- Ports (`network.json`): HTTP 6443, Nexus UDP 1111, tutorial 1112, dungeon 1113, open world 1114, server search 6444.
- The server reads the game's `configBinary` (DarkConfig + LZMA) directly: units, items, XP tables, skills, unlocks,
  buffs, texts. What lived only on the original server (enemy AI, dungeon room layouts, named loot tables, XP-per-kill
  formula, the Rebel City map) is empty in every client build checked and has to be rebuilt.
- Protocol gotchas:
  - When embedding unitOptions, never `m.Write(NetBuffer)` if the buffer can end with loose bits (bools); write
    `options.Data, 0, options.LengthBytes`, like the client's `NetBufferCopperSerializer`.
  - Spawns must always include velocity (0, 0); otherwise the client extrapolates with the "unset" value.
  - The Mono string hash used for `templateHash` and effect ids is in `GameData\MonoHash.cs`.
  - Angles: 0 = +Y, 90 = +X, clockwise; direction = (sin r, cos r).
