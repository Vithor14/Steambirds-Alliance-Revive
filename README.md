# Steambirds Alliance Revive

> **Join the community on Discord:** [discord.gg/sNwTk7T4mu](https://discord.gg/sNwTk7T4mu)

Community revival of **Steambirds Alliance**, the Steam flight-combat game whose official servers were shut down in 2021.

The project rebuilds the server the game client expects, as close as possible to the original, and adds a **launcher**
(join or host a server, manage accounts and settings) and a **map editor** for dungeons. It is an unofficial,
community-made project. The game itself is **not** included: you need your own copy from Steam.

## What it does

- **Server**: login, one account per player (saved on the host's PC), planes and hangar, XP and permadeath, skills,
  items, loot, workshop, stores, Rebel City (the Nexus), the walker tutorial, 17 recreated dungeons, an **open world**
  of the twelve original zones, or a random layout from a seed (regions, enemy spawns, dungeon carriers) and **friends and flocks**.
- **Multiplayer**: host for LAN or a VPN (Radmin, Hamachi) or over the internet; players share the Nexus, the open world and
  the dungeons, can whisper, talk in their flock, and teleport to or join a friend. New to VPNs? Follow the
  [Radmin VPN step-by-step guide](docs/radmin-vpn-guide.md).
- **Launcher**: downloads the game through Steam, lists servers, hosts a server, and edits accounts, stores,
  difficulty, dungeons, the open world and the social settings without touching any file.
- **Map editor**: builds dungeon, Nexus and tutorial maps from the game's own assets, as files the server reads.
- **Enemy AI editor**: builds enemy behavior (states, summons, shields, boss phases) as data files, with a graph view and a simulator.

Not done yet: quests and missions, the Combat Sim, the original boss AI and most dungeon-specific scripts (only
Iron Lair and Bomb Factory are switched on in `dungeons.json`; the others are ready to try). The last big in-game test is 23/11/2026:
friends, the default flock, loot, and six dungeons. Whisper, flock chat, teleport, safe trade, most store shelves, dungeon keys and the
0.6.0–0.8.0 editor work still need a game test. See [CHECKLIST.md](CHECKLIST.md), [0.7.1](docs/release-notes-0.7.1.md) and
[what is in progress for 0.8.0](docs/update-notes-0.8.0.md).

## Play or host

1. Download the latest `.zip` from [Releases](https://github.com/Vithor14/Steambirds-Alliance-Revive/releases/) and
   extract it anywhere.
2. Run `SteambirdsLauncher.exe`. The first time it helps you download the game with Steam
   (`download_depot 386010 386011 58813776496064724`, client build 201219.1413).
3. **Client** tab: pick a server (*This PC*, one found on the network, or *Connect by IP*) and click **PLAY**.
   **Server** tab: start your own server and share the addresses shown in *Network and multiplayer*.

Requirements: Windows 10 or 11, Steam with Steambirds Alliance in your library. No .NET install needed.

The game's own files are never shared here. `SteambirdsGameFiles.exe` takes the data the server needs from your own
copy of the game.

## Build from source

Needs the .NET 8 SDK and PowerShell.

```powershell
git clone https://github.com/Vithor14/Steambirds-Alliance-Revive.git C:\Steambirds
cd C:\Steambirds
.\"Build launcher.ps1"
```

This publishes the server, plugin and launcher into `Launcher\dist`. `.\"Build launcher release.ps1"` builds the
zip for GitHub Releases (launcher, server, `SteambirdsGameFiles.exe` and plugin, with no game files). The server and the game are started from the launcher. Chat commands are in
[Chat commands.md](Chat%20commands.md).

## Repository

| Path | What it is |
|---|---|
| `Server\` | The game server (.NET 8): HTTP login and UDP game servers; maps, loot tables and settings as data files |
| `Launcher\` | WinForms launcher (0.7.1) with the map editor and Enemy AI tabs |
| `Shared\EnemyAi\` | Enemy AI core, shared by the server and the launcher |
| `QuieterPlugin\` | BepInEx plugin for the game client (data exports and client-side fixes) |
| `GameFiles\` | `SteambirdsGameFiles.exe`: gets the needed files from your own game copy |
| `docs\` | Design notes (a few in English, most in Portuguese), release notes and the Discord plan |
| `Server\maps\`, `Server\ai\`, `Server\loot_tables\` | Maps, enemy AI and loot tables: data files you can edit and share |

## Contributing

Maps, dungeons, enemy AI and loot are data files, so the community can make and share them without touching code.
Read [CONTRIBUTING.md](CONTRIBUTING.md) first; [CHECKLIST.md](CHECKLIST.md) lists what is missing.

## License

MIT, see [LICENSE](LICENSE). Steambirds Alliance and its assets belong to their owners; this project contains none of them.
