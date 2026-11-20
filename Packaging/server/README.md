# Steambirds Alliance Revive — Server

Community server for **Steambirds Alliance** (the original servers were shut down in 2021). This package runs without
compiling anything and without installing .NET.

Unofficial community project: https://github.com/Vithor14/Steambirds-Alliance-Revive

## What is in this folder

| File | What it is |
|---|---|
| `SteambirdsServer.exe` | the server (login on TCP 6443; Nexus, tutorial and dungeons on UDP 1111, 1112 and 1113; server search on UDP 6444) |
| `Start server.cmd` | starts the server from this folder and keeps the window open if it stops |
| `SteambirdsGameFiles.exe` | takes the game files the server needs from **your own copy** of the game (see below) |
| `plugin\QuieterPlugin.dll` | the Steambirds Quieter plugin, installed in the game by `SteambirdsGameFiles.exe` |
| `network.json` | server name, who can join, maximum players, chat commands, ports |
| `progression.json`, `items.json`, `enemies.json`, `dungeons.json` | XP, items and loot, enemies and difficulty, dungeons |
| `maps\` | Nexus, tutorial and dungeon maps (format in `maps\README.md`) |
| `loot_tables\` | loot tables |

The `.json` files accept `//` comments; each setting is explained in the file. The server writes `accounts\` (one
folder per player), `catalog\` and `_backup\` here while it runs.

## The game files are not in this download

`configBinary` and the data files (`units_config.txt`, `actions_config.txt`, `mapinfo_config.txt`,
`portraits_config.txt`) come from the game and belong to its owners, so they cannot be shared here. Run
**`SteambirdsGameFiles.exe`** once on a PC that has Steam and the game:

1. It finds the game (the folder of `download_depot 386010 386011 58813776496064724`; if it is missing, it tells you how
   to download it from the Steam console).
2. It copies `configBinary` to `data\`.
3. It installs **BepInEx 5** (downloaded from https://github.com/BepInEx/BepInEx/releases) and the plugin in the game
   folder, starts this server and opens the game once. Steam must be open. If the game asks something (language,
   terms), answer it.
4. The plugin writes the data files and draws the pictures of the map editor, then closes the game by itself.
5. The tool copies the data files to `data\` and the pictures to `editor-assets\`.

Log: `logs\game-files.log`. Run it again after a new version of this package (the plugin may have changed).

`editor-assets\` is for the **map editor** of the launcher: copy that folder next to `SteambirdsLauncher.exe` (or run
`SteambirdsGameFiles.exe` from the launcher folder).

## Hosting

1. Run `SteambirdsGameFiles.exe` once (above).
2. Double-click `Start server.cmd`. The first time, Windows Firewall asks for permission: allow it on the networks
   other players use (private network, Radmin VPN, Hamachi...).
3. Players join with the launcher (*Client* tab: the server shows up in the list on the same network or VPN, or use
   *Connect by IP*) and click **PLAY**.
4. Over the internet without a VPN, forward TCP 6443 and UDP 1111, 1112 and 1113 on the router to this PC; players use
   your public IP.

Without the launcher, a player opens the game with the environment variable `OZY_URL=http://<server IP>:6443/ozy`
(on the server PC itself: `OZY_URL=http://localhost:6443/ozy` and the argument `-connecttolocal`).

To stop the server press Ctrl+C in its window. Accounts stay in `accounts\`: keep that folder when you update the
package (copy the new files over the old ones, or move `accounts\` and your `.json` settings to the new folder).

## Problems

- *"no permission to write in the game folder"*: run `SteambirdsGameFiles.exe` as administrator.
- The export did not finish: look at `BepInEx\LogOutput.log` in the game folder (lines with "Export mode").
- The game does not log in: Steam must be open and signed in with an account that has Steambirds Alliance.

---

## Português (resumo)

Servidor comunitário do Steambirds Alliance, pronto para rodar (não precisa compilar nem instalar o .NET).

1. Rode **`SteambirdsGameFiles.exe`** uma vez num PC com a Steam e o jogo: ele pega da **sua** cópia do jogo os
   arquivos que não podem ir para o GitHub (`configBinary` e os dados do plugin), instala o BepInEx e o plugin, abre o
   jogo uma vez (a Steam precisa estar aberta) e grava tudo em `data\` e as imagens do editor de mapas em
   `editor-assets\` (copie essa pasta para junto do `SteambirdsLauncher.exe`).
2. Abra o **`Start server.cmd`** e libere no Firewall do Windows.
3. Os jogadores entram pelo launcher (aba *Client*: lista de servidores ou *Conectar por IP*).
4. Pela internet sem VPN: redirecione TCP 6443 e UDP 1111, 1112 e 1113 no roteador.

As contas ficam em `accounts\`: guarde essa pasta ao atualizar o pacote.
