# Enemy AI editor and AI files (22/11/2026; summons, shields and phases 24/11/2026; generic AIs protected and the multi-unit simulator 25/11/2026)

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
  `maps\*.json` and `enemies.json` into `ai/brains/dungeons/<dungeon>/` (`main` = the old flight with the `builtin` move, then
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
Editing does **not** restart the simulation (24/11/2026): the new version takes over and the unit goes on from where it is (`AiSim.ReplaceBrain` → `AiRuntime.Rebind`: it keeps the state it is in, and the time spent there, when the new version still has a state with that id; otherwise it starts from the initial state). An edit that does not compile leaves the last good version running. Only the Restart button, or picking another AI or unit, starts it again; a behavior alone is simulated as a one-state AI.

**Generic AIs are protected (25/11/2026).** An AI in `brains\generic` (and a behavior file one of them uses) is never changed from the editor: `AiStore.IsProtected` is true for it (it exists on the disk), and `Save`, `Rename`, `Move` and `Delete` refuse it. Loading a unit opens its generic AI as it is; when an edit comes in (`AiTab.OnEdited`), the tab queues `DivertToCopy`, which calls `AiStore.DivertEdits`: the edited tree is cloned into a new unsaved AI **"new {unit} AI"** (id `new_<unit>_ai`, `units` = the unit, origin = `AiUnitChoice.AiFolder`: `dungeons/<dungeon>` for dungeon types, `world` otherwise), and the generic file is read from the disk again (`RestoreFromDisk`). The copy is loaded on the unit and the simulation goes on (`Rebind(restart: false)`). *Save as...* (`AiStore.SaveAs`), *Duplicate*, *Export* (`AiStore.Export(forUnit)`), *New AI* and *Import* (`place`) make the same kind of AI: made for the selected unit, in its folder, never generic.

In *Behavior values* (`AiFields.Behavior`) a movement field is greyed out when the selected `move.mode` does not read it (`EnabledWhen = UsedBy(...)`, the "Uses:" list of each mode in `AiCatalog.MoveModes`; an empty mode counts as `orbit`). The `orbitLeader` move (circles the leader of the unit's group, `AiSense.HasLeader/LeaderX/LeaderY`, filled by `Enemies.BrainStep` from `Enemy.Leader`) also runs in the simulator when that unit has a leader (see *Several units in the simulator* below). `AiGunAim.IsAnyDirection` (0.8.0, in progress): a gun the game names `anydir`, in shoot-where-it-points mode with a 360 degree arc, turns between shots and fires all around, in the simulator and on the server. Before that it only fired forward. Reconstruction: the original unit told the mount where to point. `JsonForm.SetEnabled` applies these dependencies again, so a state's own values form does not wake them up when it is switched on.

Unsaved files are kept when the language changes (the tab is rebuilt) and the launcher asks to save before closing.

## Limits

- Units in the original have gun patterns chosen by a state machine we do not have; the AI can pick `guns` per state, which
  is the closest the data allows.
- The simulator has no walls or rooms (the server adds separation and room limits after the AI). It runs the group leader, the units a state summons, and, when the AI names a formation, the glued pieces (chain, slot, ring) with the same placement and links as the server (`FormationMotion`, `FormationRules`).

**Formations (26/11/2026, points and the editor the same day).** `Server\ai\formations\<id>.json` is composition, not flight. A formation may list `points` (x right, y in front of the leader). Place `points` uses `motion` `patrol` (vertex to vertex, `pause` seconds turning by `spin` degrees per second, then the next edge), `fixed`, `toward` or `orbit` (`orbitRadius`). The Five Star is that pentagon. Shield `effect` empty means no bubble; `tankShield` is the player tank and is not the default. The Formation tab edits points, links and summon lines. *Enemy types...* writes one type into `enemies.json` (`deathUnit` included). An AI in `brains\world` that names exactly one unit is what that unit runs when the host did not pick another. Path `points` are edited as `x y` lines. The leader AI has `"formation": "<id>"`. Places: `chain` (behind, along the path, `spacing`), `slot` (x right, y front), `ring` (`count`, `radius`, `spin`, `angle`), `free` (flies with its own AI). Links: `shield` while `whileAlive` / `whileRole` / `whileAnyAlive` (empty `effect` means no damage and no `tankShield` bubble; `effect` names the look), `when.died` + `do: killAll`, `when` role `firstAlive` `healthBelow` 33 + `do.unshield: next`, `do.heal`. `bindToUnit: true` also applies when the AI is empty or `generic_*`. The map, a world site and a dungeon spawn only the leader; `Enemies.SpawnFormationMembers` adds the rest. A unit type's `formation` field, and `world-enemies.json` `formations` (unit to formation id, Enemies of each biome), name a formation outright: that unit is born with every piece, and a world site then skips the random escorts. Empty keeps the AI / `bindToUnit` rule. The Formation tab edits members, links, points and phase summons in tabs (no dialogs); file actions are the File/Edit menus and the list's right-click menu. The preview column is narrow and has speed, leader motion, right-click damage, health drain, heal and respawn. A state's `summons` list (unit, ai, role, place, count, every, max of that line, up to 30) replaces the single `summon` fields when it is not empty. `place` may be `point:<formation>/<point id>` (or `point:<point id>` on the summoner's own formation): the unit is born on that point, x to the right and y in front. The editors pick units from the same folders as the Enemy AI unit list. Shield is its own tab; `shieldEffect` empty means no bubble. `summonsAtMost` takes optional `unit` and `role`. Move modes `path` and `zigzag`, and orbit `around` the summoner, home or leader, live on the move. A type with `deathUnit` leaves a wreck (`Enemies.Wrecks.cs`, `/wreck`); the death look is `ClientInstantEffect` (58535), not a bit in FieldUpdate. F1 on the Formation tab opens `FormationHelp`.

**Ambassador and Inquisitor (26/11/2026).** The shipped Ambassador fight is the AI `ambassador` (`Server\ai\brains\world\`), not `formations\ambassador_body.json`. That formation stays unbound: pointing the AI at it spawns the cats twice. The boss waits, spins, summons three cats on a locked orbit (`around` summoner, `lock`, `faceForward`), flies a triangle (`path` points as `{x,y}` or `[x,y]`, `loop`, `topSpeed` replaces the unit speed, `spinRate` slides the body), then diplomat waves. Diplomats zigzag (`legsDone` counts dashes; `pause` and `jitter` belong to the dash, not to every tick). Under 30% health he returns to the center and summons inner cats, which stay shielded until the outer cats are gone (`siblingsAtMost`), then an open triangle. `inquisitor_chase` is the highmountainhunter that closes in. A world unit whose host did not pick an AI uses the formation bound to the unit, else the only world-folder AI made for that unit (`WorldAiMadeFor`; two AIs for the same unit means the host picks), else `generic_chase`. One BOS group can hold two bosses (Solaris and the Ambassador); a part goes to the boss whose name it shares the longest start with. Parts the boss AI summons itself are not also placed on the spawn ring. Reconstruction: the phases and the numbers come from the community fight, not from original server data.
- Bullets in the simulator are plain (no homing bullets, no gun mount offsets).
- The `builtin` flights are only
  approximated there (circle and chase orbit, charge goes straight at the player, turrets stand still).
- Not done yet: an undo history in the AI editor (use *Revert*), and per-state changes of health or damage.

**Several units in the simulator (25/11/2026).** `AiSim` now holds a list of `AiSimUnit` (`Shared\EnemyAi\AiSimUnit.cs`): the edited unit (`AiSim.Main`, whose members `AiSim` still forwards), the **leader** of its group and the **summoned units**. Each one runs its own AI and guns against the scripted player. The leader exists when the unit is an escort in the open world (`AiUnitChoice.LeaderChoice`: the first leader of a biome group where the unit is a follower, with the AI the host picked for it); the launcher's *Leader* box makes it run its AI, fly to the point of a Ctrl+click (`AiSimControl.Goal`), stand still, or be taken away. A summon is made from the type the state names (`AiSim.SummonResolver`, answered by the tab: a type of the same group, any type, or a game unit; unknown names come as a dummy), stays until destroyed (at most 40) and cannot outlive a restart. A **right click** on any unit takes the percent of the *Right click takes %* box from it (a right drag still moves the view). The `orbitLeader` move uses `AiSense.HasLeader`/`LeaderX`/`LeaderY`, which the simulator fills from `AiSimUnit.Leader`, so *generic_orbit_leader* can be tried there. The AI editor still edits the main unit only: a leader or a summon runs the AI file as it was when it was created (it is not rebound while editing).

The simulator overlay (24/11/2026, `Launcher\EnemyAi\AiSimOverlay.cs`): *Show ways out of the state* draws the transitions of the current state (`AiSim.CurrentState`, with the random time of `timeInState` from `AiRuntime.Rolled`) and a legend; *Show weapon range* draws the reach of `AiSim.ActiveGuns()` (`AiSim.WeaponRange`) and the area where the player must be for a mount to fire (`AiGunAim.FiringHalfWidth`, the same limits as `TryAim`).
