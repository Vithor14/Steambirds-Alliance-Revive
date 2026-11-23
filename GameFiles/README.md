# Steambirds Game Files (`GameFiles\`)

`SteambirdsGameFiles.exe` takes from the player's **own copy** of Steambirds Alliance the files that cannot be published
on GitHub (they belong to the game), and puts them where the server and the map editor read them. It ships in the
launcher release zip (`Build launcher release.ps1`) and in the launcher package (`Build launcher.ps1`).

## What it does

1. Finds the game: `--game`, `STEAMBIRDS_GAME`, then `<Steam>\steamapps\content\app_386010\depot_386011` (the folder of
   `download_depot 386010 386011 58813776496064724`). Without the game it shows the depot command and can open the
   Steam console.
2. Copies `Steambirds_Data\StreamingAssets\configBinary` to the server's `data` folder.
3. Installs **BepInEx 5** in the game folder when it is missing (official zip from
   `github.com/BepInEx/BepInEx/releases`, 64- or 32-bit by the header of `Steambirds.exe`; a `BepInEx_win_x64_*.zip`
   next to the tool is used instead, for PCs without internet) and copies `plugin\QuieterPlugin.dll` to
   `BepInEx\plugins`. Creates `steam_appid.txt`.
4. Starts the server of the package (or uses one already answering on the HTTP port of `network.json`) and opens the
   game once, like the launcher's *This PC* (`OZY_URL=http://localhost:<port>/ozy`, `-connecttolocal`), with
   `STEAMBIRDS_EXPORT=<temporary folder>`.
5. In that mode the plugin (`QuieterPlugin\GameFilesExport.cs`) waits for the game to load, lets QuieterPlugin write its
   data files to the BepInEx folder, draws a top view of every floor piece and unit art with the game's own meshes,
   materials and light, writes `thumbs.json` and `export_done.txt`, and closes the game.
6. Stops the server it started and copies:

| From | To | Used by |
|---|---|---|
| `BepInEx\units_config.txt`, `actions_config.txt`, `mapinfo_config.txt`, `portraits_config.txt` | server `data\` | server (enemies, skill clouds, hints, portraits) |
| `BepInEx\ground_assets.txt` | `editor-assets\` | map editor (names of the floor pieces) |
| `ground\*.png`, `units\*.png`, `thumbs.json` | `editor-assets\` | map editor (pictures) |

## Where it writes

| Package | Server data | Editor pictures | Plugin | Log |
|---|---|---|---|---|
| launcher (next to `SteambirdsLauncher.exe`) | `server\data` | `editor-assets` | `plugin\QuieterPlugin.dll` | `logs\game-files.log` |
| server (next to `SteambirdsServer.exe`) | `data` | `editor-assets` (copy it next to the launcher) | `plugin\QuieterPlugin.dll` | `logs\game-files.log` |

The server output while the tool runs it goes to `logs\game-files-server.log`.

## Options

```
--game <folder>           game folder
--server <folder>         server folder
--editor-assets <folder>  where the map editor pictures go
--plugin <file>           QuieterPlugin.dll to install (default: plugin\QuieterPlugin.dll next to the tool)
--copy-only               only copy configBinary and the data files already in BepInEx (does not open the game)
--no-server               open the game without starting the server
--no-pause                never ask or wait for Enter
```

While developing, from `C:\Steambirds` (after `Build plugin.ps1`):

```powershell
dotnet run --project GameFiles -- --server Server --editor-assets Launcher\editor-assets --plugin QuieterPlugin\bin\Release\net46\QuieterPlugin.dll
```

## Code

| File | What it is |
|---|---|
| `Program.cs` | the steps above |
| `Options.cs`, `ToolLayout.cs` | command line and the folders of each package |
| `GameInstallation.cs` | game folder, Steam, build, architecture, `steam_appid.txt` |
| `BepInExSetup.cs` | BepInEx download and the plugin |
| `LocalServer.cs` | starting and stopping the server, ports of `network.json` |
| `ExportRun.cs` | opening the game in export mode and waiting for `export_done.txt` |
| `GameDataCopy.cs` | copying the files to the server and the editor |
| `ConsoleUi.cs` | console and log |

The pictures are drawn by the plugin, inside the game, because only the game knows how to read its own assets
(meshes, materials, the art pools of `PoolManager`). Nothing of the game is stored in this repository.
