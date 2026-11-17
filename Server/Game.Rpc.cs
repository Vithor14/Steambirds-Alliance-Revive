using System.Globalization;
using System.Text;
using Lidgren.Network;

public static partial class Game
{
    // ------------------------------------------------------------------ store

    // ServerEnterStore(string storeId) -> ClientReceiveStoreContent(string storeId, List<StoreShelve> shelves).
    // The list goes empty, but NOT null (count 1 = zero items): the client does a foreach on it,
    // and a null list (count 0) would give a NullReferenceException in StoreManager.UpdateStoreContent.
    private static void HandleStore(NetServer server, Player j, NetIncomingMessage msg, Action<string, string> log)
    {
        string store = msg.ReadString() ?? "";
        j.Stores++;

        NetOutgoingMessage response = server.CreateMessage();
        response.Write(MsgStoreContent);
        response.Write(store);
        response.WriteVariableUInt32(1u);
        server.SendMessage(response, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);

        log("STORE", j.Role + " store '" + store + "' opened -> ClientReceiveStoreContent sent (" +
            response.LengthBytes + " bytes, no shelf: the store contents are not implemented yet)");
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

    // ------------------------------------------------------------------ player data and friends

    // ServerRequestRemotePlayerData(GeneralID accountId).
    // The client caches this data. While there is no answer, it asks again every 2.5 s; and,
    // when the cached copy is older than 30 s, it asks EVERY frame until a new answer arrives
    // (RemotePlayerCache.GetPlayerData). Without an answer, this reached ~60 requests per second.
    // For the player's own account the server answers with ClientReceiveRemotePlayerData.
    // For other accounts there is no answer: answering "absent" (33421) would make the client ask every frame.
    private static void HandlePlayerData(NetServer server, Player j, NetIncomingMessage msg, int times, double now,
        Action<string, string> log)
    {
        byte[] account = CopperReader.ReadGeneralId(msg);
        string text = CopperReader.GeneralIdText(account);
        bool isSelf = j.Account != null && j.Account.SequenceEqual(account);

        if (!isSelf)
        {
            // Another player online on this server: its name, rank, plane level and equipment. An unknown account gets no
            // answer (an "absent" answer would make the client ask every frame).
            if (PlayerRelay.AnswerPlayerData(server, msg.SenderConnection, account, log)) return;
            if (times == 1 || times % 100 == 0)
            {
                log("MSG", j.Role + " ServerRequestRemotePlayerData x" + times + ": accountId=" + text +
                    " (account not online on this server: no answer)");
            }
            return;
        }

        // Requests that arrive while the answer is still on its way do not need another one.
        if (now - j.LastDataSent < 1.0) return;
        j.LastDataSent = now;
        j.DataSent++;

        int unit = j.PlaneAlive ? j.PlaneShortId : 0;
        Accounts.SavedPlane? plane = j.PlaneAlive ? Accounts.GetPlane(j.CharacterId) : null;
        NetOutgoingMessage response = server.CreateMessage();
        PlayerData.WriteClientReceiveRemotePlayerData(response, unit, j.Account!, j.Name, AdminAccount,
            j.CharacterId, j.Class, j.Server, plane?.Level ?? PlayerData.StartingPlaneLevel, Accounts.Pilot());
        server.SendMessage(response, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);

        if (j.DataSent <= 2 || j.DataSent % 20 == 0)
        {
            log("MSG", j.Role + " player's own data sent x" + j.DataSent + " (ClientReceiveRemotePlayerData, " +
                response.LengthBytes + " bytes, name '" + j.Name + "', unit " + unit +
                (j.Class != null ? ", plane " + j.Class : ", no plane") +
                (AdminAccount ? ", admin" : "") + "; request x" + times + ")");
        }
    }

    // ServerRequestFriendData() -> ClientReceiveFriendData(FriendData).
    // FriendDataSerializer: five lists of GeneralID (friends, mutual, blocked, muted, soulmates),
    // each with an int count. Here all of them are empty.
    private static void HandleFriendList(NetServer server, Player j, NetIncomingMessage msg, Action<string, string> log)
    {
        NetOutgoingMessage response = server.CreateMessage();
        response.Write(MsgFriendData);
        for (int i = 0; i < 5; i++) response.Write(0);
        server.SendMessage(response, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);

        log("MSG", j.Role + " friend list requested -> ClientReceiveFriendData sent (" + response.LengthBytes +
            " bytes, empty list)");
    }

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
        else
        {
            // The store shows in the chat the text of the key returned in the failure.
            string failure = (info.Id == 54296 || info.Id == 54298) ? "purchase_error_unknown" : "not_implemented";
            r.Write(false);
            r.Write(failure);
            response = "failure '" + failure + "'";
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
        log("MSG", j.Role + " client language: '" + language + "', time zone UTC" + (timeZone >= 0 ? "+" : "") + timeZone);
    }

}
