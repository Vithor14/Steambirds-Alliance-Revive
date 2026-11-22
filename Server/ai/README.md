# Enemy AI files (`ai\`)

Every enemy AI and every behavior is **its own `.json` file**, so they can be edited by hand, copied, shared and loaded as
needed. The launcher's **Enemy AI** tab edits them and simulates them (with the art and guns of any game unit); the server
reads this folder by itself whenever a file changes (new enemies use the new version).

```
ai\
  behaviors\<id>.json    a BEHAVIOR: one way of acting (how to move, how to shoot, what happens on contact)
  brains\<id>.json       an AI: a state machine whose states act like behaviors and change to each other
```

The **file name is the id** (lowercase letters, digits and `_`). A unit type uses an AI with `"ai": "<id>"`, in
`enemies.json` or in the `types` of a map (map editor: *Units → Enemy AI*). A boss phase may also have `"ai"` to change the
boss's AI when the phase starts. Without `ai` (or when the id does not exist) the type's plain `"behavior"` runs.
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
| `orbit` | goes to a circle around the player and goes around it; far away it approaches in a straight line | `distance`, `deviation`, `speed`, `farSpeed`, `farFactor`, `spin` |
| `approach` | flies at the player, aiming a little ahead of where the plane is going | `speed`, `lead` |
| `straight` | keeps flying where it points (a pass through the player) | `speed` |
| `wander` | random points around where it was born | `distance` (radius), `interval`, `speed` |
| `hold` | does not move; with `face` it turns to look at the player (a turret) | `face` |
| `flee` | flies away from the player | `speed` |
| `home` | goes back to where it was born and circles there | `speed` |

`speed` is a fraction of the unit's top speed (1 = top speed; the unit type's `speedMultiplier` and boss phases apply).
`spin`: 1 clockwise, -1 counter-clockwise, 0 random per unit. `lead` and `interval` are seconds, `distance` is in units.

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
| `chance` | random: `value` is the probability per second |

## Sharing

In the launcher: **Export...** writes one file holding the AI and the behaviors it uses
(`"kind": "steambirds-ai-package"`); **Import...** adds what a shared file holds (a package, or a single brain or behavior
file) without overwriting anything of yours: a file with an id that already exists gets a new id. Or just copy the `.json`
files between `ai\` folders. Deleting in the launcher moves the file to `server\_backup\ai-<date>`.

## What the server does with it

The AI decides where the unit flies, how fast, whether it shoots, with which guns and how fast; the server still does the
rest (turning limits, accelerating, room walls, separation between enemies, gun arcs and bursts, damage, XP, loot). A unit
with an AI ignores `aggroRadius` and `preferredDistance` (the AI says when to fight) and the `behavior` field. See
`docs\enemy-ai.md` for the code map.
