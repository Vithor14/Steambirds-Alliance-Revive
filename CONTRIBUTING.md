# Contributing

Thank you for helping. This project is maintained by the community, so the rules below keep the code readable for
the next person and keep the game's files out of the repository.

## Before you start

1. Read [README.md](README.md) (installation) and [CHECKLIST.md](CHECKLIST.md) (what is done and what is next).
2. Pick a checklist item, or open an issue describing what you want to change. For a large change, talk first.
3. Build the project once (see the README) so you know the tools work on your PC.

## Ground rules

- **Never commit game files**: assets, DLLs, `configBinary`, decompiled code, `Server\data\`, accounts, logs. The
  `.gitignore` covers the usual ones; check `git status` before committing.
- **English everywhere in the code**: identifiers, file and folder names, comments, log messages and README/CONTRIBUTING
  files. Portuguese is fine in the `docs\` design notes and in the launcher's `lang\pt-BR.json`.
- **Data, not code**: maps, enemies, dungeons, loot and settings live in files that the server reads and reloads. Do not
  hardcode content in C# when it can be a file the host or the community can edit.
- **Stay close to the original game.** Anything rebuilt without original data must be marked as a reconstruction (in the
  file that holds it, or in a comment) and, when it makes sense, be configurable.
- **Split big files.** Do not grow a file past about 60 KB. Use `partial` classes split by topic (see
  `Server\Enemies*.cs`) or a new file per topic.

## Code

- C# 12 / .NET 8 for the server and the launcher; .NET Framework 4.6 for the BepInEx plugin (the game runs on it).
- Nullable reference types are on. Do not silence warnings with `!` when a real check is possible.
- Use the existing helpers (`Json`, `TextFiles`, `ConfigBinary`, `Maps`) instead of writing new ones.
- Comments explain *why* (the game's behavior, the protocol rule, the reconstruction), not what the code does.
- Log lines start with a tag in capitals in square brackets, like `[ENEMY]`, `[MAP]`, `[DUNGEON]`. Keep the tags in English.

## Build and test

Build every project you touched, from the project folder:

```powershell
dotnet build Server\SteambirdsServer.csproj -nologo -v q -clp:ErrorsOnly
dotnet build Launcher\SteambirdsLauncher.csproj -nologo -v q -clp:ErrorsOnly
dotnet build QuieterPlugin\QuieterPlugin.csproj -c Release -nologo -v q -clp:ErrorsOnly
```

The build must finish with no errors. Warnings are welcome to fix, but they do not block a change.

Tests in the game are manual. When your change touches gameplay, say in the pull request:

- what you tested (the map, the dungeon, the plane, the chat command);
- what you saw in `server-report.txt` (search for your log tag; do not paste the whole file).

Before you share a report or a log, remove the account data: `server-report.txt` includes the accounts' `account.json`
files, which hold Steam IDs.

## Maps and dungeons

- A map is a `.json` file in `Server\maps\`. The format is in `docs\map-format.md`.
- Edit maps with the launcher's map editor, or by hand, and open them in the editor to check them (the validator lists
  errors and warnings). Say in the pull request when a map was changed by hand.

## Documentation

- Update [CHECKLIST.md](CHECKLIST.md) when the state of an area changes (done, partial, missing). Keep its style.
- Design notes in `docs\` are in Portuguese and are snapshots: when they disagree with the code, the code and the
  checklist are right.
- Update the README when a command, a path or a requirement changes.

## Commits and pull requests

- Small commits with a clear message in English, for example `Enemies: split scripts into a partial class`.
- Describe the change, the reason, what you tested and what is still missing in the pull request.
- Do not mix formatting changes with behavior changes in the same commit.

## Reporting bugs

Open an issue with: the game build and the launcher version, what you did, what you expected, what happened, and the
lines of `server-report.txt` around the problem (without account data). Screenshots help with the map editor and the
launcher.

## Code of conduct

Be kind and specific. The people reviewing this work are volunteers, and so are the people reporting bugs.
