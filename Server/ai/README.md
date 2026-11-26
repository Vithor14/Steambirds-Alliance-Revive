# Enemy AI files (`ai\`)

Every enemy AI and every behavior is **its own `.json` file**, so they can be edited by hand, copied, shared and loaded as
needed. The launcher's **Enemy AI** tab edits them and simulates them (with the art and guns of any game unit); the server
reads this folder by itself whenever a file changes (new enemies use the new version).

```
ai\
  behaviors\<id>.json                 a shared BEHAVIOR: one way of acting (how to move, how to shoot, what happens on contact)
  brains\
    generic\<id>.json                 AIs that work for any unit (the generic_* ones and other all-round AIs)
    world\<id>.json                   AIs made for units of the open world
    dungeons\<dungeon>\<id>.json      AIs made for units of one dungeon (arena, ice, ... and tutorial: the tutorial is a dungeon)
```

An **AI** is a state machine whose states act like behaviors and change to each other. Its **origin** is the folder it lives in. The server and
the launcher read every folder inside `brains\` (and `behaviors\`), so a file can be moved between them; the id (the file name) must be
unique across all of them (a second file with the same id is ignored and reported).

- **`units`** (in the AI file): the game units the AI was made for (the `template` of a unit type, e.g. `"units": ["swarmboss"]`). The editors
  offer an AI only to those units. An AI in `brains\generic` works for any unit and its `units` list is ignored.
- **Behaviors** live in two places: as files in `behaviors\` (any AI can pick them), or **embedded** in the AI file, in a
  `"behaviors": [ { "id": "...", "move": { ... } } ]` list: only that AI's states can pick an embedded behavior (it wins over a file with the same id). The launcher names an embedded behavior `<ai id>_<name>` (the prefix follows when the AI is renamed and goes away when the behavior is saved in `behaviors\`), and the library reports an error when an embedded behavior has the id of a behavior file.

The **file name is the id** (lowercase letters, digits and `_`); an `"ai"` written as a path such as `"generic/generic_chase"` also works, only the file name counts. A unit type uses an AI with `"ai": "<id>"`, in
`enemies.json` or in the `types` of a map (map editor: *Units → Enemy AI*). **Every unit runs an AI:** a type without `ai`
(an old or community map) gets `generic_<behavior>` of its old `"behavior"` field (`generic_chase` when it has none). The maps and
`enemies.json` shipped here already name their AI and no longer have the `behavior` field. The `generic_*` AIs (`generic_chase`, `generic_circle`, `generic_charge`, `generic_turret`, `generic_patrol`, `generic_escort`, `generic_idle`,
`generic_fixed`, `generic_kamikaze`, `generic_mine`, `generic_route`) are the old built-in flights as one-state AI files (`builtin` move), one per
old behavior, so no unit lost its movement. They still use the type's `aggroRadius`, `preferredDistance` and, for bombs and mines, `contactRadius`.
In a `builtin` move an empty `builtin` name means the type's old `behavior`; the shipped boss AIs name their flight themselves. A boss with **phases** is an AI with a state per phase (see *Summons, shield, look and phases*).
`/brains` in the game chat lists what the server loaded; problems of a file go to the server log (`[ENEMY] ai: ...`).

Files accept `//` comments and trailing commas. Unknown fields are ignored (and kept by the launcher when it saves).

## Behavior

```json
{
  "format": 1,
  "id": "orbit_close",
  "name": "Orbit close",
  "author": "someone",
  "description": "Flies to a circle around the player and keeps going around it, shooting.",
  "move": { "mode": "orbit", "distance": 12, "deviation": 30, "speed": 0.7, "farSpeed": 1.0, "farFactor": 1.6 },
  "fire": { "enabled": true },
  "contact": { "explodeRadius": 0 }
}
```

### `move`

| mode | what the unit does | fields it uses |
|---|---|---|
| `orbit` | goes to a circle around the player (or its summoner, or its home: `around`) and goes around it; far away it approaches in a straight line | `around`, `distance`, `deviation`, `speed`, `farSpeed`, `farFactor`, `spin` |
| `path` | flies through `points` in order (offsets from where it was born, `{ "x": 0, "y": 12 }`); with `loop` (default) it starts again from the first, without it stops at the last point facing the player | `points`, `loop`, `speed` |
| `zigzag` | straight dashes of `distance` units toward the player, `deviation` degrees (plus or minus a random `jitter`) to one side of the line and then to the other, facing each dash (it slides); with `pause` it stops between dashes and turns to face the player first (not before the first dash after it is born); within 1.5 dashes of the player it weaves around them instead | `distance`, `deviation`, `jitter`, `pause`, `speed`, `turnMult` |
| `approach` | flies at the player, aiming a little ahead of where the plane is going | `speed`, `lead` |
| `straight` | keeps flying where it points (a pass through the player) | `speed` |
| `wander` | random points around where it was born | `distance` (radius), `interval`, `speed` |
| `hold` | does not move; with `face` it turns to look at the player (a turret) | `face` |
| `flee` | flies away from the player | `speed` |
| `home` | goes back to where it was born and circles there | `speed` |
| `builtin` | the server's own flight (`chase`, `circle`, `charge`, `turret`, `route`, `kamikaze`, `patrol`, `escort`, `fixed`, `idle`) runs the unit | `builtin` (empty = the unit type's `behavior`), `speedMult` |

`builtin` keeps the old flight of a unit (a boss that circles, charges or flies its route) while the states change everything
else. With it, the unit type's `aggroRadius` still decides when it wakes (as without an AI), and `speedMult` multiplies the
unit's own speed (1.2 = a fifth faster). The launcher's simulator only approximates these flights.

`speed` is a fraction of the unit's top speed (1 = top speed; the unit type's `speedMultiplier` applies).
`spin`: 1 clockwise, -1 counter-clockwise, 0 random per unit. `lead` and `interval` are seconds, `distance` is in units.
`turnMult` (every mode, default 1) multiplies the unit's turn rate while the state lasts (2 = twice as fast as the unit type turns):
a unit that turns faster makes tighter turns and comes around sooner when it overshoots.
`topSpeed` (every mode, units per second, default 0 = the unit's own) replaces the unit's top speed while the state lasts: for units the
game made immobile (top speed 0 in the game data, like the Ambassador `ufo`, whose flight lived on the original server).
`around` (orbit): `player` (default), `summoner` (the unit that summoned this one; its home when it was not summoned or the summoner
died) or `home`. With `summoner` a minion circles its boss, like a ring of guards.
`lock` (orbit around `summoner` or `home`): the unit keeps its place on the circle and its facing relative to the summoner's body, so the
ring turns exactly with a spinning boss, the same way it turns, and changes direction when the boss does; it slides to its place and
enemy separation does not push it. `faceForward` (with `lock`): it faces the way it travels around the circle, the way the summoner turns (flipping when the summoner turns the other way), instead of `faceOffset`. `faceOffset` (with `lock`, default 90): its facing in degrees from the line summoner → unit (90 = along
the circle clockwise, -90 = the other way, with its left side to the summoner, 0 = out, 180 = in): it decides where its fixed guns point.
`spinRate` (every mode, degrees per second, default 0): the body turns by itself (positive = clockwise, negative = counter-clockwise)
and the unit slides to its destination without turning its nose toward it, so it never turns to face the player (the Ambassador).
The client is sent the velocity along the way it really moves.

### `fire`

`enabled` (shoots or not), `guns` (gun mounts that shoot, empty = the unit type's), `rateMult` (fire rate multiplier),
`range` (0 = the gun's own range), `aimLead` (empty = the unit type's value).

### `contact`

`explodeRadius`: the unit blows up (no XP, no loot) when a player gets this close. `0` = never.

## AI (brain)

```json
{
  "format": 1,
  "id": "chaser",
  "name": "Chaser",
  "initial": "patrol",
  "states": [
    { "id": "patrol", "behavior": "patrol_home",
      "transitions": [ { "to": "chase", "when": [ { "type": "playerWithin", "value": 60 } ] } ] },
    { "id": "chase", "behavior": "orbit_close",
      "transitions": [ { "to": "patrol", "when": [ { "type": "hasTarget", "not": true } ] } ] }
  ]
}
```

A **state** either names a behavior file (`"behavior": "orbit_close"`) or carries its own `move` / `fire` / `contact`
(`berserker.json` shows both). It may also have `selfDestruct` (the unit explodes when it enters the state: a fuse),
`message` (chat text) and `bark` (a speech key of the client).

### Summons, shield, look and phases

These belong to the state (not to a behavior file). They happen when the state starts, and the shield and the periodic summons
go on while it lasts.

| field | what it does |
|---|---|
| `summon` | unit type called when the state starts: a type of `enemies.json` or of the map the unit is in (a dungeon map's own `<map>/<type>` is tried first) |
| `summonCount` | units each time, 1 to 10 (default 1), placed behind the summoner; they join its group and room |
| `summonEvery` | seconds until it summons again while the state lasts (0 = only when the state starts; at least 0.5) |
| `summonMax` | no new summons while this many of its own summons are alive (0 = no limit) |
| `summonPlace` | `behind` (default: side by side behind the summoner) or `around` (evenly spaced on a circle around it, facing along the circle) |
| `summonDistance` | how far from the summoner they appear (0 = 5 units) |
| `summonAngle` | with `around`: degrees the circle is turned (half the spacing, 60 for 3 units, puts a second ring between the units of the first) |
| `shield`, `shieldSeconds` | the unit ignores all damage; for `shieldSeconds`, or the whole state when it is 0 |
| `shieldEffect` | client effect (`statefulEffects`) shown on top of the pink shielded glow while the shield is up (empty = only the glow; the glow is the game's `Entity.FLAG_SHIELDED`, always on during a shield) |
| `effect` | client effect the unit shows from this state on (empty = keeps what it has) |
| `homing` | `"on"` / `"off"`: homing bullets from this state on (empty = keeps what the unit has) |
| `graphX`, `graphY` | where the launcher's graph view draws the state (the server ignores them) |

A **phase** is just a state that a `healthBelow` transition leads to. Phases crossed at once (a big hit) all happen, in order.
`dungeon_ice_iceboss.json` (a chain of five phases that summon), `dungeon_sneezy_sneezy.json` (a shield and other guns) and
`tutorial_boss.json` are examples; the 24 bosses of the shipped maps use this (the old `phases` of a unit type are not read
any more). Fire rate and guns of a phase are the state's `fire.rateMult` / `fire.guns`; its speed is `move.speed` (or
`move.speedMult` for `builtin`); its message and speech are `message` / `bark`.

`transitions` are checked **in order, every tick**; the first one whose `when` conditions are **all** true changes the state
(for "or", write two transitions; no `when` = always). A transition with `"not": true` on a condition means the opposite.

| condition `type` | true when |
|---|---|
| `always` | always |
| `timeInState` | `value` seconds passed in the state (with `max`, a random time between `value` and `max`, rolled each time the state starts) |
| `playerWithin` / `playerBeyond` | a player is closer / farther than `value` units (false when there is no player) |
| `hasTarget` | a live player exists |
| `healthBelow` / `healthAbove` | health fraction is at or under / over `value` (0–1) |
| `recentlyHit` | the unit took damage in the last `value` seconds |
| `facingTarget` | the player is inside `value` degrees in front of the unit |
| `summonsAtMost` | the units this unit summoned that are still alive number at most `value` (0 = all dead) |
| `siblingsAtMost` | for a summoned unit: the other live units its summoner summoned number at most `value`; with `"unit": "<type>"` only those summoned as that type count (true when the unit was not summoned). The Ambassador's inner ring stays shielded until the outer ring is dead with it |
| `legsDone` | the unit finished at least `value` zigzag dashes (or reached that many points of a path) in this state (the Ambassador's diplomats drop their shield after 2 and shoot after 3) |
| `chance` | random: `value` is the probability per second |

## Sharing

In the launcher: **Export...** writes one file holding the AI and the behaviors it uses
(`"kind": "steambirds-ai-package"`); **Import...** adds what a shared file holds (a package, or a single brain or behavior
file) without overwriting anything of yours: a file with an id that already exists gets a new id. Or just copy the `.json`
files between `ai\` folders. Deleting in the launcher moves the file to `server\_backup\ai-<date>`.

## What the server does with it

The AI decides where the unit flies, how fast, whether it shoots, with which guns and how fast; the server still does the
rest (turning limits, accelerating, room walls, separation between enemies, gun arcs and bursts, damage, XP, loot). A unit
with an AI ignores `aggroRadius` and `preferredDistance` (the AI says when to fight) and the `behavior` field, except in a
state that uses the `builtin` move. See
`docs\enemy-ai.md` for the code map.
