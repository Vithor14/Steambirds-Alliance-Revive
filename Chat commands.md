# Steambirds Alliance chat commands

Nov 15, 2026 · @Vithor Temanski Neves Castelo (command names updated to English on Nov 22, 2026; checked against the code on Nov 23, 2026)

The game has two groups of chat commands. The client resolves its own commands by itself, without asking the server. Any other command that starts with `/` goes to the server: the official server's commands were lost, and today our server answers them.

Text without `/` goes to the proximity chat. Two slashes in a row (`//`) become one.

## The game's own commands (client 201219)

The descriptions come from the client's chat code (`ChatManager.cs`) and from the help texts the game ships. These commands never reach the server, so our server cannot create a command with the same names.

### General

| Command | What it does | On our server |
| --- | --- | --- |
| `/help [command]` | Lists the common commands, or explains one (`/help trade`) | Works |
| `/help_all` | Lists every client command | Works |
| `/options` | Opens the options menu | Works |
| `/resetbindings` | Puts the controls back to the default | Works |
| `/clearsavedlogin` | Deletes the saved user name and password | Works |
| `/tutorial` | Asks to play the tutorial | Works (goes to the tutorial server) |
| `/ingredients` | Opens the ingredients panel | Opens, but empty |
| `/die` | Opens the menu to bring your own plane down (health to 0) | Works |
| `/retire` | Opens the menu to retire the plane (in the Combat Sim it becomes `/die`) | The server handles retirement since Nov 22 (pays the pilot XP, moves the items to storage); not tested in the game yet |

### Social

Friends, flocks, whispers and teleport are handled by our server (`Server\Social`, not tested in the game yet; see `docs/social.md`). Safe trade (`/trade`) is also ours (`Server\Social\SecureTrade*.cs`, `docs/social.md`; built, not played yet).

| Command | What it does |
| --- | --- |
| `/friend NAME` | Adds a friend who is on the same server |
| `/friends` | (our server) Lists your friends and where each one is |
| `/addfriend NAME` | (our server) Adds any player this server knows, even offline or on another server |
| `/unfriend NAME` | Ends the friendship |
| `/block NAME` / `/unblock NAME` | Blocks or unblocks every interaction with the player |
| `/blocklist` | Lists the blocked players |
| `/mute NAME` / `/unmute NAME` | Hides or shows the player's messages |
| `/mutelist` | Lists the muted players |
| `/teleport NAME` (or `/tp`) | Goes to the player. With no name, goes to the squad leader. If the friend is on another server, switches server |
| `/whisper NAME message` (`/w`, `/tell`, `/t`) | Private message |
| `/flock message` (`/f`, `/g`) | Chat of the default flock |
| `/myflock [name]` | Lists your flocks or changes the default one |
| `/trade NAME` | Asks for a safe trade. With no name, accepts the last request |
| `/trade_reject` | Refuses the last trade request |

### Admin only

On our server every player joins as an admin.

| Command | What it does | On our server |
| --- | --- | --- |
| `/sidelength N` | Changes the size of the visible area (default 11) | Works |
| `/spectate` | Enters spectator mode | Does not work yet |
| `/follow NAME` | Follows a player or unit during spectator mode | Does not work yet |
| `/combatsim help`, `ui PLACE`, `start MODE [period] [seed]`, `leaderboard MODE` | Opens and starts the Combat Sim | Does not work yet |

### Debug

They only exist when the Steam build turns the debug commands on; in the current installation `/reset_tutorial` has already worked.

| Command | What it does |
| --- | --- |
| `/reset_tutorial` | Marks the tutorial as not played: the next login at the Nexus sends the player to the tutorial again |
| `/reset_client_event_once_keys` | Clears the client's "hint already shown" marks |

## Our server's commands

The server tries, in this order: progression, items, dungeons, tutorial and enemies. A command that none of them knows only shows up in the log. All names are in English; the first piece of the chat line is the command, the others are its arguments.

### Network

| Command | What it does |
| --- | --- |
| `/players` | Lists the players online (the host's own game is marked) |

Who may use the other commands is decided by `chatCommands` in `network.json`: `everyone`, `host` (only the game on the server's PC) or `none`. `/leave` and `/players` always work.

### From the server console (launcher)

The host can also run every command above for a player without typing in the game: Server tab, Console page, *Run for* (the game of this PC, all players online or one player), the command, Enter. *Command list...* lists the commands with a description. The command runs as that player (it ignores `chatCommands`, the console is the host's), its answer goes to that player's chat and the server logs a `[CONSOLE]` line. The launcher writes to the standard input of the server (`Server\Admin\ConsoleCommands.cs`); in a terminal the lines are `NAME<TAB>/command`, `*<TAB>/command` (everybody) or `/command` (the game of this PC).

### Admin and debug tools

Built in 0.6.0 (`Server\Admin\`, details in `docs/admin-tools.md`). They follow `chatCommands` of `network.json`; the ones marked "host" only work in the game of the server's PC.

| Command | What it does |
| --- | --- |
| `/admin` | Lists these commands |
| `/debug` | Shows your plane's debug settings and the names. `/debug NAME VALUE` sets one (`damage`, `damageReceivedMultiplier`, `speed`, `steam`, `turnRate`, `health`, `epic`...; the switches `spawning`, `enemyMovement`, `debugInfo` take `on`/`off`). `/debug clear [NAME]` takes them off, `/debug latency on\|off` |
| `/god [off]` | Nothing hurts you (`damageReceivedMultiplier` 0) |
| `/sound NAME` (or `/sound N`) | Plays a sound of the game's audio bank for you; `/sound` lists the known names |
| `/soundall NAME` | Same, for everybody on this server (host) |
| `/screenshot [PLAYER]` | The game takes a picture and uploads it to the server (`client-uploads\screenshot....jpg`); of another player: host |
| `/stats` | Server loop time, packets, memory, players |
| `/serverstats [on\|off]` | Sends the server time chart to your game (Options > debug display) |
| `/audit` | Inventory audit of your connection: actions reported and failures |
| `/watchai [ID]` | AI text of an enemy (the closest one when no id is given) |
| `/passwd NAME PASSWORD` | Changes the password of an account with username and password (host; `passwordLogin` in `network.json`) |

### Items

Work on any server. Item ids and loot tables: `catalog\item_catalog.md` and `loot_tables\`.

| Command | What it does |
| --- | --- |
| `/items` | Lists the item commands |
| `/item ID [N]` | Puts N of the item in the cargo (up to 20; for example `/item armor_3`) |
| `/loot [unit] [tier]` | Rolls the loot of a kill in front of you |
| `/inventory` | Shows the plane's inventory as text |
| `/clear_cargo` | Empties the cargo |
| `/currency [ID N]` (or `/currencies`) | Shows the currencies, or adds N to one |
| `/ingredient ID N` | Adds N of an ingredient |
| `/bags` | Counts your loot bags in the world and lists the loot tables |
| `/clear_bags` | Removes your loot bags |
| `/export_items` | Writes the item catalog again (`catalog\`) |

### Progression

Work on any server.

| Command | What it does |
| --- | --- |
| `/progression` | Lists the progression commands |
| `/pilot` | Shows the pilot's rank, XP and status |
| `/xp N` | Gives N XP to the plane |
| `/level N` | Puts the plane at level N |
| `/rank N` | Puts the pilot at rank N |
| `/reset_pilot` | Resets the pilot's rank, XP and skills |
| `/buff ID` | Gives a buff (with no ID, lists the possible buffs) |
| `/buffs` | Lists the buffs |
| `/no_buffs` | Takes every buff off |
| `/portrait` | Rolls a new portrait for the pilot |

### Dungeons

Dungeon server (port 1113).

| Command | Where | What it does |
| --- | --- | --- |
| `/dungeons` | anywhere | Lists the dungeons of `dungeons.json` and whether they are open |
| `/dungeon NAME` | anywhere | Goes to the dungeon (`/dungeon bomb`, `/dungeon iron`) |
| `/dungeonhelp` | anywhere | Help and the state of the current dungeon |
| `/room` | in the dungeon | Lists the rooms (checkpoints) |
| `/room N` | in the dungeon | Jumps to room N; what was left behind counts as cleared |
| `/boss` | in the dungeon | Jumps to the boss room |
| `/restart` | in the dungeon | Restarts the dungeon from the beginning |
| `/leave` | in the dungeon | Goes back to the Nexus |

### Open world

Open world server (port 1114, terrain `Basic`, settings in `world.json`). The Nexus also has a portal to it.

| Command | Where | What it does |
| --- | --- | --- |
| `/world` | anywhere | Goes to the open world (builds the map if nobody is in it) |
| `/worldinfo` | anywhere | Seed, players, spawn sites with enemies, your position, region and elevation, and the carriers |
| `/carrier [NAME]` | in the world | Puts a dungeon carrier of an open dungeon next to you (to test; with no name, the first open dungeon) |

The way back is the menu's "Return to Rebel City" (or `/leave`).

### Tutorial

Only valid inside the tutorial.

| Command | What it does |
| --- | --- |
| `/tutorialhelp` | Help and the current step |
| `/step N` | Jumps to a step: 1 drones, 2 drone ring, 3 crate, 4 boss gate, 5 portal |
| `/portal` | Creates the portal to the Nexus in front of you (or takes you to it, if it already exists) |
| `/restart` | Restarts the tutorial |
| `/leave` | After the boss, goes to the Nexus without entering the carrier |

### Enemies, for tests

Work on any server.

| Command | What it does |
| --- | --- |
| `/enemies` (or `/enemyhelp`) | Lists these commands and the state of the encounter |
| `/wave N` | Jumps to wave N of the `enemies.json` encounter |
| `/next` | Calls the next wave |
| `/boss` | Calls the boss wave (inside a dungeon, `/boss` is the dungeon command) |
| `/enemy TYPE [count]` | Creates enemies of a type from `enemies.json` near you (up to 20) |
| `/spawn TEMPLATE [count]` | Creates any unit of the game by its template name (up to 20) |
| `/clear` | Removes every enemy and stops the waves |
| `/reset` | Restarts the encounter |
| `/encounter [name]` | Lists the encounters or starts one |
| `/types` | Lists the types of `enemies.json` and the template of each |
| `/catalog [filter]` | Searches the game's armed units (up to 15 at a time) |
| `/brains` | Lists the AI files the server loaded (`server\ai`) and the state of the enemies that use one. Types that use an AI: `/enemy ai_chaser`, `ai_bomber`, `ai_charger`, `ai_skirmisher`, `ai_sentry`, `ai_berserker`, `ai_inquisitor`, `ai_ambassador`, `ambassador_cat`, `ambassador_cat_inner`, `ambassador_cat_outer`, `ambassador_diplomat` |
| `/wreck UNIT [seconds] [effect\|none] [state]` | Spawns a still wreck of that unit in front of you (20 seconds and the default death effects when the extras are left out). A type with `deathUnit` leaves a wreck when it dies |

### Stores

Store server (`Server\Stores`), not tested in the game yet. Also answers to `/loja` and `/shop`. To test purchases, give yourself gold with `/currency gold 20000`.

| Command | What it does |
| --- | --- |
| `/store` | Lists the store commands |
| `/store info` | Shows whether purchases are on, the price mode, the slots (hangar, loadouts, storage, cargo; base and bought) and your currencies |
| `/store list [store]` | Lists the shelves of a store (`store_general` by default; also `store_hangar`, `store_storage`, `store_skill_loadouts`) |
| `/store buy PRODUCT [PRICE]` | Buys a product from the first shelf that has it (PRICE: `normal` = gold, or `iron`) |
| `/store reset` | Forgets the purchases of the account (slots go back to the base; items and currencies stay) |
| `/store restock` | Refills the server stock (`stock` in `stores.json`) |

## Notes

The client code also has texts such as `/pilot_levelup/`, `/boss/` and `/countdown/`. They are not commands to type: they are message templates that the server sends and the client turns into text on the screen.
