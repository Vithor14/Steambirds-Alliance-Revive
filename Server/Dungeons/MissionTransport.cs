using Lidgren.Network;

// Mission Transport: the mega carrier at the end of the main ramp of Rebel City (Nexus building prefab 30, "carrier of the
// missions"). Docking there opens the client's mission list (UGUIMissions), which asks the server for it with
// ServerListMissions (12000) and, when the player picks one, sends ServerMissionJoin (12001).
//
// Here the list holds one mission per open dungeon of dungeons.json (the mission ids of the game's own "missions"
// config: "dungeon_bomb", "DQ_KEB_sneezy"...). The client shows the mission name, tier and quest text from its own texts.
// Picking one answers MissionJoinResult.SWITCH_SERVER and sends the player to the dungeon server, like a dungeon carrier.
//
// The host chooses how players get into dungeons in dungeons.json "entryMode": "both" (carriers outside the city and
// this list), "portals" (carriers only: the list stays empty) or "missionTransport" (this list only: no carriers).
//
// Note: the client hides the missions of a higher tier than the plane while there are missions of the plane's own tier
// (MissionConfig.hideBelowPlaneLevel / defaultQuestTierHideBelowPlaneLevel of the game). That is the game's own rule.
public static class MissionTransport
{
    public const ushort MsgServerListMissions = 12000;
    public const ushort MsgServerMissionJoin = 12001;
    private const ushort MsgRpcReply = 52428;

    // MissionJoinResult of the client.
    private const byte JoinSwitchServer = 1;
    private const byte JoinError = 2;

    // GeneralID: high part of the mission ids (never 0: GeneralID.IsValid) and of the squad ids.
    private const ulong MissionHigh = 0x1000000000000001UL;
    private const ulong SquadHigh = 0x1000000000000002UL;

    private sealed class MissionRef
    {
        public string Type = "";
        public string ConfigId = "";
        public ulong Low;
    }

    private static Dictionary<string, string>? configIds;   // dungeon type -> id of its mission in the game's config

    public static bool IsMissionRpc(ushort id) => id == MsgServerListMissions || id == MsgServerMissionJoin;

    // The message is positioned after the message id. tag: "[nexus]".
    public static void HandleRpc(NetServer server, NetIncomingMessage msg, ushort id, string tag, Action<string, string> log)
    {
        NetConnection connection = msg.SenderConnection;
        bool inNexus = tag == "[nexus]";

        if (id == MsgServerListMissions)
        {
            ushort rpc = msg.ReadUInt16();
            List<MissionRef> missions = inNexus && DungeonCatalog.MissionTransportEnabled ? Available() : new List<MissionRef>();

            NetOutgoingMessage r = server.CreateMessage();
            r.Write(MsgRpcReply);
            r.Write(rpc);
            r.Write(true);
            WriteList(r, missions);
            server.SendMessage(r, connection, NetDeliveryMethod.ReliableOrdered, 0);
            log("MISSION", tag + " ServerListMissions -> " + missions.Count + " mission(s)" +
                (missions.Count > 0 ? ": " + string.Join(", ", missions.Select(m => m.ConfigId)) : inNexus
                    ? " (entryMode '" + DungeonCatalog.EntryMode + "')" : " (only the Nexus has the Mission Transport)"));
            return;
        }

        // ServerMissionJoin(GeneralID missionId, GeneralID squadId) -> MissionJoinResult
        msg.ReadUInt64();
        ulong low = msg.ReadUInt64();
        msg.ReadUInt64();
        msg.ReadUInt64();
        ushort joinRpc = msg.ReadUInt16();

        MissionRef? mission = inNexus && DungeonCatalog.MissionTransportEnabled ? Available().FirstOrDefault(m => m.Low == low) : null;
        DungeonCatalog.Entry? entry = mission == null ? null : DungeonCatalog.Find(mission.Type);
        if (entry != null && !entry.IsOpen(DateTime.Now, out _)) entry = null;

        NetOutgoingMessage answer = server.CreateMessage();
        answer.Write(MsgRpcReply);
        answer.Write(joinRpc);
        answer.Write(true);
        answer.Write(entry != null ? JoinSwitchServer : JoinError);
        server.SendMessage(answer, connection, NetDeliveryMethod.ReliableOrdered, 0);

        if (entry == null)
        {
            log("MISSION", tag + " ServerMissionJoin(" + low.ToString("x") + ") -> ERROR (unknown or closed mission)");
            return;
        }
        log("MISSION", tag + " ServerMissionJoin(" + mission!.ConfigId + ") -> SWITCH_SERVER: " + entry.Label);
        DungeonServer.SwitchToDungeon?.Invoke(server, connection, entry.Type, "[nexus] Mission Transport (" + entry.Label + ")");
    }

    // Open dungeons that have a mission in the game's config.
    private static List<MissionRef> Available()
    {
        Dictionary<string, string> ids = ConfigIds();
        var list = new List<MissionRef>();
        DateTime now = DateTime.Now;
        foreach (DungeonCatalog.Entry d in DungeonCatalog.All())
        {
            if (!d.IsOpen(now, out _)) continue;
            if (!ids.TryGetValue(d.Type, out string? configId)) continue;
            list.Add(new MissionRef { Type = d.Type, ConfigId = configId, Low = Hash(d.Type) });
        }
        return list;
    }

    // MissionsAndLookAheadSerializer: count, each MissionData (MissionDataSerializer), count of the upcoming ones (none).
    private static void WriteList(NetBuffer b, List<MissionRef> missions)
    {
        long start = DateTime.UtcNow.AddMinutes(-1).Ticks;
        long end = DateTime.UtcNow.AddDays(1).Ticks;

        b.WriteVariableInt32(missions.Count);
        foreach (MissionRef m in missions)
        {
            b.Write(MissionHigh);                  // Id
            b.Write(m.Low);
            b.Write(SquadHigh);                    // SquadId
            b.Write(m.Low);
            b.Write(m.ConfigId);
            b.Write(start);                        // StartTime
            b.WriteVariableInt32(1);               // QuestInstanceSeed
            b.Write(end);                          // QuestEndTime
            b.Write(end);                          // MissionEndTime
            b.Write(end);                          // SquadJoinEndTime
            b.WriteVariableInt32(0);               // PeopleJoined
            b.WriteVariableInt32(0);               // PeopleFromFlockJoined
            b.WriteVariableInt32(0);               // FriendsJoined
            b.Write(false);                        // IsHighlighted
            b.WriteVariableInt32(0);               // ListSortPriority (x1024)
            b.WriteVariableInt32(0);               // Progress (x1024)
        }
        b.WriteVariableInt32(0);                   // Upcoming
    }

    // dungeon terrain -> id of its mission in the "missions" file of the game's configBinary (the first one of the terrain).
    private static Dictionary<string, string> ConfigIds()
    {
        if (configIds != null) return configIds;
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ConfigNode mission in ConfigBinary.Get("missions")["missions"].Items)
        {
            if (!string.Equals(mission["serverRole"].AsString(""), "Dungeon", StringComparison.OrdinalIgnoreCase)) continue;
            string scenario = mission["scenario"].AsString("");
            string id = mission["id"].AsString("");
            if (scenario.Length == 0 || id.Length == 0 || found.ContainsKey(scenario)) continue;
            found[scenario] = id;
        }
        if (ConfigBinary.Loaded) configIds = found;   // not read yet: try again next time
        return found;
    }

    // Stable, never 0: the same dungeon always gets the same mission id (the client matches the list by id).
    private static ulong Hash(string text)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in text.ToLowerInvariant())
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return h == 0 ? 1UL : h;
    }
}
