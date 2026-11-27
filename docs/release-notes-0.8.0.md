# Steambirds Alliance Revive 0.8.0

On top of 0.7.1. Client **201219.1413** and plugin **1.22.0** are unchanged. Settings and maps from 0.7.1 keep working. Formations and the Ambassador fight are a reconstruction.

## Formations

- New **Formation** tab. It says who is born with the leader, where each piece sits, and the links (shield while someone lives, kill the other pieces on death, drop the next shield at a health percent, heal). Each piece keeps its own AI. Only the leader is placed on a map, a world spawn or a dungeon.
- Files live in `ai\formations\generic`, `world` and `dungeons\<name>`. Shipped fights: the train (`shield_convoy`), the Sergeant Pup (`sergeant_pup`) and the Five Star (`five_star`).
- **Train:** cars follow the head. Every car is shielded except the tail tip, and destroying the tip destroys the train.
- **Sergeant Pup:** four pods use `follow` (same offset as a slot, but they fly there on their own, so a hard turn does not teleport them). The leader keeps the small enemy bubble until the four pods fall, then fights alone. `tankShield` stays the player tank.
- **Five Star:** points around the leader, walked in straight legs (a pentagon). Points do not turn with the leader, and the stars keep walking after the leader dies.
- A chain or a slot stays bolted to the leader. Choices show a plain name and a line that says what they do. Unused fields stay gray. Shields, kill-all, unshield and heal are one Links list.
- Same flow as the other editors: one tab each for the formation, members, links, points and phase summons, fields under the selected line. File actions are in the menus and on right-click. The preview draws the game's ships, can pause, and has speed, leader motion, right-click damage, health drain, Heal and respawn. F1 explains the tab.
- A map unit and each biome enemy can name a Formation. On a world site that replaces the random escorts. The list offers only formations whose leader is that unit, and picking one on the default AI also applies that formation's leader AI.

## Enemy AI

- Guns named `anydir` (Five Star, Solaris planets and rings, Meowza mounts) turn between shots and fire all around, in the simulator and on the server. Before, they only fired forward.
- **Orbit the group leader** (`generic_orbit_leader`): escorts circle their leader, or the player when they have none.
- The simulator flies the group leader and summons with their own AI and art. The Leader box runs that AI, follows Ctrl+click, holds still, or is taken away. Right-click deals 25% damage. Summons last until destroyed.
- `spin_ignore` holds, turns slowly and shoots at any range (Five Star leader). `face_player` holds, faces the player and shoots (the Pup after its pods fall).
- Summons are a list (unit, AI, role, place, count, interval, cap). Amount 0 summons nothing. Place can be a formation point. Path, zigzag, orbits and a death wreck (`deathUnit`) are in the AI.
- **Ambassador** (`ufo`, AI `ambassador` unless the host picked one): cats on a locked orbit, a spinning triangle, zigzag diplomats, and under 30% health a return to the center. **Inquisitor** uses `inquisitor_chase`. Chat accepts those AI ids (`/enemy ai_ambassador`). Do not set `ambassador_body` on that AI. The phases are the community fight.
- Right-click a file to rename it. A generic AI stays protected. AI lists show ones made for that unit, then the generic ones. Fields follow the dark and light themes while gray too.

## Open world

- Each enemy in a biome group has an **Amount** (0 to 30). With amounts the group is exact (1 Five Star leader and 5 escorts). *Random group* clears them. Saved in `world-enemies.json`.
- Bosses appear after the groups, with parts and minions, and cannot be removed. AI, Formation and Amount are edited on the row.
- A group marked as two bosses (Solaris and the Ambassador) splits. Parts the AI summons are not also placed on the ring.

## Other

- `Push to GitHub.ps1` commits and pushes `main` only, and stops if the current branch has commits `main` does not.

Full log: `docs/update-notes-0.8.0.md`.
