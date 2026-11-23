using Lidgren.Network;

// Flocks: the Flock Hall's RPCs (30000-30016). Each one reads its arguments, does the change in FlockStore, tells the players it
// touches and answers with what the client expects (GuildInfo, a list, a bool...) or with a fault the client shows as text.
public static partial class SocialHandler
{
    // The message is positioned after the message id; the rpcId is the last thing in it.
    public static void HandleRpc(NetServer server, NetIncomingMessage msg, ushort id, string role, Action<string, string> logTo)
    {
        role = role.Trim('[', ']');
        NetConnection connection = msg.SenderConnection;
        Accounts.Account me = Accounts.Current;
        long now = FlockStore.Now;
        string name = "RPC " + id;

        // The arguments, in the order the client packs them.
        string text1 = "", text2 = "";
        byte[] flockId = Array.Empty<byte>(), userId = Array.Empty<byte>();
        bool flag = false;
        byte number = 0;
        switch (id)
        {
            case RpcCreateFlock:
            case RpcFindByName:
                text1 = msg.ReadString() ?? "";
                break;
            case RpcSetDefaultFlock:
            case RpcGetFlockInfo:
            case RpcLeaveFlock:
            case RpcJoinFlock:
                flockId = CopperReader.ReadGeneralId(msg);
                break;
            case RpcSetFlockNameAndDesc:
                flockId = CopperReader.ReadGeneralId(msg);
                text1 = msg.ReadString() ?? "";
                text2 = msg.ReadString() ?? "";
                number = msg.ReadByte();
                break;
            case RpcResolveInvite:
            case RpcMutePlayer:
            case RpcBanPlayer:
                flockId = CopperReader.ReadGeneralId(msg);
                userId = CopperReader.ReadGeneralId(msg);
                flag = msg.ReadBoolean();
                break;
            case RpcKickPlayer:
                flockId = CopperReader.ReadGeneralId(msg);
                userId = CopperReader.ReadGeneralId(msg);
                break;
            case RpcSetRole:
                flockId = CopperReader.ReadGeneralId(msg);
                userId = CopperReader.ReadGeneralId(msg);
                number = msg.ReadByte();
                break;
        }
        ushort rpc = msg.ReadUInt16();

        string result;
        if (!FlockRules.Enabled)
        {
            SocialWire.ReplyFault(server, connection, rpc, "flocks_off");
            result = "fault flocks_off";
        }
        else
        {
            string flock = PlayerData.ToBase64Url(flockId.Length == 16 ? flockId : new byte[16]);
            string user = PlayerData.ToBase64Url(userId.Length == 16 ? userId : new byte[16]);
            if (flockId.Length != 16 || IsNone(flockId)) flock = "";
            if (userId.Length != 16 || IsNone(userId)) user = "";

            switch (id)
            {
                case RpcCreateFlock: result = CreateFlock(server, connection, me, rpc, text1, role, now); name = "CreateFlock(" + text1 + ")"; break;
                case RpcSetDefaultFlock: result = SetDefaultFlock(server, connection, me, rpc, flock); name = "SetDefaultFlock"; break;
                case RpcGetFlockInfo: result = GetFlockInfo(server, connection, me, rpc, flock, now); name = "GetFlockInfo"; break;
                case RpcGetMyFlocks: result = GetMyFlocks(server, connection, me, rpc); name = "GetMyGuildInfoShorts"; break;
                case RpcSetFlockNameAndDesc: result = SetFlockInfo(server, connection, me, rpc, flock, text1, text2, number, now); name = "SetFlockNameAndDesc"; break;
                case RpcFindSuggestion: result = FindSuggestion(server, connection, me, rpc, now); name = "FindFlockSuggestion"; break;
                case RpcLeaveFlock: result = LeaveFlock(server, connection, me, rpc, flock); name = "LeaveFlock"; break;
                case RpcJoinFlock: result = JoinFlock(server, connection, me, rpc, flock, now); name = "JoinFlock"; break;
                case RpcResolveInvite: result = ResolveInvite(server, connection, me, rpc, flock, user, flag, now); name = "ResolveFlockInvite"; break;
                case RpcFindByName: result = FindByName(server, connection, rpc, text1, now); name = "FindFlocksByName(" + text1 + ")"; break;
                case RpcMutePlayer: result = MutePlayer(server, connection, me, rpc, flock, user, flag); name = "FlockMutePlayer"; break;
                case RpcKickPlayer: result = Remove(server, connection, me, rpc, flock, user, false, true); name = "FlockKickPlayer"; break;
                case RpcBanPlayer: result = Remove(server, connection, me, rpc, flock, user, true, flag); name = "FlockBanPlayer"; break;
                case RpcCanJoinAny: result = CanJoinAny(server, connection, me, rpc); name = "FlockCanJoinAny"; break;
                case RpcSetRole: result = SetRole(server, connection, me, rpc, flock, user, number); name = "SetFlockRole"; break;
                default:
                    SocialWire.ReplyFault(server, connection, rpc, "not_implemented");
                    result = "fault not_implemented";
                    break;
            }
        }
        logTo("FLOCK", "[" + role + "] " + me.Name + " " + name + " #" + rpc + " -> " + result);
    }

    private static bool IsNone(byte[] id) => id.All(b => b == 0);

    // A fault the client shows as text; returns the log text.
    private static string Fault(NetServer server, NetConnection connection, ushort rpc, string fault)
    {
        SocialWire.ReplyFault(server, connection, rpc, fault);
        return "fault " + fault;
    }

    // ------------------------------------------------------------------ the RPCs

    // ServerCreateFlock(string name) -> GuildInfo. The founder token is taken from the plane's cargo.
    private static string CreateFlock(NetServer server, NetConnection connection, Accounts.Account me, ushort rpc, string name, string role, long now)
    {
        if (FlockRules.RequireFounderToken && !FlockFounderToken.Has(connection)) return Fault(server, connection, rpc, "required_item_missing");

        string error = FlockStore.Create(me.IdText, me.Name, name, Game.GetLanguage(connection), Game.GetTimeZone(connection), out Flock? created) ?? "";
        if (error.Length > 0 || created == null) return Fault(server, connection, rpc, error.Length > 0 ? error : "flock_not_found");

        if (FlockRules.RequireFounderToken) FlockFounderToken.Take(server, connection, role);

        SocialStore.Change(me, s =>
        {
            s.DefaultFlock = created.Id;      // the client already shows it as the default flock
            return 0;
        });
        NetOutgoingMessage m = SocialWire.Reply(server, rpc);
        SocialWire.WriteGuildInfo(m, created, now);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
        SendDefaultFlock(server, connection, me);
        return "created '" + created.Name + "'";
    }

    // ServerSetDefaultFlock(GeneralID) -> GeneralID: the flock of the name plate, /f and the friend list.
    private static string SetDefaultFlock(NetServer server, NetConnection connection, Accounts.Account me, ushort rpc, string flockId)
    {
        Flock? flock = FlockStore.Get(flockId);
        if (flock == null || flock.ActiveMember(me.IdText) == null) return Fault(server, connection, rpc, "flock_not_found");
        SocialStore.Change(me, s =>
        {
            s.DefaultFlock = flock.Id;
            return 0;
        });
        NetOutgoingMessage m = SocialWire.Reply(server, rpc);
        SocialWire.WriteIdText(m, flock.Id);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
        SendDefaultFlock(server, connection, me);
        return "default flock '" + flock.Name + "'";
    }

    // ServerGetFlockInfo(GeneralID) -> GuildInfo: the manage page, the roster and the invites. Members and the ones waiting only.
    private static string GetFlockInfo(NetServer server, NetConnection connection, Accounts.Account me, ushort rpc, string flockId, long now)
    {
        Flock? flock = FlockStore.Get(flockId);
        if (flock == null) return Fault(server, connection, rpc, "flock_not_found");
        FlockMember? entry = flock.Member(me.IdText);
        if (entry == null || entry.Banned) return Fault(server, connection, rpc, "no_permission");
        FlockStore.Touch(me.IdText, me.Name);
        NetOutgoingMessage m = SocialWire.Reply(server, rpc);
        SocialWire.WriteGuildInfo(m, flock, now);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
        return flock.Name + " (" + flock.CountMembers + " member(s))";
    }

    // ServerGetMyGuildInfoShorts() -> List<GuildInfoShortClient>.
    private static string GetMyFlocks(NetServer server, NetConnection connection, Accounts.Account me, ushort rpc)
    {
        EnsureDefaultFlock(me);
        DefaultFlock(me, out byte[]? defaultId, out _);
        string defaultText = defaultId == null ? "" : PlayerData.ToBase64Url(defaultId);
        List<Flock> mine = FlockStore.MembershipsOf(me.IdText);
        NetOutgoingMessage m = SocialWire.Reply(server, rpc);
        m.WriteVariableUInt32((uint)mine.Count + 1);            // a list is its count + 1 (0 = null)
        foreach (Flock flock in mine) SocialWire.WriteGuildShort(m, flock, flock.Id == defaultText);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
        return mine.Count + " flock(s)";
    }

    // ServerSetFlockNameAndDesc(flock, name, desc, inviteMode) -> GuildInfo.
    private static string SetFlockInfo(NetServer server, NetConnection connection, Accounts.Account me, ushort rpc, string flockId,
        string name, string desc, byte inviteMode, long now)
    {
        string error = FlockStore.SetInfo(me.IdText, flockId, name, desc, inviteMode, out Flock? after) ?? "";
        if (error.Length > 0 || after == null) return Fault(server, connection, rpc, error.Length > 0 ? error : "flock_not_found");
        NetOutgoingMessage m = SocialWire.Reply(server, rpc);
        SocialWire.WriteGuildInfo(m, after, now);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
        // The members who have it as default flock carry the new name on their plane.
        foreach (FlockMember member in after.Members.Where(x => x.IsMember))
        {
            Accounts.Account? account = Accounts.FindById(member.UserId);
            if (account != null && SocialStore.DefaultFlockOf(account) == after.Id) SendDefaultFlockTo(member.UserId);
        }
        return "'" + after.Name + "' updated";
    }

    // ServerFindFlockSuggestion() -> GuildInfoJoinClient: a flock worth joining.
    private static string FindSuggestion(NetServer server, NetConnection connection, Accounts.Account me, ushort rpc, long now)
    {
        Flock? flock = FlockStore.Suggest(me.IdText, now);
        if (flock == null) return Fault(server, connection, rpc, "no_flock_to_suggest");
        NetOutgoingMessage m = SocialWire.Reply(server, rpc);
        SocialWire.WriteGuildJoin(m, flock, now);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
        return "suggested '" + flock.Name + "'";
    }

    // ServerLeaveFlock(GeneralID) -> bool.
    private static string LeaveFlock(NetServer server, NetConnection connection, Accounts.Account me, ushort rpc, string flockId)
    {
        string error = FlockStore.Leave(me.IdText, flockId, out Flock? after, out FlockMember? promoted) ?? "";
        if (error.Length > 0) return Fault(server, connection, rpc, error);
        AfterLeaving(me, flockId);
        if (after != null)
        {
            TellFlock(after, me.IdText, "flock_player_left", "{0} left the flock.", me.Name);
            if (promoted != null) TellFlock(after, null, "flock_founder_left_promoted_new", "The founder left the flock. {0} got promoted as the new founder.", promoted.Name);
        }
        SocialWire.ReplyBool(server, connection, rpc, true);
        SendDefaultFlock(server, connection, me);
        return "left" + (after == null ? " (the flock ended)" : "");
    }

    // ServerJoinFlock(GeneralID) -> bool: true when the player has to wait for an officer to accept.
    private static string JoinFlock(NetServer server, NetConnection connection, Accounts.Account me, ushort rpc, string flockId, long now)
    {
        if (TooSoonToJoin(me, now)) return Fault(server, connection, rpc, "joined_recently");

        int rank = Accounts.Pilot().Level;
        string error = FlockStore.Join(me.IdText, me.Name, rank, flockId, out bool pending, out Flock? after) ?? "";
        if (error.Length > 0 || after == null) return Fault(server, connection, rpc, error.Length > 0 ? error : "flock_not_found");

        if (pending)
        {
            foreach (FlockMember officer in after.Members.Where(m => m.IsMember && FlockRoles.Has(m.Role, m.Founder, FlockPermission.HandleInvites)))
                Tell(officer.UserId, "flock_player_pending", "{0} wants to join the flock.", me.Name);
        }
        else
        {
            Joined(me, after, now);
            TellFlock(after, me.IdText, "flock_player_joined", "{0} joined the flock.", me.Name);
        }
        SocialWire.ReplyBool(server, connection, rpc, pending);
        if (!pending) SendDefaultFlock(server, connection, me);
        return pending ? "asked to join '" + after.Name + "'" : "joined '" + after.Name + "'";
    }

    // ServerResolveFlockInvite(flock, user, accept) -> bool.
    private static string ResolveInvite(NetServer server, NetConnection connection, Accounts.Account me, ushort rpc, string flockId,
        string userId, bool accept, long now)
    {
        string error = FlockStore.Resolve(me.IdText, flockId, userId, accept, out Flock? after, out string userName) ?? "";
        if (error.Length > 0 || after == null) return Fault(server, connection, rpc, error.Length > 0 ? error : "flock_not_found");
        if (accept)
        {
            Accounts.Account? joined = Accounts.FindById(userId);
            if (joined != null)
            {
                Joined(joined, after, now);
                SendDefaultFlockTo(userId);
            }
            Tell(userId, "flock_join_ok", "You successfully joined the flock.");
            TellFlock(after, userId, "flock_player_joined", "{0} joined the flock.", userName);
        }
        SocialWire.ReplyBool(server, connection, rpc, true);
        return (accept ? "accepted " : "turned down ") + userName;
    }

    // ServerFindFlocksByName(string) -> List<GuildInfoJoinClient>.
    private static string FindByName(NetServer server, NetConnection connection, ushort rpc, string search, long now)
    {
        List<Flock> found = FlockStore.Search(search, 20);
        NetOutgoingMessage m = SocialWire.Reply(server, rpc);
        m.WriteVariableUInt32((uint)found.Count + 1);
        foreach (Flock flock in found) SocialWire.WriteGuildJoin(m, flock, now);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
        return found.Count + " flock(s)";
    }

    // ServerFlockMutePlayer(flock, user, muted) -> bool.
    private static string MutePlayer(NetServer server, NetConnection connection, Accounts.Account me, ushort rpc, string flockId, string userId, bool muted)
    {
        string error = FlockStore.Mute(me.IdText, flockId, userId, muted, out Flock? after, out bool autoBanned) ?? "";
        if (error.Length > 0 || after == null) return Fault(server, connection, rpc, error.Length > 0 ? error : "flock_not_found");
        string name = AccountDirectory.NameOf(userId);
        Tell(me.IdText, muted ? "flock_msg_muted" : "flock_msg_unmuted", muted ? "{0} muted in {1} flock." : "{0} unmuted from {1} flock.", name, after.Name);
        if (autoBanned)
        {
            AfterLeaving(Accounts.FindById(userId), flockId);
            Tell(userId, "flock_got_banned", "You got banned from the flock {0}.", after.Name);
            SendDefaultFlockTo(userId);
        }
        SocialWire.ReplyBool(server, connection, rpc, true);
        return (muted ? "muted " : "unmuted ") + name + (autoBanned ? " (banned: muted by too many members)" : "");
    }

    // ServerFlockKickPlayer(flock, user) and ServerFlockBanPlayer(flock, user, banned) -> bool.
    private static string Remove(NetServer server, NetConnection connection, Accounts.Account me, ushort rpc, string flockId, string userId, bool ban, bool banned)
    {
        string error = FlockStore.Remove(me.IdText, flockId, userId, ban, banned, out Flock? after, out string userName) ?? "";
        if (error.Length > 0 || after == null) return Fault(server, connection, rpc, error.Length > 0 ? error : "flock_not_found");
        bool leaves = !ban || banned;
        if (leaves)
        {
            AfterLeaving(Accounts.FindById(userId), flockId);
            Tell(userId, ban ? "flock_got_banned" : "flock_got_kicked", ban ? "You got banned from the flock {0}." : "You got kicked from the flock {0}.", after.Name);
            SendDefaultFlockTo(userId);
        }
        SocialWire.ReplyBool(server, connection, rpc, true);
        return (ban ? (banned ? "banned " : "unbanned ") : "kicked ") + userName;
    }

    // ServerFlockCanJoinAny() -> bool.
    private static string CanJoinAny(NetServer server, NetConnection connection, Accounts.Account me, ushort rpc)
    {
        bool can = FlockStore.CanJoinAny(me.IdText);
        SocialWire.ReplyBool(server, connection, rpc, can);
        return can ? "true" : "false";
    }

    // ServerSetFlockRole(flock, user, role) -> bool.
    private static string SetRole(NetServer server, NetConnection connection, Accounts.Account me, ushort rpc, string flockId, string userId, byte role)
    {
        string error = FlockStore.SetRole(me.IdText, flockId, userId, role, out Flock? after) ?? "";
        if (error.Length > 0 || after == null) return Fault(server, connection, rpc, error.Length > 0 ? error : "flock_not_found");
        Tell(userId, "flock_player_role", "{0} has a new role in the flock.", AccountDirectory.NameOf(userId));
        SocialWire.ReplyBool(server, connection, rpc, true);
        return AccountDirectory.NameOf(userId) + " is now role " + role;
    }

    // ------------------------------------------------------------------ rules around joining and leaving

    // The game's rule against hopping from flock to flock: after enough joins, one join per RejoinSeconds.
    private static bool TooSoonToJoin(Accounts.Account me, long now)
    {
        int seconds = FlockRules.RejoinSeconds;
        int count = FlockRules.RejoinAfterJoins;
        if (seconds <= 0) return false;
        return SocialStore.Read(me, s => s.FlockJoins.Count >= count && s.FlockJoins.Count > 0 && now - s.FlockJoins[^1] < seconds);
    }

    // The player is a member now: it counts for the rejoin rule and becomes the default flock when they had none.
    private static void Joined(Accounts.Account account, Flock flock, long now)
    {
        SocialStore.Change(account, s =>
        {
            s.FlockJoins.Add(now);
            while (s.FlockJoins.Count > 20) s.FlockJoins.RemoveAt(0);
            if (s.DefaultFlock.Length == 0) s.DefaultFlock = flock.Id;
            return 0;
        });
    }

    // The player is no longer in this flock (left, kicked, banned): the default flock changes if it was this one.
    private static void AfterLeaving(Accounts.Account? account, string flockId)
    {
        if (account == null) return;
        SocialStore.Change(account, s =>
        {
            if (s.DefaultFlock == flockId) s.DefaultFlock = "";
            return 0;
        });
        EnsureDefaultFlock(account);
    }
}

// The founder token that pays for a flock (the game's "create_flock_token" and "test_create_flock"): one of them in the cargo of the
// plane the player flies.
public static class FlockFounderToken
{
    public static bool Has(NetConnection connection)
    {
        string? planeId = Game.GetPlaneCharacterId(connection);
        if (planeId == null) return false;
        List<string> tokens = FlockRules.FounderTokens;
        return InventoryStore.Read(d =>
        {
            InvAccess? access = InventoryStore.Resolve(d, planeId);
            return access != null && access.Cargo.Any(item => item != null && tokens.Contains(item.DefId));
        });
    }

    // Takes one token out of the cargo and tells the client.
    public static bool Take(NetServer server, NetConnection connection, string role)
    {
        string? planeId = Game.GetPlaneCharacterId(connection);
        if (planeId == null) return false;
        List<string> tokens = FlockRules.FounderTokens;
        InvPos taken = InvPos.Invalid;
        string defId = "";
        InventoryStore.Change(d =>
        {
            InvAccess? access = InventoryStore.Resolve(d, planeId);
            if (access == null) return 0;
            for (int i = 0; i < access.Cargo.Count; i++)
            {
                StoredItem? item = access.Cargo[i];
                if (item == null || !tokens.Contains(item.DefId)) continue;
                taken = InvPos.CargoAt(i);
                defId = item.DefId;
                InventoryStore.Set(access, taken, null);
                InventoryStore.NextAudit(d);
                break;
            }
            return 0;
        });
        if (!taken.Valid) return false;
        InvPos position = taken;
        InventoryHandler.Send(server, connection, m => InventoryWire.WriteClientRemoveInventoryItem(m, planeId, position));
        InventoryHandler.Log("ITEM", "[" + role + "] flock founder token " + defId + " taken from the cargo " + position);
        return true;
    }
}
