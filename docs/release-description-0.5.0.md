## Steambirds Alliance Revive 0.5.0

Big update: open world, social features, a redesigned launcher, editor improvements and many gameplay additions.

**Note:** the new server features were written from the client's behavior and are not fully tested in the game yet. Please report anything odd.

### New: Open World
- New open world server (port 1114) with a generated map, regions, enemy spawn sites and carriers.
- Configurable through `world.json`.
- Launcher pages for the world map (with each player's position) and world settings.

### New: Social
- Friends list, flocks (Flock Hall), whispers and flock chat.
- Teleport to a friend and "Join Server".
- New Social page in the launcher; settings in `social.json`.

### Gameplay
- Plane variants can be changed (checked against class variants and account unlocks).
- Plane XP boost consumables work and apply to kill XP.
- Dyes and trails are saved on the plane and shown to other players.
- Dungeon keys summon a drop ship to the dungeon (Nexus only, open dungeons).
- Career item rewards go to cargo/storage, with a pending queue if there is no room.
- Store: "use instantly" keeps dyes and keys in your inventory, and the flock founder token is sold for 750 gold.
- Turrets visibly rotate to aim.
- New screen effects: turret hit sparks, camera shake on boss death and dungeon clear, "mission accomplished" banner.
- Other players' bullets show correctly.
- Enemy view radius: each client only knows nearby enemies (`enemies.viewRadius`, default 90), so the radar no longer points at far-away enemies.

### Launcher redesign
- New dark and light themes built on the project branding.
- Tabs grouped as Client, Server | Map editor, Enemy AI | Settings, with server and game state in the header.
- Client tab: Servers / Game / Console sub-tabs, a Play bar that stays in view, and the Steam download button only when the game is missing or on the wrong build.
- Server tab: side navigation (Monitor, Game rules, Players, World, Connection) and separate "New accounts".
- Settings in four sub-tabs; server `.json` settings are filterable tiles with "?" help buttons.
- Icons, new controls, DPI fixes and updated Portuguese translation.

### Map editor and Enemy AI editor
- Game pictures in lists and drop-downs (needs the `editor-assets` folder).
- "Open in the AI editor" jumps from a unit type to the simulator.
- Help menu and `F1` in both editors.
- Enemy AI editor now has a menu bar (File, Edit, Help) and a weapons checklist with per-gun colors and data.

### Upgrade notes
- Copy your `accounts\` folder over as usual. New settings files (`world.json`, `social.json`, `flocks.json`) are created with defaults.
- Rebuild the plugin and re-run `SteambirdsGameFiles.exe`.
- Client build is still **201219.1413**.
