# AIs of the open world

Put here the AI files made for units of the open world (`Server\world.json`). The open world gives every unit the generic chase
AI (`generic\generic_chase.json`) until a file here, or an AI chosen for it, replaces it. See `Server\ai\README.md`.

A **world boss** the host gave no AI (in the launcher's *Server > World map > Enemies*) runs the AI of this folder whose `units`
lists it, and the open world does not create the parts that AI summons by itself. When the game has a `<boss>DEATH` unit, the boss
leaves it where it dies (its death animation, `deathUnit`).

| File | Unit | What it does |
|---|---|---|
| `inquisitor_chase.json` | `highmountainhunter` (Inquisitor) | wanders, locks on to a player and chases with tight turns (pick it in the Enemies tab) |
| `ambassador.json` | `ufo` (Ambassador, world boss of the Imperial Palace) | the boss fight below |
| `ambassador_cat.json`, `ambassador_cat_inner.json`, `ambassador_cat_outer.json` | `ufosaucer` / `ufosaucermini` (Cat Representatives) | fixed to the Ambassador's body, turning with it (`orbit` around `summoner` with `lock`) |
| `ambassador_diplomat.json` | `ufohopper` (Diplomats) | short zigzag toward the nearest player (shielded for 2 dashes), then a still turret; wanders when the player is over 8 units away |

## Ambassador

A reconstruction from a video of the original game (the original AI was on the server and is lost). The unit types it summons are
in `Server\enemies.json` (`ambassador_cat`, `ambassador_cat_inner`, `ambassador_cat_outer`, `ambassador_diplomat`); `/enemy ai_ambassador`
creates one anywhere to test it.

It never turns to face the player: its body turns slowly by itself (`spinRate`, 20 degrees a second), counter-clockwise in phase 1 and the
other way at each phase change (triangle clockwise, diplomats counter-clockwise, rings counter-clockwise, last stand clockwise). It slides
along its path while it turns. The cats are fixed to its body (`lock`) and face the way they circle (`faceForward`), so they circle it the
same way it turns.

1. **wait**: shielded, still, until a player comes within 40 units or shoots it.
2. **intro** (counter-clockwise): shielded; 3 cat representatives (`ufosaucer`) appear evenly spaced 4.5 units from its center, close around it,
   shielded for their first 5.5 s (they fire their laser, fast line and spinner, not the big slow circles). When the 3 are dead:
3. **triangle** (clockwise): no shield; slides along a small triangle (points 3 units from its spawn) at 3 units/s (the game gives the
   `ufo` a top speed of 0, so the state sets `topSpeed`). When it has lost **a quarter of its health** (or after 13–15 s, if sooner):
4. **diplomats** (counter-clockwise): shielded; 5 diplomats now and 5 more each second (15 in 3 waves). When all are dead it goes back to the
   triangle. The 75% call is forced (`diplomats_forced` → `triangle_late`), so a burst of damage cannot skip it; a diplomats phase
   called by the timer first goes back to the first `triangle`, which still forces it at 75%. Later triangles call diplomats every 13–15 s.
   Each **diplomat** (`ambassador_diplomat.json`) darts toward the nearest player in a short zigzag: 3 fast dashes (8 units/s) of 1.6 units,
   facing each dash; the first two a random 28 to 52 degrees off the line (`jitter`), the third 40. The first leaves at once; before
   each of the others it stops and turns to face the player (`pause`, 0.2 s). Shielded for its first 2 dashes. Then it stays still as a
   turret, facing the player and shooting; with no player within 9 units it wanders slowly, and turns turret again when one comes back
   within 9.
5. Under **30% health** (from the later triangle) → **final_return** (counter-clockwise): shielded, back to its spawn point; then 3 cats on an
   inner ring (4.5 units, the pink `ufosaucer`, shielded until the outer ring is dead: `siblingsAtMost 0` with `unit: ambassador_cat_outer`)
   and 3 on an outer ring (8 units, the orange `ufosaucermini`, turned 60 degrees by `summonAngle`, so each outer cat sits between two
   inner ones). Turning counter-clockwise, the inner cats' left side is toward the boss, so
   their side guns (the big slow circles) fire through it: as it turns, the bullets draw a spiral.
6. When all 6 are dead → **final_fight** (clockwise, 30 degrees a second): no shield; desperate, it slides along a bigger triangle (4.5 units
   from the spawn) at 4.5 units/s, until it dies. The `ufo` itself dies with no explosion (`planeExplosionEmpty`): it leaves its death
   model `ufoDEATH` for 6 s, with the game's boss death, a sequence of the client's instant effects (`ClientInstantEffect`): at once
   `explosionBossInitial` (the first big explosion), `explosionBossDebris` and `deathAnimation` (the model's animator trigger `death`: it
   shakes and throws its beams of white light), then the small and medium explosions with their sounds `epicDeath1`, `epicDeath2`,
   `epicDeath4` and `epicDeath3` over the 6 s. Then the server destroys the model, which plays the big final explosion (`ExplosionB_Final`,
   the `explosionPrefab` of `ufoDEATH`). The original timing is lost; `deathSequence` in `enemies.json` changes it.

**Guns of the boss.** 1 = a fast bullet spiral (6 a second), 2 to 5 = rings of 8 to 16 bullets every 3 to 4 s (0 is a dud). Their ranges
in the game data are short (3 to 9 units), so every state that shoots sets `fire.range` 25. To leave the players room: while summons
are alive (cats or diplomats) only the ring of gun 2, once every 4 s; open on the triangle the rings of guns 2 and 3, each every 4 s; in
the last stand the two rings and the spiral at 60% of their rate.

The cats' guns have short ranges in the game data (the big slow circles 3.99), so their states set `fire.range` (30, 40 for the inner ring)
or they would only fire with a player almost on top of them.

Not known from the video, so guessed (change them in the launcher's Enemy AI tab): which extra effect, if any, it shows on top of the pink
shield glow (*Shield look*), which guns the boss fires in each phase (all of them), and its turning and dash speeds.
