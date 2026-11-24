using System.Net;
using Lidgren.Network;

// Program (part): the client messages that belong to the host itself (login, planes, portals, EULA, chat commands).
// Everything else goes on to InventoryHandler, ProgressionHandler and Game.

public static partial class Program
{
    // Chat commands decided by the network (network.json "chatCommands") and /players. True = handled or blocked here.
    static bool HandleNetworkCommand(NetServer server, NetIncomingMessage msg, byte role)
    {
        var start = msg.Position;
        var pieces = new List<string>();
        try
        {
            uint countPlusOne = msg.ReadVariableUInt32();
            for (uint i = 0; i + 1 < countPlusOne && i < 8 && CopperReader.BitsLeft(msg) > 0; i++)
                pieces.Add(msg.ReadString() ?? "");
        }
        catch
        {
            // Malformed command: Game logs it.
        }
        finally
        {
            msg.Position = start;
        }
        if (pieces.Count == 0) return false;

        NetConnection connection = msg.SenderConnection;
        string command = pieces[0].Trim().ToLowerInvariant();
        if (command == "/players")
        {
            List<PlayerSession> online = PlayerSessions.Online();
            string list = string.Join(", ", online.Select(s => s.Name + (s.FromThisPc ? " (host)" : "")));
            WorldObjects.SendChat(server, connection, $"{online.Count} player(s) online: {list}");
            return true;
        }

        // /leave always works: it is the exit of the dungeon and of the tutorial.
        if (command == "/leave") return false;

        string who = ServerNetworkSettingsFile.NormalizedCommands(ServerNetworkSettingsFile.Current.ChatCommands);
        PlayerSession? session = PlayerSessions.Of(connection);
        bool allowed = who == "everyone" || (who == "host" && session != null && session.FromThisPc);
        if (allowed) return false;

        Log("CHAT", $"[{RoleName(role)}] {PlayerSessions.NameOf(connection)} tried {command}: chat commands are off for this player (network.json chatCommands = {who})");
        WorldObjects.SendChat(server, connection, "Chat commands are turned off on this server.");
        return true;
    }

    // ServerSetSessionTicket: binds the connection to the login session (account and name). With no known session, an account by IP.
    static PlayerSession BindSession(NetConnection connection, string ticket, string user, out string howFound)
    {
        PlayerSession? session = PlayerSessions.Bind(connection, ticket, out howFound);
        if (session != null) return session;

        IPAddress? remote = connection.RemoteEndPoint?.Address;
        var (key, steamId, _) = SteamTicket.AccountKey(null, null, remote?.ToString());
        bool fromThisPc = PlayerSessions.IsThisPc(remote);
        string name = PlayerNamePath.Clean(user);
        Accounts.Account account = Accounts.GetOrCreate(key, name, steamId, out string note);
        if (name.Length == 0) name = account.Name;
        PrepareAccount(account);
        IPAddress address = remote == null ? IPAddress.Loopback : PlayerSessions.LocalAddressToward(remote);
        session = PlayerSessions.Create(account, name, address, remote);
        PlayerSessions.BindTo(connection, session);
        howFound = "unknown sessid: account by IP " + remote + (note.Length > 0 ? " (" + note + ")" : "");
        return session;
    }

    // Once per loaded account: starting rank, levels and unlocks (PilotProgression) and the inventory (read now so the log shows it).
    static void PrepareAccount(Accounts.Account account)
    {
        if (account.Prepared) return;
        using Accounts.Scope accountScope = Accounts.Use(account);
        PilotProgression.PrepareAccount();
        InventoryStore.Read(d => 0);
        account.Prepared = true;
    }

    static void HandleData(NetServer server, NetIncomingMessage msg, byte role)
    {
        if (msg.LengthBytes < 2)
        {
            Log("UDP", $"data {msg.SenderEndPoint} {msg.LengthBytes}B (too short)");
            return;
        }

        // Everything this message does to the account (plane, XP, items, dungeons) goes to the account of this connection's player.
        using Accounts.Scope accountScope = Accounts.UseFor(msg.SenderConnection);

        var msgId = msg.ReadUInt16();

        // TimeSync.RequestDelta: ushort 1, double clientNetTime, int counter (14 bytes)
        if (msgId == 1 && msg.LengthBytes == 14)
        {
            var clientTime = msg.ReadDouble();
            var counter = msg.ReadInt32();

            var resp = server.CreateMessage();
            resp.Write((ushort)2);
            resp.Write(clientTime);
            resp.Write(counter);
            resp.Write((float)Clock.Elapsed.TotalSeconds);
            server.SendMessage(resp, msg.SenderConnection, NetDeliveryMethod.Unreliable, 0);

            if (counter % 200 == 0) Log("SYNC", $"[{RoleName(role)}] RequestDelta #{counter} answered");
            return;
        }

        // ServerSetSessionTicket: ushort 28035, string sessionTicket, string clientVersion, string username
        if (msgId == MsgServerSetSessionTicket)
        {
            var session = msg.ReadString();
            var version = msg.ReadString();
            var user = msg.ReadString();
            NetConnection connection = msg.SenderConnection;

            PlayerSession player = BindSession(connection, session, user, out string howFound);
            using Accounts.Scope playerScope = Accounts.Use(player.Account);
            string name = player.Name;
            // Map test mode: a plane and the tutorial done, before the welcome tells the client what the account has.
            if (MapTest.Active && role == RoleNexus) MapTest.PrepareAccount(server, connection, Log);
            Log("HELLO", $"[{RoleName(role)}] {name} (account {player.Account.Key}, {howFound}{(player.FromThisPc ? ", this PC" : ", from " + connection.RemoteEndPoint)}), " +
                $"version={version}, client user={user}");

            // Server full: only those who were not online already count (switching servers does not count as joining again).
            int max = Math.Max(1, ServerNetworkSettingsFile.Current.MaxPlayers);
            if (role == RoleNexus && !PlayerSessions.IsOnline(player.Account, connection) && PlayerSessions.OnlineCount() > max)
            {
                PlayerSessions.Unbind(connection);
                Log("PLAYER", $"{name} refused: server full ({max} player(s))");
                connection.Disconnect($"Server full ({max} players)");
                return;
            }

            var procId = role == RoleNexus ? "revive-1" : role == RoleDungeon ? "revive-dungeon-1" : role == RoleWorld ? "revive-world-1" : "revive-tutorial-1";
            // Dungeon server: the session is born before the welcome, which carries the dungeon's terrain (the client's TerrainConfig).
            // The open world works the same way: its map exists before the welcome, which carries the terrain "Basic".
            string? dungeonTerrain = role == RoleDungeon
                ? DungeonServer.PrepareSession(server, connection, Accounts.AccountIdText, Clock.Elapsed.TotalSeconds, Log)
                : role == RoleWorld
                    ? WorldServer.PrepareSession(server, connection, Clock.Elapsed.TotalSeconds, Log)
                    : null;
            // Expired buffs leave, a pilot without a portrait gets one and the unlocks are checked before the welcome.
            PilotProgression.PrepareForWelcome();
            InventoryHandler.PrepareForWelcome(RoleName(role));
            var planes = Accounts.Planes();
            var finished = Accounts.FinishedDungeons();
            var pilot = Accounts.Pilot();
            var acceptances = Accounts.Acceptances();
            var welcome = server.CreateMessage();
            PlayerData.WriteClientWelcome(welcome, Accounts.AccountId, name, acceptances, role, procId, planes, finished, pilot,
                dungeonTerrain, dungeonTerrain ?? "", role == RoleWorld ? WorldServer.RadiusFor(connection) : 650f);
            int welcomeBytes = welcome.LengthBytes;
            int pilotXp = Accounts.Read(data => PilotProgression.ComputedPilotXp(data));
            server.SendMessage(welcome, connection, NetDeliveryMethod.ReliableOrdered, 0);
            Log("WELCOME", $"[{RoleName(role)}] ClientWelcomeMessage for {name} sent ({welcomeBytes} bytes, {acceptances.Count} acceptance(s), " +
                $"{planes.Count} plane(s) [{string.Join(", ", planes.Select(a => a.Class + " level " + a.Level))}], " +
                $"pilot rank {pilot.Level} ({pilotXp} XP, {pilot.Unlocks.Count} unlock(s)), " +
                $"finished dungeons: {(finished.Count == 0 ? "none" : string.Join(", ", finished))})");

            // Game needs to know who the player is to answer the data requests of their own account.
            Game.Identify(connection, RoleName(role), Accounts.AccountId, name, procId, Clock.Elapsed.TotalSeconds);
            // Multiplayer: from now on the other players of this server can see and hear this one (Network\PlayerRelay.cs).
            // The Nexus is one group; each dungeon instance is a group of its own; the tutorial is private.
            string? relayRoom = role == RoleNexus ? PlayerRelay.NexusRoom : role == RoleDungeon ? DungeonServer.RoomKey(connection) : role == RoleWorld ? WorldServer.RoomKey(connection) : null;
            if (relayRoom != null)
                PlayerRelay.Joined(server, connection, relayRoom, name, Accounts.AccountId, procId, Accounts.Pilot().Level);
            // Social: online for the friend list and the flock roster, and the default flock goes to the client.
            SocialHandler.PlayerIdentified(server, connection, role, RoleName(role));
            PilotProgression.PlayerIdentified(server, connection, RoleName(role), Clock.Elapsed.TotalSeconds);
            // Ingredient wallet (ClientUpdateIngredients); the items already went in the PlayerData of the welcome.
            InventoryHandler.PlayerIdentified(server, connection, RoleName(role));

            // Floor: in the tutorial, the script's plan (rooms, corridors and gates); in the Nexus, the Rebel City.
            // Goes after the welcome, on the same reliable channel (guaranteed order).
            if (role == RoleTutorial)
            {
                Tutorial.PlayerJoined(server, connection, Clock.Elapsed.TotalSeconds, Log);
            }
            else if (role == RoleDungeon)
            {
                DungeonServer.PlayerJoined(server, connection, Clock.Elapsed.TotalSeconds, Log);
            }
            else if (role == RoleWorld)
            {
                WorldServer.PlayerJoined(server, connection, Clock.Elapsed.TotalSeconds, Log);
            }
            else
            {
                int floorBytes = Nexus.SendFloor(server, connection, out string floorSummary);
                Log("FLOOR", $"[nexus] ClientReceiveTerrainTiles sent ({floorBytes} bytes, {floorSummary})");
                // The other Nexus players find out (they do not see each other yet: each one has their own world).
                TellOthers(server, connection, $"{name} joined the server.");
            }

            // ClientMapInfo of the terrain (client events: the original hints of the tutorial and the Nexus).
            var terrain = role == RoleTutorial ? Tutorial.DungeonName : role == RoleDungeon || role == RoleWorld ? dungeonTerrain ?? "" : "Nexus";
            var mapInfo = server.CreateMessage();
            int infoBytes = MapInfo.WriteMessage(mapInfo, terrain, Log);
            if (infoBytes >= 0)
            {
                server.SendMessage(mapInfo, connection, NetDeliveryMethod.ReliableOrdered, 0);
                Log("MAPINFO", $"[{RoleName(role)}] ClientSetClientMapInfo of '{terrain}' sent ({infoBytes} bytes)");
            }

            // Map test mode: the Nexus is only the door. Straight to the map (also after a death or /leave: it starts again).
            if (MapTest.Active && role == RoleNexus)
                SendToDungeon(server, connection, MapTest.Entry.Type, "map test: " + MapTest.Entry.Name);
            return;
        }

        // ServerSendMeToTutorial: ushort 4060, string tutorialDungeon
        if (msgId == MsgServerSendMeToTutorial)
        {
            var tutorial = msg.ReadString();
            if (!string.IsNullOrWhiteSpace(tutorial)) RequestedTutorial = tutorial;
            if (MapTest.Active)
            {
                // There is no tutorial server in the map test mode.
                SendToDungeon(server, msg.SenderConnection, MapTest.Entry.Type, "map test (the client asked for the tutorial '" + tutorial + "')");
                return;
            }
            IPEndPoint destination = PlayerSessions.AddressFor(msg.SenderConnection, new IPEndPoint(IPAddress.Loopback, TutorialPort));
            Log("TUTORIAL", $"[{RoleName(role)}] {PlayerSessions.NameOf(msg.SenderConnection)} asked for '{tutorial}' -> ClientSwitchToServer to {destination}");

            var swap = server.CreateMessage();
            PlayerData.WriteClientSwitchToServer(swap, destination, HostIdTutorial, "revive-tutorial", BuildVersion);
            server.SendMessage(swap, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
            DisconnectIfStillHere(msg.SenderConnection);
            return;
        }

        // SendMarkDungeonFinished: the client entered an exit portal (Portal.TryToTeleport).
        // The dungeon goes into finishedDungeonTypes: in the Nexus the client stops asking for the tutorial.
        if (msgId == MsgSendMarkDungeonFinished)
        {
            if (role == RoleDungeon)
            {
                DungeonServer.MarkFinished(msg.SenderConnection, Log);
                return;
            }
            if (role != RoleTutorial)
            {
                Log("TUTORIAL", $"[{RoleName(role)}] SendMarkDungeonFinished outside the tutorial: ignored");
                return;
            }
            bool fresh = Accounts.MarkDungeonFinished(Tutorial.DungeonName);
            if (RequestedTutorial != Tutorial.DungeonName) fresh |= Accounts.MarkDungeonFinished(RequestedTutorial);
            Log("TUTORIAL", $"[tutorial] SendMarkDungeonFinished from {PlayerSessions.NameOf(msg.SenderConnection)}: " +
                $"{(fresh ? "tutorial marked as finished" : "the tutorial was already finished")} " +
                $"(finished dungeons: {string.Join(", ", Accounts.FinishedDungeons())})");
            return;
        }

        // SendResetDungeonFinished: /reset_tutorial in the chat. The next login at the Nexus sends to the tutorial again.
        if (msgId == MsgSendResetDungeonFinished)
        {
            int removedCount = Accounts.ClearFinishedDungeons();
            Log("TUTORIAL", $"[{RoleName(role)}] SendResetDungeonFinished from {PlayerSessions.NameOf(msg.SenderConnection)}: {removedCount} dungeon(s) taken off the finished list");
            return;
        }

        // ServerSendMeThroughPortal(int portalShortId): ClientPortalResult and, if the portal is the tutorial's exit,
        // the switch to the Nexus with the plane.
        if (msgId == MsgServerSendMeThroughPortal)
        {
            int portal = msg.ReadInt32();

            // Nexus: the portal of the open world -> world server with the plane.
            if (role == RoleNexus && WorldServer.IsNexusPortal(portal))
            {
                bool open = WorldServer.Available(out string worldReason);
                var answer = server.CreateMessage();
                PlayerData.WriteClientPortalResult(answer, portal, open);
                server.SendMessage(answer, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
                if (!open)
                {
                    Log("PORTAL", $"[nexus] ServerSendMeThroughPortal({portal}): the open world is not available ({worldReason}) -> ClientPortalResult false");
                    WorldObjects.SendChat(server, msg.SenderConnection, "The open world is not available: " + worldReason + ".");
                    return;
                }
                Log("PORTAL", $"[nexus] ServerSendMeThroughPortal({portal}): portal of the open world -> ClientPortalResult true");
                SendToWorld(server, msg.SenderConnection, "[nexus] portal of the open world");
                return;
            }

            // Open world: a dungeon carrier -> dungeon server with the plane.
            if (role == RoleWorld)
            {
                DungeonCatalog.Entry? carrierEntry = WorldServer.CarrierEntry(msg.SenderConnection, portal, Clock.Elapsed.TotalSeconds);
                var answer = server.CreateMessage();
                PlayerData.WriteClientPortalResult(answer, portal, carrierEntry != null);
                server.SendMessage(answer, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
                if (carrierEntry == null)
                {
                    Log("PORTAL", $"[world] ServerSendMeThroughPortal({portal}): unknown or expired carrier -> ClientPortalResult false");
                    return;
                }
                Log("PORTAL", $"[world] ServerSendMeThroughPortal({portal}): carrier of {carrierEntry.Label} -> ClientPortalResult true");
                SendToDungeon(server, msg.SenderConnection, carrierEntry.Type, "[world] carrier of " + carrierEntry.Label);
                return;
            }

            // Nexus: dungeon carrier (DungeonCatalog) -> dungeon server with the plane.
            if (role == RoleNexus)
            {
                DungeonCatalog.Entry? entry = DungeonCatalog.NexusPortal(portal, out string portalReason);
                var response = server.CreateMessage();
                PlayerData.WriteClientPortalResult(response, portal, entry != null);
                server.SendMessage(response, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
                if (entry == null)
                {
                    Log("PORTAL", $"[nexus] ServerSendMeThroughPortal({portal}): unknown portal{(portalReason.Length > 0 ? " (" + portalReason + ")" : "")} -> ClientPortalResult false");
                    return;
                }
                Log("PORTAL", $"[nexus] ServerSendMeThroughPortal({portal}): carrier of {entry.Label} -> ClientPortalResult true");
                SendToDungeon(server, msg.SenderConnection, entry.Type, "[nexus] carrier of " + entry.Label);
                return;
            }

            // Dungeon: only this session's exit carrier, which leads to the Nexus.
            if (role == RoleDungeon)
            {
                bool exit = DungeonServer.IsExitPortal(msg.SenderConnection, portal);
                var response = server.CreateMessage();
                PlayerData.WriteClientPortalResult(response, portal, exit);
                server.SendMessage(response, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
                if (!exit)
                {
                    Log("PORTAL", $"[dungeon] ServerSendMeThroughPortal({portal}): unknown portal -> ClientPortalResult false");
                    return;
                }
                // Makes sure the mark is there even if SendMarkDungeonFinished did not arrive before.
                DungeonServer.MarkFinished(msg.SenderConnection, Log);
                Log("PORTAL", $"[dungeon] ServerSendMeThroughPortal({portal}): exit carrier -> ClientPortalResult true");
                SendToNexus(server, msg.SenderConnection, "[dungeon] dungeon exit through the carrier");
                return;
            }

            bool valid = role == RoleTutorial && Tutorial.IsExitPortal(msg.SenderConnection, portal);

            var result = server.CreateMessage();
            PlayerData.WriteClientPortalResult(result, portal, valid);
            server.SendMessage(result, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);

            if (!valid)
            {
                Log("PORTAL", $"[{RoleName(role)}] ServerSendMeThroughPortal({portal}): unknown portal -> ClientPortalResult false");
                return;
            }

            // Makes sure the mark is there even if SendMarkDungeonFinished did not arrive before.
            Accounts.MarkDungeonFinished(Tutorial.DungeonName);
            Log("PORTAL", $"[tutorial] ServerSendMeThroughPortal({portal}): exit portal -> ClientPortalResult true");
            SendToNexus(server, msg.SenderConnection, "[tutorial] tutorial exit through the portal");
            return;
        }

        // ServerSendMeToNexus: "go back to base" (the client's AskToReturnToNexus state).
        if (msgId == MsgServerSendMeToNexus)
        {
            if (role == RoleNexus)
            {
                Log("NEXUS", "[nexus] ServerSendMeToNexus in the Nexus itself: ignored");
                return;
            }
            SendToNexus(server, msg.SenderConnection, $"[{RoleName(role)}] ServerSendMeToNexus");
            return;
        }

        // ServerAcceptLegalNotice: ushort 54200, string legalNoticeId, LegalNoticeAcceptance
        if (msgId == PlayerData.MsgServerAcceptLegalNotice)
        {
            var legalId = msg.ReadString();
            var acceptance = new LegalAcceptance
            {
                Ticks = msg.ReadInt64(),
                TextHash = msg.ReadInt32(),
                Locale = msg.ReadString(),
                BuildVersion = msg.ReadString(),
                BuildCommit = msg.ReadString()
            };
            // Each player accepts the terms on their own account (accounts\<key>\acceptances.json).
            Dictionary<string, LegalAcceptance> acceptances = Accounts.RecordAcceptance(legalId, acceptance);
            Log("EULA", $"{PlayerSessions.NameOf(msg.SenderConnection)} accepted '{legalId}' (locale={acceptance.Locale}, version={acceptance.BuildVersion})");

            var update = server.CreateMessage();
            PlayerData.WriteClientUpdateLegalNotices(update, acceptances);
            server.SendMessage(update, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
            Log("EULA", $"ClientUpdateLegalNoticeAcceptances sent ({acceptances.Count} acceptance(s))");
            return;
        }

        // ServerRequestCreateCharacter: ushort 54849, string classId, string variantId
        if (msgId == PlayerData.MsgServerRequestCreateCharacter)
        {
            var classId = msg.ReadString();
            var variantId = msg.ReadString();
            if (string.IsNullOrEmpty(classId)) classId = "walker";
            if (string.IsNullOrEmpty(variantId)) variantId = "default";

            // Valid 16-byte GeneralID in base64url: the client converts it to the same text.
            var characterId = PlayerData.ToBase64Url(PlayerData.NewCharacterId());
            Accounts.AddPlane(characterId, classId, variantId);
            // Inventory of the new plane with the class's starting items (Items/InventoryStore).
            InventoryHandler.PlaneCreated(characterId, classId);
            Log("PLANE", $"[{RoleName(role)}] {PlayerSessions.NameOf(msg.SenderConnection)}: ServerRequestCreateCharacter class='{classId}' variant='{variantId}' -> {characterId}");

            var response = server.CreateMessage();
            PlayerData.WriteClientAddPlane(response, Accounts.AccountId, characterId, classId, variantId);
            int responseBytes = response.LengthBytes;
            server.SendMessage(response, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
            Log("PLANE", $"ClientAddPlane sent ({responseBytes} bytes)");
            // Every hangar plane counts toward pilot XP in real time (Pilot.computedXP): checks the rank.
            PilotProgression.PlaneCreated(server, msg.SenderConnection, RoleName(role));
            return;
        }

        // ServerRequestSpawnPlayerPlane: ushort 4940, string characterId, bool isAtHangar, GeneralID targetAccountId.
        // The account is the player the client was going to join ("Join Server" of the friend list or the flock roster): the new
        // plane is born next to them when the server can (Social\SocialHandler.Teleport.cs).
        if (msgId == MsgServerRequestSpawnPlayerPlane)
        {
            var characterId = msg.ReadString();
            byte[]? joining = null;
            try
            {
                msg.ReadBoolean();
                joining = CopperReader.ReadGeneralId(msg);
            }
            catch
            {
                // An older message without the target: the normal spawn point.
            }
            (float X, float Y, float Rotation)? beside = SocialHandler.ArrivalPlace(msg.SenderConnection, joining);
            Log("SPAWN", $"[{RoleName(role)}] {PlayerSessions.NameOf(msg.SenderConnection)}: ServerRequestSpawnPlayerPlane characterId='{characterId}'" +
                (beside != null ? " (next to the player they joined)" : ""));
            SpawnPlane(server, msg.SenderConnection, role, characterId, PlaneSpawn.PlayerPlaneShortId,
                beside == null ? null : (beside.Value.X, beside.Value.Y, beside.Value.Rotation), -1f, "spawn");
            return;
        }

        // ServerRequestChangePlayerPlane: ushort 15839, string characterId. The player chose another plane in the hangar
        // (GameClient.StartHangarPlaneSelection): the client stops the camera and waits for the server to remove the current plane
        // (KillUnitsBatch with KillReason.ChangePlane, which does not count as a death) and create the chosen one in the same place.
        // Without an answer the player stays frozen inside the hangar.
        if (msgId == MsgServerRequestChangePlayerPlane)
        {
            var characterId = msg.ReadString();
            var connection = msg.SenderConnection;
            var chosen = Accounts.GetPlane(characterId);
            if (chosen == null)
                Log("PLANE", $"[{RoleName(role)}] ServerRequestChangePlayerPlane: '{characterId}' is not in the account (the plane spawns as walker)");

            int newShortId = PlaneSpawn.PlayerPlaneShortId;
            (float X, float Y, float Rot)? place = null;
            float delay = -1f;
            if (Game.RemovePlaneForSwap(connection, out int old, out float x, out float y, out float rot))
            {
                var exit = server.CreateMessage();
                PlaneSpawn.WritePlaneSwap(exit, (float)Clock.Elapsed.TotalSeconds - 1f, old, x, y, rot);
                server.SendMessage(exit, connection, NetDeliveryMethod.ReliableOrdered, 0);
                newShortId = PlaneSpawn.NextPlaneShortId(old);
                place = (x, y, rot);
                // The new plane comes in a little later: so the client removes the old one in one frame and creates the new one in another.
                delay = 0.25f;
            }
            // Shared cargo: the cargo copy of the plane chosen on the client may be old.
            InventoryHandler.PlaneChanged(server, connection, characterId);
            Log("PLANE", $"[{RoleName(role)}] ServerRequestChangePlayerPlane -> " +
                (chosen != null ? $"{chosen.Class}/{chosen.Variant} level {chosen.Level}" : characterId) +
                (place != null ? $": current plane left (ChangePlane), the new one enters as unit {newShortId} in the same place" : ": no live plane, spawns at the normal point"));
            SpawnPlane(server, connection, role, characterId, newShortId, place, delay, "swap in the hangar");
            return;
        }

        // Chat commands: network.json decides who can use them; /players lists who is online.
        if (msgId == MsgSlashCommand && HandleNetworkCommand(server, msg, role))
            return;

        // Items: inventory (move, equip, drop, recycle, consume, sort), loot pickup and the Workshop (reforge).
        if (InventoryHandler.Handle(server, msg, msgId, RoleName(role), Clock.Elapsed.TotalSeconds, Log))
            return;

        // Debug and admin messages of the client (debug settings, ServerWatchAI, ServerQuery).
        if (AdminHandler.Handle(server, msg, msgId, RoleName(role), Clock.Elapsed.TotalSeconds, Log))
            return;

        // Pilot progression: skills, loadouts, portrait, buffs, career rewards and skill reports.
        if (ProgressionHandler.Handle(server, msg, msgId, RoleName(role), Clock.Elapsed.TotalSeconds, Log))
            return;

        // All the other messages of the client's catalog: shots, player input, skills, store, RPCs,
        // unit batches... Game keeps the state, answers what needs an answer and logs the rest.
        if (Game.Handle(server, msg, msgId, RoleName(role), Clock.Elapsed.TotalSeconds, Log))
            return;

        // ID that does not exist in the catalog (the client's GeneratedDispatcher): shows the raw bytes.
        var rest = msg.ReadBytes(msg.LengthBytes - 2);
        MessageLog.RecordUnhandled(msgId, rest, msg.LengthBytes,
            $"[{RoleName(role)}]", msg.SenderEndPoint.ToString(), t => Log("UDP", t));
    }
}
