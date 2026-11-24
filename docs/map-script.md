# Map scripts (`"script"` in a map file)

Written on 23/11/2026 (server 0.5.0). Compiled and checked with a simulated host (the whole tutorial script runs from the first rule to the
exit portal); **not played in the game yet**.

A map file holds not only the floor and the points but also **what happens in it**: which units appear, who wakes up when, who says what,
which gates unlock. Before, the walker tutorial's script was C# code (`Tutorial.Script.cs`); now it is the `script` section of
`Server\maps\tutorial_walker.json`, run by a shared engine. Any dungeon map can have a `script` too: the dungeon server runs it next to
its own rooms, waves and bosses.

Code: `Server\Scripting\MapScript.cs` (the file format and the check of every name), `MapScriptRunner.cs` (runner, spawns, jumps),
`MapScriptRunner.Logic.cs` (conditions and actions). The two hosts are `Tutorial.Host.cs` and `Dungeons\DungeonServer.Script.cs`.

## Shape

```json
"script": {
  "speakers": { "instructor": "sergeant", "boss": "bosscat" },
  "stages":   ["hangar", "anteroom drones", "..."],
  "completeWhen": "boss_defeated",
  "spawns": [ ... ],
  "rules":  [ ... ],
  "steps":  { "1": { ... } }
}
```

Everything refers to things of the **same map by name**: `points`, `groups` (of points), `areas`, `lines`, `gates` (by their `name`) and
`checkpoints`. The server checks every name when it reads the file; a typo is an error in the log (`[MAP] ERROR ... script: ...`) and the
previous good version of the map stays, instead of a script that silently stops.

## Spawns

Units that appear. By default they are made when the script starts (`"when": "start"`); `"when": "manual"` waits for a `spawn` action.

| field | meaning |
|---|---|
| `name` | what a `spawn` action calls it |
| `type` | unit type (the map's `types` in a dungeon, `enemies.json` or the tutorial defaults otherwise) |
| `id` | alias of the unit (a single placement): speech, `dead`, `failed`, `guide` and `atUnit` talk about it |
| `group` | group name: `wake`, `quiet`, `remove`, `cleared`, `alive`, `awake` use it. A boss's summons join its group |
| `point` / `points` / `ellipse` | where: one point, every point of a group, or a ring: `{center, count, opening, radiusU, radiusV, startDegrees, faceCenter}` |
| `atUnit` | with `point`: appears where that alias was last seen (the boss wreck); the point is the fallback |
| `area` | area the AI of the unit flies in |
| `asleep`, `wakeRadius` | waits idle until the script wakes the group, a player gets within `wakeRadius` (0 = never) or one of them is hit |
| `invulnerable`, `rot` | optional overrides |

## Rules

`{ "id": "...", "when": <condition>, "do": [ <action>, ... ] }`. A rule fires **once**, the first time its condition is true (no `when` =
on the first tick). Rules are checked in file order every 0.1 s.

### Conditions

Every field that is set must be true (AND). `any` is an OR of conditions, `all` an AND, `not` the opposite.

| field | true when |
|---|---|
| `line` | the furthest live player is at or past `lines[name]` |
| `done`, `afterSeconds` | the rule has fired (with `afterSeconds`: that long ago) |
| `unless` | the rule has NOT fired |
| `gateOpen` | the gate is open |
| `gateNear` `{gate, distance}` | the gate is unlocked and closed and a player is within `distance` tiles of it |
| `cleared` | the group was spawned and nothing of it is alive (a unit that could not be created counts as gone) |
| `alive` / `aliveAtMost` `{group, count}` | some / at most `count` units of the group are alive |
| `dead` / `failed` | the unit with that `id` was created and is gone / could not be created |
| `awake` | somebody in the group is awake (or nobody is waiting) |
| `within` `{point, radius}` | a live player is within `radius` units of the point |
| `roomCleared`, `typeDead` | dungeon maps: a room of `encounters` is cleared / a type of `types` has died |

### Actions

One verb per action. Any action may have `delay` (seconds), `if` (a condition, checked when it runs) and `unless` (a rule that must not have fired).

| verb | what it does |
|---|---|
| `say` (+ `by`, `byGate`, `portrait`, `mode`) | speech key of the client (`Texts/bark`); `by` = alias or group (default `instructor`); `byGate` = the unit of a gate; `mode` `dialog` (default: portrait, bubble and chat line) or `bubble`. The portrait comes from `speakers` |
| `chat` | text in the chat |
| `sound` | plays a sound of the game for the players of the session (`ClientPlayAudio`; a name of the game's audio bank, e.g. `Stinger Mission Objective`; see `docs/admin-tools.md`) |
| `guide` (+ `who`, `seconds`, `until`) | the guide unit (alias `instructor`) goes to a point for `seconds`, or until the rule `until` fires |
| `wake` (+ `stagger`) | wakes a group; with `stagger` one unit every that many seconds |
| `quiet` | the group goes back to waiting |
| `area` | the room the AI flies in from now on |
| `stage` | step number for the log and `/step` (`stages` list) |
| `unlock` / `open` (+ `explode`) | a gate starts taking damage / is opened |
| `spawn` | makes the units of a `manual` spawn |
| `remove` (+ `explode`) | removes a group (explode = it blows up, otherwise it disappears) |
| `portal` (+ `autoExitSeconds`) | the exit carrier to the Nexus at a point; the server takes the player there after the time |
| `log` | a line in the server log |

## Steps (`/step N` in the tutorial)

`"steps": { "3": { "checkpoint": "crate corridor", "done": [...], "quiet": [...], "remove": [...], "open": [...], "fire": [...] } }`: marks the
rules in `done` as fired without running them, quiets and removes groups, opens gates without exploding, runs the rules in `fire`
and takes the plane to the checkpoint.

## In a dungeon

The dungeon server creates the runner when the map has a `script` and starts it with the dungeon. Units made by the script are tracked like the
dungeon's own (`onDeath`, barks, sequences, periodic summons work) but keep the group names of the script, so they **never count to clear a
room**. Gates come from the map's `gates` / `sideGates` (`name`); `unlock` makes a locked gate take damage.

## Not done

- Editor tab for scripts in the launcher (the map editor keeps the `script` section when it saves, but only by hand in the file).
- Actions for the special mechanics (The Dig drill, The Vaults key, Temple of Bast altars, ambushes): they will be new verbs here.
- The tutorial's unit types (`tutorial_*`) are still registered in `Tutorial.Helpers.cs`; `enemies.json` overrides them.
