# Enemy AI editor and AI files (22/11/2026; summons, shields and phases 24/11/2026)

The original enemy AI only ran on the servers (`AI.cs` and `ModalAI.cs` are empty in every client build), so the built-in
behaviors of `Enemies.Ai.cs` are reconstructions. This adds a **data-driven AI**: the community can build, test, share and
load enemy AIs as files, without touching the server code. Reconstruction, not original data: nothing here claims to be
what the original server did.

## Pieces

| Where | What |
|---|---|
| `Shared\EnemyAi\` | the core, **compiled into both the server and the launcher** (`<Compile Include="..\Shared\EnemyAi\*.cs">`): file models, checks, runtime, flight, gun aiming, simulator, packages. The simulator therefore runs the server's own code. |
| `Server\ai\behaviors\*.json`, `Server\ai\brains\{generic,world,dungeons\<name>}\*.json` | the files (format: `Server\ai\README.md`). One file per AI / behavior; the file name is the id (unique across the folders). `generic` = works for any unit; the other AIs list their `units`. |
| `Server\Enemies.Brains.cs` | server side: loads the folder (re-reads when a file changes, every 2 s with `enemies.json`), `AttachBrain`, `BrainThink`, `/brains`. |
| `Launcher\EnemyAi\` | the **Enemy AI** tab: `AiTab` (files, buttons), `AiStore` (the files as editable JSON trees, save/rename/delete/import/export), `AiBrainEditor` / `AiBehaviorEditor` (forms over `JsonForm`), `ListEditor`, `AiFields` (field specs and texts), `AiSimView` (simulator). |
| `Launcher\Editor\EnemyAiHook.cs` | link with the map editor: the `ai` field of a unit type, *Edit AI...* opens the tab on that AI, the save check warns about ids that do not exist. |

## Shared core (`Shared\EnemyAi`)

- `AiModels.cs`: `AiBehaviorFile`, `AiBrainFile`, `AiStateFile`, `AiTransition`, `AiCondition`, `AiMove`, `AiFire`,
  `AiContact` (System.Text.Json, camelCase, comments and trailing commas allowed, nulls not written). A state also has the
  summon, shield, look and homing fields (24/11: `summon*`, `shield*`, `effect`, `homing`) and the editor-only `graphX` / `graphY`.
- `AiCatalog.cs`: the list of move modes and condition types with their help texts (the editor builds its lists from it).
- `AiLibrary.cs`: `Load(folder)` (never throws: a bad file becomes an `AiProblem`), `Compile` (resolves behaviors, checks
  ids, transitions, unknown modes, unreachable states) and `Signature(folder)` (change detection).
- `AiRuntime.cs`: `AiRuntime.Step(AiSense)` → `AiOutput` (destination, speed, `Hold`, fire flags, `Explode`, state change,
  `EnteredStates`, `UseBuiltin`, `ShieldOn`, `Summons`). One runtime per unit; it keeps only the current state, its enter time,
  rolled random times, the wander point, the shield end and the next summon time. Transitions that chain in one tick (a hit
  that crosses two health limits) put every state entered in `EnteredStates` and every summon in `Summons`.
- `AiMath.cs`: `AiMotion.Steer` (turn limit, acceleration, slowing on sharp turns: the server's flight code, now shared),
  `AiGunAim.TryAim` (FireMode patterns: aimed, arcs, hose, sprinkler...) and `AiGunAim.Range`.
- `AiSim.cs`: a headless arena (enemy + scripted player + bullets). Guns repeat `Enemies.Fire` (burst, reload, arcs).
- `AiPackage.cs`: one-file sharing (`steambirds-ai-package`) of a brain with the behaviors it uses.

## How the server uses it

- `EnemyType.Ai` (enemies.json and map `types`). A non-text `ai` (the old placeholder slot of the map editor) is ignored by
  `AiReferenceConverter`, so old maps still load. (`BossPhase` and `phases` were removed on 24/11, see *Summons, shield, look and phases*.)
- `CreateEnemy` calls `AttachBrain(EffectiveAi(type))`: a type with no `ai` gets `generic_<behavior>`, so every unit runs an AI; an unknown id is logged once and the built-in `behavior` runs. `TypeFlight(type)` is the flight the first state of the AI names (it tells idle, fixed, mine, kamikaze and route units apart); a `builtin` state keeps the old rules of its flight (idle and fixed do not move or turn, fixed fires straight, kamikaze and mine explode on contact).
- `Think`: with a brain, `BrainStep` (`Enemies.Brains.cs`) runs the state machine and does what the states ask; `mode = "brain"`
  takes its destination and speed, and the common code (enemy separation, room area / arena limits, `AiMotion.Steer`) still
  runs after it. `Hold` makes it a turret (no slowing on turns). A state with the `builtin` move hands the flight to the old
  switch of `Think` (`FlightMode`: chase, circle, charge, turret, route...), with `speedMult` on the unit's speed and the unit
  type's `aggroRadius` deciding the wake-up, exactly as without an AI.
- `Fire`: the state's `fire` overrides enabled / guns (`GunAllowed`) / rate / range / aim lead. `aggroRadius` is ignored:
  `Aggro = a live player exists`; the state machine decides when to fight.
- A state with `contact.explodeRadius` or `selfDestruct` makes the enemy explode like the old `kamikaze` (no XP, no loot).
- `Enemy.LastDamagedAt` (set in `TakeDamage`) feeds the `recentlyHit` condition.

## Summons, shield, look and phases (24/11/2026)

They are fields of a **state** (`Server\ai\README.md` has the table), done by `BrainStep`:

- **Summons:** `SummonUnits` creates `summonCount` units of `summon` behind the unit (spread 40 degrees, 5 units away, kept in its
  room), in its group, with `Enemy.Summoner` set. `summonEvery` repeats it while the state lasts and `summonMax` limits how many
  of its own are alive; the `summonsAtMost` condition counts them (`AiSense.SummonsAlive`, only computed when a state summons or
  a condition asks: `AiBrain.NeedsSummonCount`). In a dungeon map the type is looked up as `<map>/<name>` first, then as `<name>`.
- **Shield:** while `ShieldOn` the server renews `Enemy.ShieldUntil` every tick (`TakeDamage` ignores damage until then) and shows
  `shieldEffect` (or `tankShield`) in EFFECTSTATE1; when the state stops asking, `UpdateInstance` puts the normal look back.
- **Look:** `effect` sets `Enemy.BaseEffect` from this state on. **Homing:** `homing` sets `Enemy.Homing`.
- **Phases:** a phase is a state that a `healthBelow` transition leads to; nothing else is special. The old `phases` of the unit
  types (`BossPhase`, `ApplyPhases`, `barks.phase` of the dungeon server) are gone: a one-off script converted the 24 bosses of
  `maps\*.json` and `enemies.json` into `ai\brains\<map>_<type>.json` (`main` = the old flight with the `builtin` move, then
  `phase1`, `phase2`... with the same summons, messages, speech, shield, effect, guns and fire rate). Only `periodicSummon`
  (dungeon rooms, with `countsForRoomClear`) stays as a unit type field.
- **Limit:** the AI files are shared by id (`ai\` is one folder for every map), so a community dungeon that uses a boss AI must
  ship the brain file with it (the launcher's *Export...* packs a brain with its behaviors).

## The launcher tab

The editor only edits **the AI of the unit in the simulator** (23/11/2026). The middle area has two views of the same AI
(24/11): the tab **States and transitions** (the list, with the State / Behavior values / Summons and shield / Transitions tabs; it
opens by default) and the tab **Graph** (`AiGraphView.cs`): a box per state and an arrow per transition with its conditions
(`health ≤ 50%`, `after 3s`...). Clicking a state selects it in the list and a box under the graph says what it takes to go from
it to every other state (the direct transition, or the shortest chain of states, or that there is no way) and what it takes to
arrive at it from each of the others. States can be dragged (`graphX` / `graphY` are saved in the file), *Arrange automatically*
lays them out in columns by steps from the first state (a long chain wraps into bands of four) and *Fit view* shows them all. The unit bar (`AiTab`, `AiUnits.cs`) lists the enemy
types of `enemies.json` (game unit of the `template`, `ai`, speed / turn / fire-rate multipliers, aim lead, free aim; the
`auto:` templates use a copy of the server's pick, which can differ for island missions) and then the plain game units. A type
shows its own `ai` file in the editor; a plain unit, or a type with only a built-in `behavior`, has none. Any other AI or
behavior is chosen in the file list and put on the unit with *Load on the unit* (double-click), edited and saved there, and
*Back to the unit's AI* returns the unit to its own AI (the loaded file is dropped when another unit is chosen; unsaved
files stay marked in the list). The banner above the editor and the simulator's HUD say which file runs and whether it is the unit's
own or only loaded to be tried. The file buttons (Duplicate, Rename, Delete, Export) act on the file selected in the list;
Save / Revert act on the file in the editor. Renaming or deleting an AI that a type of `enemies.json` uses warns first.

The name, author, description and first state of an AI (and the same for a behavior) are on their own **Info** tab, so the states and values get the whole height (24/11/2026).

The actions are in a menu bar like the map editor's (23/11/2026): *File* (New AI / New behavior, Save, Save all, Revert, Import, Export,
Open folder, Reload), *Edit* (Duplicate, Rename, Delete) and *Help* (F1, `AiHelp.cs`: a topic per area; the window is `UI\HelpWindow.cs`).
The unit bar of the tab (24/11/2026): the *Unit* button opens `AiUnitPicker` (a tree built by `AiUnitOrigins`: Dungeons > dungeon, World > biome, Generic, Without origin; a unit in several places is listed in each, with the AI of that place; no AI file = another color), the *AI* box lists `AiStore.ApplicableBrains(unit)` (made for the unit first, then generic) and *Load another AI...* loads any AI or a file of the computer (`AiStore.Import(contents, save: false)`). In an AI, *New behavior...* (`AiBrainEditor.NewBehavior`) opens `AiBehaviorWindow`; the result is an embedded behavior (`"behaviors"` of the AI file) or a file of `behaviors\`.

The map editor's *Load unit in the AI editor* button (`AiTab.OpenType`) adds the unit type of the map to the unit list as `map type: ...`
(`AiUnits.FromType`, the same reading as the `enemies.json` types) and opens its AI. Left: the files (AIs and behaviors). Middle: the editor (a brain: info, states, State / Behavior values / Transitions tabs with conditions;
a behavior: info and movement / weapons / contact) and the list of problems of the file (same checks as the server). Right:
the simulator.

Simulator: pick any game unit (`units_config.txt`: art picture from `editor-assets\units`, speed, turn, guns); the player
plane follows clicks, circles or stands still; the speed, turn and fire-rate multipliers, aim lead and free aim of the unit
type can be tried; "Health lost %/s" and *Hit* test health conditions; "Player in sight" off tests the no-target case.
It draws the trail, the destination the AI is flying to (dashed line and cross), bullets, the state and the state log.
The "Weapons (mounts)" field (`fire.guns`) is a checklist made from the guns of the unit in the simulator
(`FieldKind.GunMounts`, `Editor\GunMountList.cs`; the guns come from `AiUnits.CurrentGuns`): checked = shoots in that state,
all checked = the field is removed (every mount of the unit). At least one mount stays checked (turn off *Shoots* for none).
Each mount is an expandable section (several can be open at once): clicking its name opens its data under it: pattern, mount angle and arc, position on the art, fire rate / burst / reload,
bullets, damage, speed, lifetime, reach, bullet steering and the shot type (gun type name). The shot type is the last column
of the `gun` lines of `units_config.txt` (plugin 1.22.0 with this change): rebuild the plugin and export the game files again
to get it. Every gun of the unit has a color (HUD list, dimmed when the current state does not let it fire) and its bullets are drawn in
that color, so it is clear which gun fired what (`AiSimBullet.Gun`).
Editing restarts the simulation with the new AI; a behavior alone is simulated as a one-state AI.

Unsaved files are kept when the language changes (the tab is rebuilt) and the launcher asks to save before closing.

## Limits

- Units in the original have gun patterns chosen by a state machine we do not have; the AI can pick `guns` per state, which
  is the closest the data allows.
- The simulator has no walls, rooms or other enemies (the server adds separation and room limits after the AI).
- Bullets in the simulator are plain (no homing bullets, no gun mount offsets).
- Summoned units are only counted in the simulator (they are not flown and fall after 12 s), and the `builtin` flights are only
  approximated there (circle and chase orbit, charge goes straight at the player, turrets stand still).
- Not done yet: an undo history in the AI editor (use *Revert*), and per-state changes of health or damage.

The simulator overlay (24/11/2026, `Launcher\EnemyAi\AiSimOverlay.cs`): *Show ways out of the state* draws the transitions of the current state (`AiSim.CurrentState`, with the random time of `timeInState` from `AiRuntime.Rolled`) and a legend; *Show weapon range* draws the reach of `AiSim.ActiveGuns()` (`AiSim.WeaponRange`) and the area where the player must be for a mount to fire (`AiGunAim.FiringHalfWidth`, the same limits as `TryAim`).
