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

- Version at the last update (24/11/2026): server and launcher **0.6.0**, plugin Steambirds Quieter **1.22.0**, client build
  **201219.1413**. Release notes: `docs/release-notes-0.6.0.md` (short, for GitHub) and `docs/update-notes-0.6.0.md` (full, English and Portuguese).
- **`CHECKLIST.md` (repo root) is the source of truth**: done / partial / missing per area, and the suggested MVP order.
  Update it when a feature changes state (keep its Portuguese style) and recompute its summary table (a bullet counts as
  done `- [x]`, partial `- [ ] 🟡` or missing `- [ ]`).
- Design notes per area live in `docs/`. They are snapshots (most in Portuguese, the newest ones in English), reviewed
  on 23/11/2026: when they disagree with the code or the checklist, trust the code and the checklist, and fix the note.
  Read only the one you need:

| File | Topic |
|---|---|
| `docs/multiplayer-and-accounts.md` | login flow, one account per player, `network.json`, server search, launcher 0.3.0, player relay, shared dungeons |
| `docs/dungeon-server.md` | dungeon server (port 1113): architecture, flow, Iron Lair / Bomb Factory test |
| `docs/dungeons-190828.md` | the 17 recreated dungeons of build 190828; what is original and what is rebuilt |
| `docs/dungeon-enemies-and-loot.md` | kamikaze bombs, weapon reach vs orbit distance, dungeon loot tables |
| `docs/dungeon-tuning.md` | 22/11 fixes: `dungeons.json` host settings, Mission Transport, 4x smaller maps, bullet limit, chain reaction, Nexus collision |
| `docs/items-and-loot.md` | items, authoritative inventory, loot bags, workshop, bit-alignment fix |
| `docs/progression.md` | plane and pilot XP, permadeath, skills, career rewards, `progression.json` |
| `docs/map-format.md` | map JSON format, coordinates, dungeon fields, `Maps.cs` |
| `docs/map-script.md` | the `script` section of a map: spawns, rules, conditions, actions, `/step` jumps; engine `Server\Scripting\` (English) |
| `docs/nexus-rebel-city.md` | Rebel City (Nexus) rebuild, buildings, docking, client uploads |
| `docs/tutorial.md` | walker tutorial (Training Grounds), ClientMapInfo, exit portal |
| `docs/enemies.md` | enemy AI protocol, scripted enemies, chat commands |
| `docs/enemy-ai.md` | Enemy AI editor: AI/behavior files (`Server\ai`), shared core `Shared\EnemyAi`, launcher tab and simulator (English) |
| `docs/stores.md` | stores: shelves, `ClientReceiveStoreContent` bytes, purchase, slots, `stores.json` (English) |
| `docs/open-world.md` | open world (`Basic`, port 1114): map generator, regions, enemy spawn sites, carriers, `world.json` (English) |
| `docs/social.md` | friends, flocks (Flock Hall), whispers, flock chat, teleport and "Join Server", `social.json`, `flocks.json` (English) |
| `docs/admin-tools.md` | admin and debug tools: debug settings, sounds, screenshots, server stats, inventory audit, password accounts (English) |
| `docs/update-notes-0.5.0.md` | release notes of 0.5.0 (English and Portuguese) |
| `docs/update-notes-0.6.0.md`, `docs/release-notes-0.6.0.md` | release notes of 0.6.0: full (English and Portuguese) and the short one for GitHub |
| `docs/update-notes-0.7.0.md` | release notes of 0.7.0, **in progress**: fed with every change until the release (English and Portuguese) |
| `docs/radmin-vpn-guide.md` | player-facing guide: install Radmin VPN, join the `Steambirds_Revive` network, host or join a server (English) |
| `docs/discord-server.md`, `docs/discord-start-here.md` | plan of the community Discord server and the ready-to-paste `#start-here` texts (not code) |

## Repository layout

- `Server\` — the server (.NET 8: ASP.NET for the HTTP "Ozy" API, Lidgren UDP for "Copper"). Project `SteambirdsServer.csproj`.
  - Folders by topic: `GameData`, `Progression`, `Items`, `Dungeons`, `World` (open world), `Network`, `Planes`, `Skills`,
    `Social` (friends, flocks, teleport), `Stores`, `Scripting` (map script engine), `Admin` (debug settings, sounds, screenshots, server stats, inventory audit).
    Top-level files: `Program*.cs` (`Program.cs` startup and `Main`, `Program.Transfers.cs` server switches and plane spawn,
    `Program.Messages.cs` host messages, `Program.Udp.cs` network loop, `Program.Http.cs` login), `Game*.cs` (client messages,
    split by topic), `Enemies*.cs` (AI, waves, view radius, guns), `Tutorial*.cs` (walker tutorial session; its script is in the map file), `Maps.cs` (map files), `Scripting\` (map script engine: `MapScript.cs` format + check, `MapScriptRunner*.cs` runner),
    `Accounts.cs`, `PlayerData.cs`, `Ground.cs`, `MapInfo.cs`, `Nexus.cs`, `WorldObjects.cs`, `PlaneSpawn.cs`, `ScreenEffects.cs`,
    `CopperReader.cs`, `MessageCatalog.cs`, `MessageLog.cs`.
  - Host configs (`Server\*.json`, with comments, reread when they change): `progression.json`, `items.json`, `enemies.json`,
    `dungeons.json`, `world.json`, `network.json`, `stores.json`, `social.json` (created with defaults on first start).
    `flocks.json`, `world-map.json` and `world-live.json` are written by the server (gitignored).
  - Maps: `maps\*.json` (format: `docs/map-format.md`, all field names in English). Loot: `loot_tables\`. Enemy AI: `ai\`.
  - Game data exported by the plugin (gitignored, copied from the game): `data\`. Item catalog written by the server: `catalog\`.
  - Accounts: `accounts\<key>\` (gitignored). Chat messages of the client: `Game*.cs`; message IDs: `MessageCatalog.cs`.
- `Launcher\` — WinForms .NET 8 launcher (0.6.0): `Core\`, `UI\` (screens) with `UI\Controls\` (themed controls), `Editor\` (map editor tab),
  `EnemyAi\` (Enemy AI tab and simulator), `lang\` (English is the default; `pt-BR.json`, `_template.json`).
  Docs: `Launcher\README.md`, `Launcher\Editor\README.md`, `Launcher\lang\README.md`.
- `Shared\EnemyAi\` — enemy AI core (file formats, runtime, flight, gun aim, simulator), compiled into **both** `Server` and `Launcher`
  (`<Compile Include="..\Shared\EnemyAi\*.cs">`). The AI files themselves live in `Server\ai\` (format: `Server\ai\README.md`).
- `QuieterPlugin\` — BepInEx plugin for the game client (net46): diagnostics, data exports, client-side fixes, and the
  export mode (`GameFilesExport.cs`, env `STEAMBIRDS_EXPORT`) that draws the map editor pictures and closes the game.
- `GameFiles\` — `SteambirdsGameFiles.exe` (.NET 8 console): takes configBinary, the plugin's data files and the editor
  pictures from each player's own copy of the game (installs BepInEx + plugin, opens the game once). See its README.
- `Branding\` — project logo and square emblem (SVG, PNG, ICO), our own drawing in the style of the original logo.
  Generated by `tools\make_logo.py` (Pillow only); see `Branding\README.md`. Never copy the game's own logo into the repo.
- `tools\` — `make_logo.py` and `scale_dungeon_maps.py` (shrinks a dungeon map; refuses to scale twice) and `make_map_maker_helper.py` (generates the three catalog maps `Server\maps\map_maker_floors.json`, `map_maker_decor.json` and `map_maker_enemies.json`: every floor piece, decoration and enemy, from `Server\data`). The scripts that
  generated the 17 dungeon maps are gone: the `.json` files are the source now.
- `docs\` — design notes (see the table above). `Release\` — output of `Build launcher release.ps1` (gitignored).
- `decompilado\` — decompiled client (Assembly-CSharp and firstpass). Reference only, very large, gitignored:
  **search it** (`rg` / `Select-String`), never read whole folders. (On the maintainer's PC the folders are still named
  `decompilado\` and `VersoesAntigas\`; `Clean project.ps1 -RenameFolders` renames them to `decompiled\` and `OldVersions\`.
  Comments in the code may use either name. Use whichever exists.)
- `VersoesAntigas\`, `Assets\`, `Asset ripper\` — older builds and extracted assets (gitignored, not ours).
- `Chat commands.md` — in-game chat commands (the game's and ours).

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

The server is run and the game opened from the launcher (Server and Client tabs; the old `Run server.ps1`, `Run game.ps1`
and `Build server release.ps1` were removed; some code comments still mention them). Server output and game logs go to
`Launcher\dist\logs\` and, on stop, to `server-report.txt` (large: search it instead of reading it all). `Launcher\dist` is
the testing copy (it holds the maintainer's accounts and game data: never share it; the release zip is the shareable one).

In-game testing is done by the user. When they report a test, read `server-report.txt`,
`Launcher\dist\logs\` and `Player.log` with targeted searches.

## Rules

- Identifiers, comments and READMEs in English (Portuguese optional). There is no compatibility layer for the old
  Portuguese names: files, folders, map fields and plugin data written by older versions are not read. Portuguese only
  survives in most `docs\` snapshots, the launcher's translation (`Launcher\lang\pt-BR.json`), `CHECKLIST.md`, and the game's own data.
  Every launcher text goes through `L.T(...)` / `L.F(...)` and needs its entry in `pt-BR.json` and `_template.json`.
- Split different parts of the project into different files so each one can be maintained on its own. Don't grow big
  files (`Enemies.cs`, `Game.cs` and the like are split into `partial` files by topic): read only the ranges you need.
- Maps and game content go in data files, never hardcoded (map editor, community dungeons). Map field names are in
  English (`floor`, `decor`, `buildings`...).
- Launcher: a feature that does not exist yet in the game/server still gets its menu, greyed out and disabled. New screens use
  the themed controls (`UI\Controls`, `Ui.*`) so they follow the dark and light themes, and no picture or icon of the game.
- Stay as close as possible to the original game. Anything rebuilt without original data must be documented as a
  reconstruction and, when it makes sense, be configurable by the host.
- Never commit game files (assets, DLLs, `configBinary`, decompiled code); `.gitignore` covers them.
  Commit / push only when the user asks.
- Write complete changes in place, then tell the user which files changed and what to test in the game.
- **0.7.0 is in progress:** every change to the game, the server, the launcher or the editors gets a line in
  `docs/update-notes-0.7.0.md` (English and Portuguese, in the right section), in the same change. At the release the file is closed and
  the next one (`update-notes-<next>.md`) starts.
- When a feature, file or command changes, update the docs in the same change: `CHECKLIST.md`, the matching `docs\` note, the
  READMEs and `Chat commands.md`. (A big part of the docs went stale between 16/11 and 23/11 because they were not.)

## Key facts

- Client build **201219.1413**, depot `download_depot 386010 386011 58813776496064724`.
  Game folder: `C:\Program Files (x86)\Steam\steamapps\content\app_386010\depot_386011`.
  Player.log: `%USERPROFILE%\AppData\LocalLow\SpryFox\Steambirds Alliance\Player.log`.
- Ports (`network.json`): HTTP 6443, Nexus UDP 1111, tutorial 1112, dungeon 1113, open world 1114, server search 6444.
  One UDP game server per role (`Program.cs`: Nexus 0, World 1, Dungeon 2, Tutorial 3). Players in the same room (Nexus, world, one
  dungeon instance) see each other through `Network\PlayerRelay.cs`; the tutorial is private.
- The server reads the game's `configBinary` (DarkConfig + LZMA) directly: units, items, XP tables, skills, unlocks,
  buffs, texts. What lived only on the original server (enemy AI, dungeon room layouts, named loot tables, XP-per-kill
  formula, the Rebel City map) is empty in every client build checked and has to be rebuilt.
- Protocol gotchas:
  - When embedding unitOptions, never `m.Write(NetBuffer)` if the buffer can end with loose bits (bools); write
    `options.Data, 0, options.LengthBytes`, like the client's `NetBufferCopperSerializer`.
  - Spawns must always include velocity (0, 0); otherwise the client extrapolates with the "unset" value.
  - The Mono string hash used for `templateHash` and effect ids is in `GameData\MonoHash.cs`.
  - Angles: 0 = +Y, 90 = +X, clockwise; direction = (sin r, cos r).
