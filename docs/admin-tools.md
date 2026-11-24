# Admin, debug and login tools

Built on 24/11/2026 (0.6.0), compiled and checked against the client's code, **not played yet**. Code: `Server\Admin\`,
`Server\Network\AccountLogins.cs`, `Server\Enemies.Debug.cs`, hooks in `Program.Messages.cs`, `Program.Udp.cs`, `Game.Shots.cs` (commands).

Who can use what: every chat command and every client debug message goes through `network.json` `chatCommands` (`everyone`, `host`, `none`).
Things that touch another player or the host's accounts (`/screenshot NAME`, `/soundall`, `/passwd`) also need the game of the host's PC.

## Debug settings (`DebugSettings.cs`)

The client has a `GameServer.DebugSetting` enum and reads the float ones while it runs the player's plane (`UnitScript`): `health` 10, `speed` 3,
`turnRate` 5, `steam` 4, `damage` 1 (multiplier of the damage dealt), `damageReceivedMultiplier` 2 (0 = nothing hurts), `epic` 12 (1 = survives with 1 HP).
`spawning` 0, `enemyMovement` 9 and `debugInfo` 11 are on/off switches of the client's own game server (`ClientSetDebugSetting` `3732`); they matter little
here because our server owns the enemies.

| Message | Direction | What the server does |
|---|---|---|
| `ClientSetFloatPlayerDebugSetting` `27395`, `ClientSetStringPlayerDebugSetting` `58246` | server → client | sent by `/debug`, `/god` and as the answer to the two below |
| `ClientClearPlayerDebugSetting` `6848` (byte setting, PeerId = Lidgren unique id) | server → client | `/debug clear` |
| `ServerSetFloatPlayerDebugSetting` `49275`, `ServerSetStringPlayerDebugSetting` `15630`, `ServerClearPlayerDebugSetting` `30792` | client → server | stored per connection and sent back to that player (the shipped client has no UI that sends them: they are the original tools) |
| `ClientSimulateLatency` `38607` | server → client | `/debug latency on\|off` (this build's client ignores it) |
| `ServerWatchAI` `19540` / `ClientReceiveAIDebug` `33683` | both | text about one enemy (type, hp, position, behavior, AI file and state, group); `/watchai [id]` also writes it in the chat. The client only stores the text |
| `ServerQuery` `45861` | client → server | the client got updates of units it never saw spawn (up to 4 ids): the enemies among them are spawned again for that player |

Settings end with the connection (nothing is saved).

## Sounds (`Sounds.cs`)

`ClientPlayAudio` `7335` carries a sound group name of the game's audio bank (MasterAudio); the client plays it once. The known names are the ones the client's own
code uses (UI, inventory, pickups, teleports, stingers: `Sounds.Known`). Any other name is sent as typed: the game plays nothing when it does not have it.
`/sound NAME|N`, `/soundall NAME|N` (host) and the map script action `"sound": "NAME"` (all players of the session; `docs/map-script.md`).

## Screenshots

`/screenshot [PLAYER]` sends `ClientCaptureScreen` `23010` (type `screenshot`, an upload id and the upload and finish URLs). The game takes a JPEG and uploads it
to `http://<server address>:<httpPort>/ozy/user_uploads/put/<id>`; it lands in `client-uploads\screenshot<name><time>.jpg` (the same upload path as the error logs).
The URL uses the address the player's game used to reach the server (`PlayerSession.ServerAddress`). The client also knows type `gif`.

## Server statistics (`ServerStats.cs`)

Every turn of the update loop of each game server (Nexus, tutorial, dungeon, world) is timed. `/stats` shows, per server: connections, turns per second, average and maximum work time,
the worst one, packets and bytes, plus uptime, memory, threads and GC count. `/serverstats on` makes the server send `ClientReceiveServerStats` `59268` every half second
(frame number, longest turn of that half second, update time, average, 0, "a garbage collection happened"); the game draws it in its server time chart when
**Options > debug display** is on.

## Inventory audit (`InventoryAudit.cs`)

Every accepted inventory change has an audit id (`InventoryStore.NextAudit`) that goes back to the client. The client reports what it did in `ServerAuditInvAction` `61999`.
A report fails when its id was never given out (id ≥ the account's next id) or when the same id arrives twice in one connection. Failures go to the log (`[AUDIT]`) and the count goes to
the client in `ClientOnAuditChange` `23009` (this build's client ignores it). `/audit` shows the counts and the last actions. The audit of buffs (`ServerAuditBuffAction`) is not checked.

## Accounts with username and password (`AccountLogins.cs`)

`network.json` `"passwordLogin": true` (default false). The game's Ozy API:

| Route | Answer |
|---|---|
| `/user/login` with `steam_ticket` | as before when the Steam ID has an account; `400 {"errors":["user_missing"]}` when it has none: the game opens its register screen |
| `/user/create` (username, password, e-mail, `steam_ticket`) | creates the account (of the Steam ID when there is a ticket, otherwise `user_<name>`) and logs in; errors `username_invalid`, `password_invalid` (4 to 64 characters), `username_duplicate` |
| `/user/login` with username and password and no ticket | the account of that username; errors `user_missing`, `password_wrong`, `username_invalid` |
| `/user/change` (`old_password`, `new_password`) | needs the `X-Session` of the account |
| `/user/reset_request`, `/activation/*` | empty answer; the reset only logs a line (there is no e-mail server): the host uses `/passwd NAME PASSWORD` |
| `/msg_interface_config` | empty broker settings (the game never calls it) |

Credentials: `accounts\<key>\login.json` (username, salt, PBKDF2-SHA256 hash with 100,000 rounds, e-mail). Passwords never reach the log (`MessageLog.Redact`).
When the Steam game is running the game always tries Steam first, so the username and password screen only shows without Steam (or with `-loginas` / `-password`).

## Pilot XP into currency

`PlaneRetirement.PayCurrencyForPilotXp`: `floor((1 - realTimePlaneXPProportion) × unpaid pilot score) / pilotXpPer1Currency` of `pilotXpCurrencyId`, from
`pilotXpCurrencyAwardThreshold` on (the three come from the game's `configBinary`). Paid when a plane is retired and when it is destroyed (with permadeath off a plane that falls again only
pays the part it had not paid yet). The client shows `ClientNotificationGiveCurrencyForPilotXp` `54391`. `docs/progression.md`.

## Console commands (24/11/2026)

The launcher (Server tab, Console page) and a terminal can run the chat commands above for a player: `Server\Admin\ConsoleCommands.cs` reads lines from the standard input (`NAME<TAB>/command`, `*<TAB>/command` for everybody, `/command` for the game of this PC; `/players` lists who is online) and queues the command for the UDP server each player is connected to; the loop of that server runs it with `Game.RunCommand`, as the player, ignoring `chatCommands`. Each run is a `[CONSOLE]` log line.
