using System.Net;
using Lidgren.Network;

// Program (part): sending the client to another server (Nexus, dungeon) and creating the player's plane in the world.

public static partial class Program
{
    // Switches the client to the Nexus. With a live plane ClientSwitchToServerWithPlane goes out: the client stores the
    // characterId and, at the Nexus, requests the spawn of that plane by itself (the same plane of the tutorial).
    static void SendToNexus(NetServer server, NetConnection connection, string reason)
    {
        // Also called by the update loop (tutorial and dungeon exit): the account is the one of this connection's player.
        using Accounts.Scope accountScope = Accounts.UseFor(connection);
        string? characterId = Game.GetPlaneCharacterId(connection);
        Accounts.SavedPlane? plane = Accounts.GetPlane(characterId);
        IPEndPoint destination = PlayerSessions.AddressFor(connection, NexusAddress);

        var swap = server.CreateMessage();
        if (plane != null)
        {
            PlayerData.WriteClientSwitchToServerWithPlane(swap, destination, HostIdNexus, "Nexus", BuildVersion, NexusTags,
                Accounts.AccountId, plane.CharacterId, plane.Class, plane.Variant, plane.Level, plane.Xp);
        }
        else
        {
            PlayerData.WriteClientSwitchToServer(swap, destination, HostIdNexus, "Nexus", BuildVersion, NexusTags);
        }
        int bytes = swap.LengthBytes;
        server.SendMessage(swap, connection, NetDeliveryMethod.ReliableOrdered, 0);
        Log("NEXUS", $"{reason} -> {(plane != null ? "ClientSwitchToServerWithPlane" : "ClientSwitchToServer")} to {destination} " +
            $"({PlayerSessions.NameOf(connection)}, {bytes} bytes{(plane != null ? $", plane {plane.Class}/{plane.Variant} {plane.CharacterId}" : ", no plane")})");
    }

    // Switches the client to the dungeon server, with the plane, for the dungeon "type" (client terrain, e.g. DungeonBomb).
    // The next connection of this account to the dungeon server gets that dungeon (DungeonServer.SetPending).
    static void SendToDungeon(NetServer server, NetConnection connection, string type, string reason)
    {
        using Accounts.Scope accountScope = Accounts.UseFor(connection);
        DungeonServer.SetPending(Accounts.AccountIdText, type);
        string? characterId = Game.GetPlaneCharacterId(connection);
        Accounts.SavedPlane? plane = Accounts.GetPlane(characterId);
        IPEndPoint destination = PlayerSessions.AddressFor(connection, DungeonAddress);

        var swap = server.CreateMessage();
        if (plane != null)
        {
            PlayerData.WriteClientSwitchToServerWithPlane(swap, destination, HostIdDungeon, type, BuildVersion, null,
                Accounts.AccountId, plane.CharacterId, plane.Class, plane.Variant, plane.Level, plane.Xp);
        }
        else
        {
            PlayerData.WriteClientSwitchToServer(swap, destination, HostIdDungeon, type, BuildVersion);
        }
        int bytes = swap.LengthBytes;
        server.SendMessage(swap, connection, NetDeliveryMethod.ReliableOrdered, 0);
        Log("DUNGEON", $"{reason} -> {(plane != null ? "ClientSwitchToServerWithPlane" : "ClientSwitchToServer")} to {destination} " +
            $"({type}, {PlayerSessions.NameOf(connection)}, {bytes} bytes{(plane != null ? $", plane {plane.Class}/{plane.Variant}" : ", no plane")})");
    }

    // Switches the client to the open world server (terrain "Basic"), with the plane.
    static void SendToWorld(NetServer server, NetConnection connection, string reason)
    {
        using Accounts.Scope accountScope = Accounts.UseFor(connection);
        string? characterId = Game.GetPlaneCharacterId(connection);
        Accounts.SavedPlane? plane = Accounts.GetPlane(characterId);
        IPEndPoint destination = PlayerSessions.AddressFor(connection, WorldAddress);
        string name = WorldSettingsFile.Current.Name;

        var swap = server.CreateMessage();
        if (plane != null)
        {
            PlayerData.WriteClientSwitchToServerWithPlane(swap, destination, HostIdWorld, name, BuildVersion, null,
                Accounts.AccountId, plane.CharacterId, plane.Class, plane.Variant, plane.Level, plane.Xp);
        }
        else
        {
            PlayerData.WriteClientSwitchToServer(swap, destination, HostIdWorld, name, BuildVersion);
        }
        int bytes = swap.LengthBytes;
        server.SendMessage(swap, connection, NetDeliveryMethod.ReliableOrdered, 0);
        Log("WORLD", $"{reason} -> {(plane != null ? "ClientSwitchToServerWithPlane" : "ClientSwitchToServer")} to {destination} " +
            $"({PlayerSessions.NameOf(connection)}, {bytes} bytes{(plane != null ? $", plane {plane.Class}/{plane.Variant}" : ", no plane")})");
    }

    // Creates the player's plane in the world (SpawnUnitsBatch) and tells the game state, the progression, the enemies and the
    // tutorial. Used by the normal spawn (ServerRequestSpawnPlayerPlane) and by the plane swap in the hangar.
    // position: null = the server's spawn point (tutorial hangar or the Rebel City eagle).
    // spawnDelay: added to the clock in the spawnTime. -1 = the client creates it at once; positive = the client waits that time
    // (the plane swap uses it so the old plane leaves before the new one enters).
    static void SpawnPlane(NetServer server, NetConnection connection, byte role, string characterId, int shortId,
        (float X, float Y, float Rot)? position, float spawnDelay, string origin)
    {
        using Accounts.Scope accountScope = Accounts.UseFor(connection);
        var idBytes = PlayerData.FromBase64Url(characterId);
        if (idBytes == null)
        {
            Log("SPAWN", $"[{RoleName(role)}] invalid characterId '{characterId}', spawn cancelled ({origin})");
            return;
        }

        var record = Accounts.GetPlane(characterId);
        if (record == null)
        {
            Log("SPAWN", "characterId is not in the account, using walker/default");
            record = new Accounts.SavedPlane { CharacterId = characterId, Class = "walker", Variant = "default" };
        }

        if (!PlayerData.ClassesNetIdShort.TryGetValue(record.Class, out var netClass) ||
            !PlayerData.VariantNetIdShort.TryGetValue(record.Variant, out var netVariant))
        {
            Log("SPAWN", $"class '{record.Class}' or variant '{record.Variant}' has no netIdShort, spawn cancelled ({origin})");
            return;
        }

        var options = server.CreateMessage();
        PlayerData.WritePlaneOptions(options, netClass, netVariant, Accounts.AccountId, idBytes, record.Decoration);

        var peerId = connection.RemoteUniqueIdentifier;
        var currentTime = (float)Clock.Elapsed.TotalSeconds;
        // In the tutorial the plane spawns in the hangar (or at the last checkpoint, after a fall);
        // in the Nexus, on top of the eagle of the Rebel City square. On a plane swap, where the old plane was.
        float spawnX, spawnY, spawnRot;
        if (position != null)
        {
            (spawnX, spawnY, spawnRot) = position.Value;
        }
        else if (role == RoleTutorial)
        {
            Tutorial.SpawnPoint(connection, out spawnX, out spawnY, out spawnRot);
        }
        else if (role == RoleDungeon)
        {
            DungeonServer.SpawnPoint(connection, out spawnX, out spawnY, out spawnRot);
        }
        else if (role == RoleWorld)
        {
            WorldServer.SpawnPoint(connection, out spawnX, out spawnY, out spawnRot);
        }
        else
        {
            Nexus.SpawnPoint(out spawnX, out spawnY, out spawnRot);
        }

        // UnitConfig of the plane: the class's planeName (walkerClassPlane, playerPlane...). It says whether the plane flies in
        // 8 directions or always forward and whether the guns follow the mouse.
        var model = PlayerPlaneTemplates.ForClass(record.Class);

        var response = server.CreateMessage();
        PlaneSpawn.WritePlaneSpawn(response, peerId, currentTime + spawnDelay, spawnX, spawnY, spawnRot, options, shortId,
            model.Hash);
        int spawnBytes = response.LengthBytes;
        server.SendMessage(response, connection, NetDeliveryMethod.ReliableOrdered, 0);
        Log("SPAWN", $"SpawnUnitsBatch sent ({origin}; {PlayerSessions.NameOf(connection)}; {spawnBytes} bytes, options {options.LengthBytes}B, unit {shortId}, " +
            $"class={record.Class}/{netClass}, variant={record.Variant}/{netVariant}, model {model.Describe()}, time {currentTime:F2}s, peer {peerId:X16}, " +
            $"pos ({spawnX:0.#}, {spawnY:0.#}))");

        // The game state (Game) now knows which unit is this player's plane.
        Game.PlaneCreated(connection, RoleName(role), shortId, spawnX, spawnY, Clock.Elapsed.TotalSeconds, characterId, record.Class);
        // Multiplayer: the plane appears for the other players of the server, and theirs for this one.
        PlayerRelay.PlaneSpawned(connection, characterId, idBytes, record.Class, netClass, netVariant, model.Hash,
            Accounts.Pilot().Level, spawnX, spawnY, spawnRot, Clock.Elapsed.TotalSeconds, record.Decoration);
        // Progression: statistics of nearby players (pilot skills) from now on.
        PilotProgression.PlaneSpawned(server, connection, RoleName(role), characterId, Clock.Elapsed.TotalSeconds);

        if (role == RoleTutorial)
        {
            // Tutorial: the script (Tutorial) starts or continues from where it stopped.
            Tutorial.PlaneSpawned(server, connection, Clock.Elapsed.TotalSeconds, Log);
        }
        else if (role == RoleDungeon)
        {
            // Dungeon: the gates spawn and the first room waits for the player (or the progress continues after a fall).
            DungeonServer.PlaneSpawned(server, connection, Clock.Elapsed.TotalSeconds, Log);
        }
        else if (role == RoleWorld)
        {
            // Open world: the carriers and units that already exist, and the name of the region.
            WorldServer.PlaneSpawned(server, connection, Clock.Elapsed.TotalSeconds, Log);
        }
        else
        {
            // Nexus: encounters of enemies.json for "nexus" (if any) and the arrival message.
            Enemies.PlayerPlaneSpawned(server, connection, RoleName(role), Clock.Elapsed.TotalSeconds, Log);
            if (WelcomedInNexus.Add(peerId))
            {
                // Buildings of the Nexus map (once per connection: respawning the plane does not send them again).
                Nexus.SendBuildings(server, connection, Clock.Elapsed.TotalSeconds, Log);
                // Carriers of the dungeons open in dungeons.json (entering takes you to the dungeon server).
                DungeonCatalog.SendNexusPortals(server, connection, Clock.Elapsed.TotalSeconds, Log);
                // The portal to the open world (world.json).
                WorldServer.SendNexusPortal(server, connection, Clock.Elapsed.TotalSeconds, Log);
                WorldObjects.SendChat(server, connection,
                    $"Welcome to {Nexus.Name}! (Map rebuilt from Open Beta videos; the stores open, but buying does not work yet.)");
                ServerNetworkSettings network = ServerNetworkSettingsFile.Current;
                if (!string.IsNullOrWhiteSpace(network.WelcomeMessage)) WorldObjects.SendChat(server, connection, network.WelcomeMessage);
                List<string> online = PlayerSessions.OnlineNames();
                if (online.Count > 1)
                {
                    WorldObjects.SendChat(server, connection, 
                        $"{network.ServerName}: {online.Count} players online ({string.Join(", ", online)}). /players lists them.");
                }
            }
        }
    }
}
