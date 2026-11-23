# Friends, flocks and teleport to players

Status (23/11/2026): built and tested with a test client that speaks the game's messages (two accounts, the Nexus and the open world; 72 checks). **Not tested in the game yet.**
Code in `Server\Social\`, host settings in `Server\social.json`, flocks in `Server\flocks.json`, each account's friends in `accounts\<key>\social.json`.

## What the client does and what the server does

| Feature | Client | Server |
|---|---|---|
| Friend list | keeps the list, shows "Nearby / Flock / Friends / Recent", sends `ServerAddFriend` `12201` and `ServerDeleteFriend` `12202` (types Following 1, Muted 10, Blocked 100) | saves the lists per account, answers `ServerRequestFriendData` `12200` with `ClientReceiveFriendData` `12100` |
| Name, plane, server of a friend | asks `ServerRequestRemotePlayerData` `12509` | same server: `PlayerRelay`; another server of this host or offline: `SocialHandler.AnswerRemoteData` (`serverName` = "Rebel City" / "Open World", `isJoinable`) |
| Flock Hall | every action is an RPC `30000`–`30016` | `SocialHandler.Flocks.cs` + `FlockStore` |
| `/w`, `/f` | send `ServerWhisperChat` `11731` / `ServerGuildChat` `11733` | forward `ClientWhisperChat` `11730` / `ClientGuildChat` `11734` (flock chat also goes back to the sender) |
| Teleport on the same server (radar key, friend list "Teleport", `/tp NAME`) | does it all by itself with the planes it sees, then sends `ServerNotifyCompleteTeleport` `12006` | only logs it. `PlayerRelay` shows every plane of the room (Nexus, world), so every player is a target |
| "Join Server" (friend list, flock roster, `/tp FRIEND` on another server) | sends `ServerSendMeToPlayerServer` `43983` and remembers the target | same server: `ClientTeleportToCoordinatesAfterTime` next to the player; another server (Nexus ↔ world): `ClientSwitchToServer(WithPlane)`; refusal: `ClientSendMeToPlayerServerFailed` `46627` |
| Arriving next to the friend | puts the target in `ServerRequestSpawnPlayerPlane` `4940` | `SocialHandler.ArrivalPlace`: the plane is born `arrivalDistance` units from the friend (free land in the world) |

## Rules

- **Who can be joined:** the target must be online, in the Nexus or in the open world (while it has room), and be a friend of the player or share a flock with them; a player who blocked you cannot be joined. Dungeons are one instance per player and the tutorial is private, so they are "not joinable".
- **Flocks** use the game's own numbers from its `guilds` config (60 members, 3 flocks per player, 5 mutes auto-ban, join cooldown 86400 s after 5 joins, name 2-12 characters); `social.json` can change them.
- **Founder token:** like the original, creating a flock takes one `create_flock_token` (or `test_create_flock`) out of the plane's cargo, and the client only shows the create page to a player who carries one. Give it with `/item create_flock_token`, or put it in the starting items. `requireFounderToken: false` stops it being taken.
- **Roles** (client's `GuildPermissions`): the founder does everything; Officer mutes, kicks, bans and handles invites; Leader also changes settings; Member and Elder only mute. Invite modes: open for all, open for rank 100+, invite only (pending requests wait in "Pending Invites"). A founder who leaves hands the flock to the longest-standing member; the last one out ends it.
- **Default flock:** shown on the name plate and used by `/f`. The id goes in the welcome message (`PlayerData`) and the name in `ClientUpdatePlayerDataDefaultGuild` `48916`.
- **Reconstructions** (the original backend is gone): the server-side rules above, the health formula (copied from the client), the suggestion (random among the 3 healthiest flocks with room), error texts (`guild_name_invalid`, `joined_recently`, `limit_reached`, `required_item_missing` are the client's own; others show as "Unknown error: key").

## Chat commands of the server

`/friends` lists friends and where they are; `/addfriend NAME` adds anybody the server knows, even offline (the game's own `/friend` only finds players it sees). See `Chat commands.md`.

## social.json

A flat file: `friendsEnabled`, `maxFriends`, `flocksEnabled`, `requireFounderToken`, `maxMembers`, `maxFlocksPerPlayer`, `rejoinSeconds`, `rejoinAfterJoins`, `autoBanMutes`, `inactiveSeconds` (0 or -1 = the game value), `toPlayersInWorld` turns the client's own teleport off in the open world through its ClientMapInfo, `joinPlayerServers`, `spawnNearTarget`, `arrivalDistance`). Created with the defaults on the first start and read again when it changes (the ClientMapInfo change applies on the next join of the world).

## Test

A test client (Lidgren, the client's own serializers copied from the decompiled code) logged in two players over HTTP and checked: empty friend list; create flock; taken and invalid names; search, suggestion, join, pending and accept; roster fields (`role`, `founder`, `muted_by`, `ProcId`); flock chat and whisper; friends, mutes, data of an online and an offline friend; roles, mute, kick; join a friend on the same server; refusal for an offline or unrelated player; Nexus → open world and the new plane next to the friend; persistence after a restart; founder token taken from the cargo; `/addfriend` and `/friends`.

## Not done

- Launcher: Server tab > Friends and flocks (`Launcher\UI\SocialPage.cs`) lists the flocks and their members (read only, from `flocks.json`) and edits `social.json`. Built, not opened yet. Flocks are managed by the players in the Flock Hall, not from the launcher.
- Squads (`13500`–`13505`), secure trade, spectate/follow and the Combat Sim flock message (`11736`) are still missing (see `CHECKLIST.md`, section 10).
- Friends who are only in a dungeon appear as "online" but cannot be joined.
