using System.Globalization;
using System.Text;
using Lidgren.Network;

public static partial class Game
{
    // ------------------------------------------------------------------ store

    // ServerEnterStore(string storeId) -> ClientReceiveStoreContent(string storeId, List<StoreShelve> shelves).
    // The shelves are built in Stores\ (StoreHandler); the purchase (ServerPurchaseInStore) is answered there too.
    private static void HandleStore(NetServer server, Player j, NetIncomingMessage msg, Action<string, string> log)
    {
        string store = msg.ReadString() ?? "";
        j.Stores++;
        StoreHandler.OpenStore(server, msg.SenderConnection, j.Role, store);
    }

    // ------------------------------------------------------------------ pub and workshop (quest givers)

    // QuestGiverBuilding (Pilot's Pub, giverId "tavern") and Workshop ("crafter"): when docking, the client sends
    // ServerEnterQuestGiver and only opens the window when ClientReceiveGiverQuests of that giverId arrives
    // (QuestManager.QuestGiverUpdated). Without an answer the plane stayed stuck inside the building, docked
    // forever. The answer goes with an empty dictionary (Int32 0 = no quest): the window opens, and closing
    // the window releases the plane. ServerRequestGiverQuests (sent when the building appears) gets the same.
    private static void HandleQuestGiver(NetServer server, Player j, NetIncomingMessage msg, bool docked,
        Action<string, string> log)
    {
        string giver = msg.ReadString() ?? "";

        NetOutgoingMessage response = server.CreateMessage();
        response.Write(MsgGiverQuests);
        response.Write(giver);
        response.Write(0);                                   // GiverQuestsSerializer: Int32 count
        server.SendMessage(response, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);

        log("QUEST", j.Role + (docked ? " docked at the " : " asked for the quests of ") + "quest giver '" + giver +
            "' -> ClientReceiveGiverQuests sent (no quest: the pub and workshop missions do not exist yet)");
    }

    // ServerRequestQuestRewards(string questId) -> ClientReceiveQuestRewards(string questId, List<QuestRewardData>).
    // The mission list asks for the rewards of each quest. The server has no quest rewards yet: an empty list is the
    // answer (the client caches it by quest id and stops asking; without it the rewards line of the quest stays loading).
    private static void HandleQuestRewards(NetServer server, Player j, NetIncomingMessage msg, Action<string, string> log)
    {
        string questId = msg.ReadString() ?? "";
        NetOutgoingMessage response = server.CreateMessage();
        response.Write(MsgQuestRewards);
        response.Write(questId);
        response.WriteVariableUInt32(0);                     // List<QuestRewardData>: count 0
        server.SendMessage(response, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
        log("QUEST", j.Role + " asked for the rewards of quest '" + questId + "' -> ClientReceiveQuestRewards sent (no reward: quest rewards do not exist yet)");
    }

    // ------------------------------------------------------------------ player data and friends

    // ServerRequestRemotePlayerData(GeneralID accountId).
    // The client caches this data. While there is no answer, it asks again every 2.5 s; and,
    // when the cached copy is older than 30 s, it asks EVERY frame until a new answer arrives
    // (RemotePlayerCache.GetPlayerData). Without an answer, this reached ~60 requests per second.
    // For the player's own account the server answers with ClientReceiveRemotePlayerData.
    // For other accounts: players on this server (PlayerRelay), players on another server of this host or offline (SocialHandler:
    // the friend list needs them). An account this server does not know gets no answer: answering "absent" (33421) would make
    // the client ask every frame.
    private static void HandlePlayerData(NetServer server, Player j, NetIncomingMessage msg, int times, double now,
        Action<string, string> log)
    {
        byte[] account = CopperReader.ReadGeneralId(msg);
        string text = CopperReader.GeneralIdText(account);
        bool isSelf = j.Account != null && j.Account.SequenceEqual(account);

        if (!isSelf)
        {
            // An empty account id (the client asks about a unit that has no account, such as an NPC): nothing to answer, and no
            // log line either (it came about once per second per player).
            if (account.All(b => b == 0)) return;

            // The client asks again on every frame while its copy is older than 30 s and the answer is still on its way, so
            // several requests arrive before the first answer does (up to ~200 answers per second in the 23/11 test). One
            // answer per second for each (player, account) is enough: the next one only refreshes the same data.
            if (!OtherDataDue(msg.SenderConnection.RemoteUniqueIdentifier, text, now)) return;

            // Another player online on this server: its name, rank, plane level and equipment.
            if (PlayerRelay.AnswerPlayerData(server, msg.SenderConnection, account, log)) return;
            // Somebody on another server or offline (the friend list and the flock roster ask for them).
            if (SocialHandler.AnswerRemoteData(server, msg.SenderConnection, account, log)) return;
            ForgetOtherData(msg.SenderConnection.RemoteUniqueIdentifier, text);
            if (times == 1 || times % 100 == 0)
            {
                log("MSG", j.Role + " ServerRequestRemotePlayerData x" + times + ": accountId=" + text +
                    " (account unknown to this server: no answer)");
            }
            return;
        }

        // Requests that arrive while the answer is still on its way do not need another one.
        if (now - j.LastDataSent < 1.0) return;
        j.LastDataSent = now;
        j.DataSent++;

        int unit = j.PlaneAlive ? j.PlaneShortId : 0;
        Accounts.SavedPlane? plane = j.PlaneAlive ? Accounts.GetPlane(j.CharacterId) : null;
        SocialHandler.DefaultFlock(Accounts.Current, out byte[]? flockId, out string flockName);
        NetOutgoingMessage response = server.CreateMessage();
        PlayerData.WriteClientReceiveRemotePlayerData(response, unit, j.Account!, j.Name, AdminAccount,
            j.CharacterId, j.Class, j.Server, plane?.Level ?? PlayerData.StartingPlaneLevel, Accounts.Pilot(), flockId, flockName);
        server.SendMessage(response, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);

        if (j.DataSent <= 2 || j.DataSent % 20 == 0)
        {
            log("MSG", j.Role + " player's own data sent x" + j.DataSent + " (ClientReceiveRemotePlayerData, " +
                response.LengthBytes + " bytes, name '" + j.Name + "', unit " + unit +
                (j.Class != null ? ", plane " + j.Class : ", no plane") +
                (AdminAccount ? ", admin" : "") + "; request x" + times + ")");
        }
    }

    // Last time the data of another account was answered to a connection, so a burst of identical requests gets one answer.
    private static readonly Dictionary<(long, string), double> otherDataSent = new Dictionary<(long, string), double>();
    private const double OtherDataMinSeconds = 1.0;

    // True when the data of this account may be answered to this connection now (and records it).
    private static bool OtherDataDue(long connection, string account, double now)
    {
        lock (otherDataSent)
        {
            if (otherDataSent.TryGetValue((connection, account), out double last) && now - last < OtherDataMinSeconds) return false;
            if (otherDataSent.Count > 4000) otherDataSent.Clear();     // old connections never leave: start over now and then
            otherDataSent[(connection, account)] = now;
            return true;
        }
    }

    // The account was not answered at all: the next request must not be held back.
    private static void ForgetOtherData(long connection, string account)
    {
        lock (otherDataSent) otherDataSent.Remove((connection, account));
    }

    // The friend list (ServerRequestFriendData, ServerAddFriend, ServerDeleteFriend) is answered in Social\SocialHandler.Friends.cs.

    // ------------------------------------------------------------------ RPC

    // An RPC is a regular message with a ushort (rpcId) right after the arguments.
    // Answer: ushort 52428, ushort rpcId, bool ok (1 bit) and the value, or the failure text.
    // The server does not implement any of them yet; the answer exists so the client does not keep waiting:
    //  - bool                 -> false
    //  - int of the inventory -> -1 (AuditLog.INVALID_ID: "not done"; a failure would arrive as 0 = "done")
    //  - lists                -> empty list
    //  - carrier missions     -> no mission
    //  - the rest             -> failure
    private static void ReplyRpc(NetServer server, NetIncomingMessage msg, MessageCatalog.Message info,
        Player j, Action<string, string> log)
    {
        // Store purchases and the Steam currency packages: Stores\StoreHandler answers them.
        if (StoreHandler.IsStoreRpc(info.Id))
        {
            StoreHandler.HandleRpc(server, msg, info.Id, j.Role);
            j.Rpcs++;
            return;
        }

        // The mission list of the Mission Transport (the mega carrier of the Nexus): dungeons as missions.
        if (MissionTransport.IsMissionRpc(info.Id))
        {
            MissionTransport.HandleRpc(server, msg, info.Id, j.Role, log);
            j.Rpcs++;
            return;
        }

        // The Flock Hall: create, join, leave, roster, invites, roles (Social\SocialHandler.Flocks.cs).
        if (SocialHandler.IsRpc(info.Id))
        {
            SocialHandler.HandleRpc(server, msg, info.Id, j.Role, log);
            j.Rpcs++;
            return;
        }

        string arguments = CopperReader.Describe(msg, info.Args, out bool complete);

        ushort rpcId;
        if (complete && CopperReader.BitsLeft(msg) >= 16)
        {
            rpcId = msg.ReadUInt16();
        }
        else if (msg.LengthBits >= 32)
        {
            // Arguments not decoded to the end: the rpcId is the last ushort of the message.
            msg.Position = msg.LengthBits - 16;
            rpcId = msg.ReadUInt16();
        }
        else
        {
            log("RPC", j.Role + " " + info.Name + " arrived without an rpcId (" + msg.LengthBytes + " bytes): no answer");
            return;
        }

        string result = info.RpcReturn ?? "";
        string response;

        NetOutgoingMessage r = server.CreateMessage();
        r.Write(MsgRpcReply);
        r.Write(rpcId);

        if (result == "bool")
        {
            r.Write(true);
            r.Write(false);
            response = "false";
        }
        else if (result == "int" && IsInventoryRpc(info.Id))
        {
            r.Write(true);
            r.Write(-1);
            response = "-1 (refused: the inventory is not implemented yet)";
        }
        else if (result.StartsWith("List<", StringComparison.Ordinal))
        {
            r.Write(true);
            r.WriteVariableUInt32(1u);
            response = "empty list";
        }
        else if (result == "MissionsAndLookAhead")
        {
            // Carrier missions (ServerListMissions): MissionsAndLookAheadSerializer without a null marker,
            // only the two counts. Before, a 'not_implemented' failure went out, which the game logged as an error and sent the log.
            r.Write(true);
            r.WriteVariableInt32(0);     // Missions
            r.WriteVariableInt32(0);     // Upcoming
            response = "no mission (missions do not exist on the server yet)";
        }
        else if (result == "QuestGoalSourceInfo")
        {
            // ServerRequestQuestGoalSourceInfo: where a quest goal can be done (a mission or a crafting recipe). The client asks for
            // every quest of the mission list. QuestGoalSourceInfo.Empty (HasInfo false) = no hint; the goal still shows.
            // QuestGoalMissionInfoSerializer: byte sourceType, string sourceConfigId, bool isNow, bool hasNextTime.
            r.Write(true);
            r.Write((byte)0);
            r.Write("");
            r.Write(false);
            r.Write(false);
            response = "no source (QuestGoalSourceInfo.Empty)";
        }
        else
        {
            r.Write(false);
            r.Write("not_implemented");
            response = "failure 'not_implemented'";
        }

        server.SendMessage(r, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
        j.Rpcs++;

        string tag = Tags.TryGetValue(info.Id, out var foundOne) ? foundOne : "RPC";
        log(tag, j.Role + " RPC " + info.Name + "(" + arguments + ") #" + rpcId + " -> " + response +
            " [expected return: " + result + "]");
    }

    // ServerInvSwapItems, ServerInvRemoveItem, ServerInvAccumulateItem e ServerInvFavoriteItem.
    private static bool IsInventoryRpc(ushort id)
    {
        return id == 17407 || id == 60901 || id == 60904 || id == 60905;
    }

    // ------------------------------------------------------------------ language

    // ServerSetLocalAndTimezone(string localeId, int utfOffsetInHours)
    private static void HandleLanguage(Player j, NetBuffer msg, Action<string, string> log)
    {
        string language = msg.ReadString() ?? "";
        int timeZone = msg.ReadInt32();
        j.Language = language;
        j.TimeZone = timeZone;
        log("MSG", j.Role + " client language: '" + language + "', time zone UTC" + (timeZone >= 0 ? "+" : "") + timeZone);
    }

}
