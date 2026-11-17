# Steambirds Alliance Revive

Community revival of **Steambirds Alliance**, a Steam flight-combat game whose original servers were shut down in 2021.
The project rebuilds the server that the game client expects, as close as possible to the original, and adds a launcher
(to host and to join servers, with accounts saved on the host's PC) and a map editor (to build dungeons and maps the
server reads).

This is an **unofficial community project**. The game itself is not included: you need your own copy from Steam, and
the game's files, decompiled code and extracted assets are never stored in this repository.

- **Status and roadmap:** [CHECKLIST.md](CHECKLIST.md) (done, partial and missing, per area; the source of truth).
- **How to help:** [CONTRIBUTING.md](CONTRIBUTING.md).
- **Design notes** (Portuguese): [docs/](docs/).

## Quick Download (Releases)

If you just want to play or host without compiling from source, you can download the latest prebuilt launcher package directly from the [GitHub Releases page](https://github.com/Vithor14/Steambirds-Alliance-Revive/releases/). 

1. Download the latest release `.zip` asset.
2. Extract the archive anywhere on your computer.
3. Run `SteambirdsLauncher.exe`.

## What works today

- Login and one account per player, with the pilot, planes, items and finished dungeons saved on the host's PC.
- Planes, classes and the hangar, XP for planes and pilots (real-time share, permadeath), skills and loadouts.
- Items, inventory, loot bags and the Workshop (reforging and supercharging) read from the game's own data.
- Rebel City (the Nexus) rebuilt from the Open Beta, the walker tutorial (Training Grounds), and 17 recreated dungeons
  (only two of them are switched on by default: Iron Lair and Bomb Factory).
- The launcher: download the game through Steam, server list (LAN, Radmin VPN, Hamachi, typed IP), hosting, accounts
  and the network settings.
- The map editor for dungeons, Nexus and tutorial maps.

Not yet: multiplayer between players in the same world, stores that sell, quests and missions, Combat Sim, and most
dungeon scripts. The checklist has the full list.

## Requirements

**To play or host (using the prebuilt release or launcher package):**

- Windows 10 or 11.
- Steam, with a copy of Steambirds Alliance in the account's library (the launcher downloads it with the depot command).
- Nothing else: the launcher package is self-contained and does not need .NET installed.

**To build from source:**

- .NET 8 SDK.
- Python 3 (optional, only for the dungeon generator and the editor image builder; `pip install pillow` for the previews).
- PowerShell 5.1 or newer (it comes with Windows).

## Installation (building from source)

1. Clone the repository into `C:\Steambirds` (the scripts assume this folder; change `$raiz` in the scripts if you use another one):

   ```powershell
   git clone https://github.com/Vithor14/Steambirds-Alliance-Revive.git C:\Steambirds
   ```

2. Build the launcher package (server, plugin and launcher) into `Launcher\dist`:

   ```powershell
   cd C:\Steambirds
   .\"Build launcher.ps1"
   ```

   Then open `Launcher\dist\SteambirdsLauncher.exe`. In the **Client** tab, *Download the game with Steam* installs the game.

3. Data taken from the game. The server needs the game's configuration (`configBinary`, read straight from the installed
   game) and a few text files exported from the game by the plugin. To create those files:

   ```powershell
   .\"Build plugin.ps1"      # copies the plugin into the game's BepInEx\plugins
   ```

   Start the game once with the plugin installed. It writes `units_config.txt`, `actions_config.txt`,
   `mapinfo_config.txt` and `portraits_config.txt` into the game's `BepInEx` folder. Copy them to `Server\data`
   (that folder is not in git: it holds the game's data). The server also works without them, with fewer features.

## Running

- **Launcher (recommended):** open `SteambirdsLauncher.exe` (from the downloaded release zip or `Launcher\dist\`). Use the **Server** tab to host, the **Client**
  tab to join (*This PC* or any server in the list), and **Play**.
- **Server alone (for development):** `.\"Run server.ps1"` runs the server in a console; Ctrl+C writes
  `server-report.txt` with the server output and the game logs. It is interactive (it asks some questions).
- **Game against a local server:** `.\"Run game.ps1"` (Steam must be open).
- **Chat commands** for the game are listed in [Chat commands.md](Chat%20commands.md). Type `/help` in the game.

Ports used by the server (changed in `Server\network.json`): HTTP login 6443 (TCP), Nexus 1111, tutorial 1112 and
dungeons 1113 (UDP), server search 6444 (UDP).

## Repository layout

| Path | What it is |
|---|---|
| `Server\` | The game server (.NET 8): HTTP login API and UDP game servers for the Nexus, the tutorial and the dungeons. |
| `Server\maps\` | Map files (`nexus.json`, `tutorial_walker.json`, `dungeon_*.json`); format in `Server\maps\README.md`. |
| `Server\*.json` | Settings: progression, items, enemies, dungeons, network. |
| `Server\loot_tables\` | Loot tables (one file each). |
| `Server\Compat\` | Converts files and folders written by older versions (Portuguese names). |
| `Launcher\` | The launcher (WinForms): client, server hosting, settings and the map editor. |
| `QuieterPlugin\` | BepInEx plugin for the game client: data exports and client-side fixes. |
| `tools\` | Python tools: dungeon generator, editor image builder, map migration. |
| `docs\` | Design notes (Portuguese). |
| `CHECKLIST.md` | Status of every area and the suggested order of work. |

## License

MIT, see [LICENSE](LICENSE). Steambirds Alliance and its assets belong to their owners; this project contains none of them.
