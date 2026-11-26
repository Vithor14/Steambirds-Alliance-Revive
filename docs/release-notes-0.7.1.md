# Steambirds Alliance Revive 0.7.1

Patch on top of 0.7.0. Client build **201219.1413** and plugin **1.22.0** are unchanged. Settings and maps from 0.7.0 keep working.

## Enemy AI editor

- **Generic AIs can no longer be overwritten.** The AIs of the `generic` folder (and the behavior files they use) run for every unit with no AI of its own, so the editor never changes, saves over, renames, moves or deletes them. Load a unit and you see its generic AI as it is; the first change turns what is open into **"new {unit} AI"**, a new AI with your changes, made for that unit and kept in its folder (`dungeons\<dungeon>` or `world`). **Save** writes it there. The generic AI stays as it was.
- **Save as...** (new in the *File* menu), **Duplicate**, **Export**, **New AI** and **Import** do the same: what comes out of a generic AI is an AI made for the selected unit, never a generic one.
- **Behavior values:** the fields the movement mode does not use are greyed out (for example *Distance* only works in `orbit` and `wander`).

## Open world

- **Biome enemy lists were missing many enemies.** Units the game files under other names are back: tetra, blink and blinkleader (Egyptian Dig), birdcatcher and vampire (Noble Estates), duelist, marksman, barrager, hammerplanets, hyperdrive, lion, lion cub, destroyer and sentinel (Imperial Palace), hurtsnake (Hospital Area), zombie28days and zombieromero (Graveyard) and remora (Fishing Zone). Projectiles and tower parts stay out. A biome you already saved in `world-enemies.json` with its own groups keeps its list: add them in *Server > World map > Enemies of each biome*.
- *Enemies of each biome* is its own tab in *Server > World map*, with bigger enemy pictures.

## Fixes

- **Skill clouds follow the plane variant.** Poison, heal, damage and shield clouds, blasts, auras and the bite cloud now look like the variant of the plane that used the skill (`EpicA`..`EpicD`), instead of always the default one. Only the picture changes.

Full details (English and Portuguese): `docs/update-notes-0.7.1.md`.
