using Lidgren.Network;

// Friends: the list the client shows (ServerRequestFriendData), adding and removing friends, blocks and mutes, and the data of players
// that are not on the server the asking client is in (ServerRequestRemotePlayerData).
public static partial class SocialHandler
{
    // FriendType of the client.
    private const byte FriendFollowing = 1;
    private const byte FriendMuted = 10;
    private const byte FriendBlocked = 100;

    // ClientReceiveFriendData: the lists of this account.
    private static void SendFriendData(NetServer server, NetConnection connection, string role)
    {
        Accounts.Account me = Accounts.Current;
        bool enabled = SocialSettingsFile.Current.FriendsEnabled;
        (List<string> friends, List<string> blocked, List<string> muted) = SocialStore.Read(me, s =>
            (new List<string>(s.Friends), new List<string>(s.Blocked), new List<string>(s.Muted)));
        if (!enabled) friends.Clear();

        // The mutual friends: people who also have me in their list (only those already loaded; the client does not use them yet).
        var mutual = new List<string>();
        foreach (Accounts.Account other in Accounts.LoadedAccounts())
        {
            if (!friends.Contains(other.IdText)) continue;
            if (SocialStore.Read(other, s => s.Friends.Contains(me.IdText))) mutual.Add(other.IdText);
        }

        NetOutgoingMessage m = SocialWire.WriteFriendData(server, friends, mutual, blocked, muted);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
        log("FRIEND", "[" + role + "] " + me.Name + ": friend list sent (" + friends.Count + " friend(s), " + blocked.Count + " blocked, " + muted.Count + " muted)");
    }

    // ServerAddFriend(GeneralID friendId, FriendType relationship): Following = a friend, Muted, Blocked.
    private static void AddFriend(NetServer server, NetIncomingMessage msg, NetConnection connection, string role)
    {
        byte[] target = CopperReader.ReadGeneralId(msg);
        byte relationship = msg.ReadByte();
        Accounts.Account me = Accounts.Current;
        string targetId = PlayerData.ToBase64Url(target);
        if (targetId == me.IdText) return;
        if (relationship == FriendFollowing && !SocialSettingsFile.Current.FriendsEnabled)
        {
            WorldObjects.SendChat(server, connection, "Friends are turned off on this server.");
            return;
        }
        if (AccountDirectory.Find(targetId) == null)
        {
            Warn("[" + role + "] " + me.Name + " tried to add an unknown account " + targetId + " (type " + relationship + ")");
            return;
        }

        string name = AccountDirectory.NameOf(targetId);
        switch (relationship)
        {
            case FriendFollowing:
            {
                int max = SocialSettingsFile.Current.MaxFriends;
                bool added = SocialStore.Change(me, s =>
                {
                    if (s.Friends.Contains(targetId)) return false;
                    if (s.Friends.Count >= max) return false;
                    s.Friends.Add(targetId);
                    s.Blocked.Remove(targetId);
                    return true;
                });
                if (!added && SocialStore.Read(me, s => s.Friends.Count >= max && !s.Friends.Contains(targetId)))
                    WorldObjects.SendChat(server, connection, "Your friend list is full (" + max + " friends).");
                log("FRIEND", "[" + role + "] " + me.Name + " " + (added ? "added" : "already has / could not add") + " friend " + name);
                break;
            }
            case FriendMuted:
                SocialStore.Change(me, s =>
                {
                    if (!s.Muted.Contains(targetId)) s.Muted.Add(targetId);
                    return 0;
                });
                log("FRIEND", "[" + role + "] " + me.Name + " muted " + name);
                break;
            case FriendBlocked:
                SocialStore.Change(me, s =>
                {
                    if (!s.Blocked.Contains(targetId)) s.Blocked.Add(targetId);
                    s.Friends.Remove(targetId);
                    return 0;
                });
                log("FRIEND", "[" + role + "] " + me.Name + " blocked " + name);
                break;
            default:
                log("FRIEND", "[" + role + "] " + me.Name + ": relationship " + relationship + " of " + name + " is not used here");
                break;
        }
    }

    // ServerDeleteFriend(GeneralID friendId, FriendType relationship).
    private static void DeleteFriend(NetIncomingMessage msg, NetConnection connection, string role)
    {
        byte[] target = CopperReader.ReadGeneralId(msg);
        byte relationship = msg.ReadByte();
        Accounts.Account me = Accounts.Current;
        string targetId = PlayerData.ToBase64Url(target);
        string name = AccountDirectory.NameOf(targetId);
        SocialStore.Change(me, s =>
        {
            switch (relationship)
            {
                case FriendFollowing: s.Friends.Remove(targetId); break;
                case FriendMuted: s.Muted.Remove(targetId); break;
                case FriendBlocked: s.Blocked.Remove(targetId); break;
            }
            return 0;
        });
        log("FRIEND", "[" + role + "] " + me.Name + " removed " + name + " (type " + relationship + ")");
    }

    // ------------------------------------------------------------------ chat commands

    // /friends lists the friends and where each one is; /addfriend NAME adds anybody this server knows, even offline or on another
    // server (the game's own /friend only finds players it sees). The game's /unfriend works for every friend on the list.
    public static bool Command(NetServer server, NetConnection? connection, string role, List<string> pieces, double now, Action<string, string> logTo)
    {
        if (connection == null || pieces.Count == 0) return false;
        string command = pieces[0].Trim().ToLowerInvariant();
        if (!command.StartsWith("/")) command = "/" + command;
        if (command != "/friends" && command != "/addfriend") return false;

        Accounts.Account me = Accounts.Current;
        if (!SocialSettingsFile.Current.FriendsEnabled)
        {
            WorldObjects.SendChat(server, connection, "Friends are turned off on this server.");
            return true;
        }

        if (command == "/friends")
        {
            List<string> friends = SocialStore.FriendsOf(me);
            if (friends.Count == 0)
            {
                WorldObjects.SendChat(server, connection, "No friends yet. /addfriend NAME adds one.");
                return true;
            }
            WorldObjects.SendChat(server, connection, friends.Count + " friend(s):");
            foreach (string id in friends.OrderBy(i => Presence.Find(i) == null).ThenBy(i => AccountDirectory.NameOf(i), StringComparer.OrdinalIgnoreCase))
            {
                Presence.Here? there = Presence.Find(id);
                string where = there == null ? "offline" : Presence.PlaceName(there) + (Presence.IsJoinable(there) ? " (/tp NAME goes there)" : "");
                WorldObjects.SendChat(server, connection, "  " + AccountDirectory.NameOf(id) + " - " + where);
            }
            return true;
        }

        string name = string.Join(" ", pieces.Skip(1)).Trim();
        if (name.Length == 0)
        {
            WorldObjects.SendChat(server, connection, "Use /addfriend NAME.");
            return true;
        }
        AccountDirectory.Entry? found = AccountDirectory.FindByName(name);
        if (found == null)
        {
            WorldObjects.SendChat(server, connection, "No player found named " + name + ".");
            return true;
        }
        if (found.IdText == me.IdText)
        {
            WorldObjects.SendChat(server, connection, "Sorry. You can not friend yourself.");
            return true;
        }
        int max = SocialSettingsFile.Current.MaxFriends;
        bool added = SocialStore.Change(me, s =>
        {
            if (s.Friends.Contains(found.IdText) || s.Friends.Count >= max) return false;
            s.Friends.Add(found.IdText);
            s.Blocked.Remove(found.IdText);
            return true;
        });
        string shown = found.Name.Length > 0 ? found.Name : found.Key;
        WorldObjects.SendChat(server, connection, added ? "Added friend " + shown : shown + " is already your friend (or the list is full).");
        if (added)
        {
            logTo("FRIEND", "[" + role + "] " + me.Name + " added friend " + shown + " with /addfriend");
            SendFriendData(server, connection, role);   // the open friend list refreshes
        }
        return true;
    }

    // ------------------------------------------------------------------ data of players on other servers or offline

    // ServerRequestRemotePlayerData for an account that is not in the room of the player who asked: it is online on another
    // server of this host (the data says where, and whether it can be joined) or offline (the last plane it flew). False = this
    // account does not exist here (no answer: "absent" would make the client ask every frame).
    public static bool AnswerRemoteData(NetServer server, NetConnection requester, byte[] account, Action<string, string> log)
    {
        string idText = PlayerData.ToBase64Url(account);
        Presence.Here? there = Presence.Find(idText);
        Accounts.Account? other = Accounts.FindById(idText);
        if (other == null) return false;

        string serverName = there == null ? "" : Presence.PlaceName(there);
        bool joinable = there != null && Presence.IsJoinable(there);
        DefaultFlock(other, out byte[]? flockId, out string flockName);

        NetOutgoingMessage m = server.CreateMessage();
        using (Accounts.Use(other))
        {
            // The plane it flew at the highest level: its class and level fill the friend's card.
            Accounts.SavedPlane? plane = Accounts.Planes().OrderByDescending(p => p.Level).FirstOrDefault();
            PlayerData.WriteClientReceiveRemotePlayerData(m, 0, other.Id, other.Name, false, plane?.CharacterId, plane?.Class, serverName,
                plane?.Level ?? PlayerData.StartingPlaneLevel, Accounts.Pilot(), flockId, flockName, joinable);
        }
        server.SendMessage(m, requester, NetDeliveryMethod.ReliableOrdered, 0);
        log("MSG", "data of '" + other.Name + "' sent (" + (there == null ? "offline" : "on " + serverName + (joinable ? ", joinable" : "")) + ", " + m.LengthBytes + " bytes)");
        return true;
    }

    // ------------------------------------------------------------------ questions the other parts ask

    // Is this account a friend of mine (in my list)?
    public static bool IsFriend(Accounts.Account me, string otherId) => SocialStore.Read(me, s => s.Friends.Contains(otherId));

    public static bool IsBlockedBy(string otherId, Accounts.Account me)
    {
        Accounts.Account? other = Accounts.FindById(otherId);
        return other != null && SocialStore.Read(other, s => s.Blocked.Contains(me.IdText));
    }

    // Friend, or in a flock together: the people a player can join on another server.
    public static bool HasBond(Accounts.Account me, string otherId)
    {
        if (IsFriend(me, otherId)) return true;
        return FlockStore.MembershipsOf(me.IdText).Any(f => f.ActiveMember(otherId) != null);
    }
}
