# Map editor (`Launcher\Editor`)

The **Map editor** tab of the launcher creates, opens, edits and saves the map files the server uses (`server\maps\*.json`, format described in `docs\map-format.md`). It draws the map with the game's pieces, so what you see is what the server will build.

## Using it

- **File → New map...** starts from a dungeon of the game (a copy of its map, the original file is not touched) or from an **empty dungeon**. **Open...** loads any `.json` map; **Open a map of the server** lists `server\mapas`. **Save** / **Save as...** write the file (the file name becomes the map id: lowercase letters, digits and `_`). `Ctrl+N`, `Ctrl+O`, `Ctrl+S`.
- **Left side**: the tools. *Terrain* (Select / move, Floor, Walls), *Dungeon* (Entrance, Exit, Checkpoint, Gate, Enemy) and *Markers* (Point, Group point, Area, Line, Route, Sign), and the options of the chosen tool: only the options that tool uses are shown (enemy type and room for Enemy, gate type for Gate, group name for Group point, rectangle for Floor and Walls, walls by tile for Walls, snap for the tools that place or move points). Hover an option to see what it changes.
- **Middle**: the map. Left button uses the tool, middle button (or right button with Select, or `Space`+drag) moves the view, the wheel zooms. `Delete` removes, `Q`/`E` rotate (enemies, decoration, points and the entrance turn on the map by their `rot`; floor pieces of `floor.pieces` are drawn with their rotation, counter-clockwise relative to the tile) (`Shift` = 45°), arrows nudge, `Ctrl+D` duplicates, `Ctrl+Z`/`Ctrl+Y` undo and redo, `F` fits the map, `Esc` goes back to Select.
- **Right side**: one tab per kind of data — *Map* (name, terrain, territory, entrance, exit, floor), *Rooms* (rooms, their enemies and waves), *Units* (unit types: combat, death and summons, phases), *Gates*, *Points* (points, groups, areas, lines, values, routes, checkpoints, signs, decoration, buildings, floor pieces). The AI of an enemy is chosen in *Units → Enemy AI* (see below). When an object is selected on the map, only the tabs that have its fields stay (an enemy: Rooms and Units; a gate: Gates; a point, area, line, route, checkpoint, sign or decoration: Points; the entrance: Map) and the first one is selected by itself; click an empty spot of the map to bring every tab back. Hover a field or its name for a tip about what it changes.
- **View → Game pictures** switches between the pictures of the game and plain shapes. **Tools → Edit the file text...** shows the JSON as it will be saved.

## Saving

Saving checks the map first (**Tools → Check map** shows the same list):

- **Errors** (the dungeon would not work): no floor or floor pieces, floor outside the game grid, no entrance, no exit point, nothing that ends the dungeon, rooms without name or area, groups or gates pointing to a unit type, room or area that does not exist, bad conditions (`cleared:`, `death:`, `quest:`), empty terrain.
- **Warnings** (it works, but look at it): entrance or exit off the floor, rooms without units, gates that never open, no checkpoints, a unit outside the area of its room.

A map with errors is not saved as a normal map. The dialog offers **Save as draft anyway**, which writes `"draft": true` in the file so you can keep working. The mark is only for the editor (the server does not read it), so a draft may not work on the server until the errors are fixed. Before the first save of a session the old file is copied to `server\_backup\maps-<date>`.

## Game pictures

The tab reads `editor-assets\` next to the launcher: `ground\<id>.png` (floor pieces), `units\<name>.png` and `thumbs.json`, plus `ground_assets.txt` (names of the floor pieces). They are drawn by `SteambirdsGameFiles.exe` from your own copy of the game, and `Build launcher.ps1` copies them to `dist\editor-assets`. Without the folder the editor still works and draws shapes and flat colors. The pictures are *previews* (shape and vertex colors of the meshes, not the textures); units without a picture are drawn as a letter.

## The enemy AI of a unit type

A unit type may name an AI with `"ai": "<id>"`: the id (file name) of a file of `server\ai\brains`, made in the launcher's **Enemy AI** tab (format: `server\ai\README.md`). The AI is **not stored in the map**: it is its own file, so the same AI serves many maps and can be shared. *Units → Enemy AI* has the field (with the list of AIs) and *Edit AI...* (also *Tools → Enemy AI editor...*), which opens the Enemy AI tab on that AI. Empty = the plain *Behavior* of the *Combat* tab.

- The editor keeps the `ai` field when it opens and saves a map, undo/redo and renames included. An old `"ai"` that is not text is ignored by the server.
- *Check map* warns when a unit type names an AI that is not in the `ai` folder (the server then uses the type's normal behavior).
- Code: `EnemyAiHook.cs` (`IEnemyAiEditor`, implemented by `EnemyAi\AiEditorBridge`), the AI page of `UnitTypesPage.cs`, the check in `MapValidator.cs`.

## Code

| File | What it is |
|---|---|
| `MapEditorTab.cs` | the tab: menu, toolbar, pages, status bar, open/save/new, undo and redo, closing |
| `MapDocument.cs` | the map as a JSON tree (`MapDocument`) and its session: undo/redo snapshots, dirty flag, file |
| `MapCanvas.cs`, `MapCanvas.Paint.cs` | the view: tools, mouse and keyboard, drawing |
| `MapObjects.cs` | the things on the map that can be selected, moved and deleted (one class per kind) |
| `FloorGrid.cs`, `MapCoords.cs` | the floor drawing and the units (tile 12 u, slot 6 u, sub-cell 3 u) |
| `MapValidator.cs`, `MapFactory.cs`, `MapTransforms.cs`, `MapRefactor.cs`, `Territories.cs` | checks, new maps, moving/centering, renaming with references, terrains and territories of the client |
| `EditorAssets.cs` | the pictures and name lists of `editor-assets` |
| `JsonForm.cs`, `CollectionPanel.cs`, `ItemSources.cs`, `EditorContext.cs` | table-driven forms: a page lists its fields (`FieldSpec`) and the form edits the JSON directly |
| `MapSettingsPage.cs`, `RoomsPage.cs`, `UnitTypesPage.cs`, `GatesPage.cs`, `PointsPage.cs` | the tabs on the right |
| `EditorDialogs.cs`, `EditorTheme.cs`, `Json.cs`, `EnemyAiHook.cs` | dialogs, light/dark theme of the menu and buttons, JSON helpers, the AI slot |

To support a new field of the map format, add a `FieldSpec` to the page that owns it (the key is the name used in the file; dotted keys edit nested objects), and a check to `MapValidator.cs` when the server needs it. Every text goes through `L.T`/`L.F` (or a `FieldSpec` label/help, translated by `JsonForm`), and its translation goes into `Launcher\lang`.
