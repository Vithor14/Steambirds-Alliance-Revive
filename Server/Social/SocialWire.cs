using System.Globalization;
using Lidgren.Network;

// The messages of the social features as the client reads them: GuildInfo and its members (GuildInfoSerializer), the short and
// join views of a flock, FriendData, the answers of the flock RPCs and the chat messages.
public static class SocialWire
{
    public const ushort MsgRpcReply = 52428;
    public const ushort MsgWhisperChat = 11730;               // ClientWhisperChat
    public const ushort MsgGuildChat = 11734;                 // ClientGuildChat
    public const ushort MsgFriendData = 12100;                // ClientReceiveFriendData
    public const ushort MsgSendMeToPlayerServerFailed = 46627; // ClientSendMeToPlayerServerFailed
    public const ushort MsgUpdateDefaultGuild = 48916;        // ClientUpdatePlayerDataDefaultGuild
    public const ushort MsgAbsenceOfPlayerData = 33421;       // ClientReceiveAbsenceOfRemotePlayerData

    // ------------------------------------------------------------------ small pieces

    // GeneralIDSerializer: two ulongs; null or short = GeneralID.None.
    public static void WriteId(NetBuffer b, byte[]? id16)
    {
        if (id16 == null || id16.Length < 16)
        {
            b.Write(0UL);
            b.Write(0UL);
            return;
        }
        InventoryWire.WriteGeneralId(b, id16);
    }

    public static void WriteIdText(NetBuffer b, string? idText) => WriteId(b, PlayerData.FromBase64Url(idText));

    private static void WriteData(NetBuffer b, List<(string Key, string Value)> map)
    {
        b.WriteVariableInt32(map.Count);
        foreach ((string key, string value) in map)
        {
            b.Write(key);
            b.Write(value);
            b.WriteVariableInt32(0);                   // version of the value
        }
    }

    private static string Float(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ flocks

    // GuildInfo with every member: the Flock Hall's manage, roster and invites pages read this.
    public static void WriteGuildInfo(NetBuffer b, Flock flock, long now)
    {
        WriteIdText(b, flock.Id);
        b.Write(flock.Name);
        WriteData(b, new List<(string, string)>
        {
            ("desc", flock.Desc),
            ("invite_mode", flock.InviteMode.ToString(CultureInfo.InvariantCulture)),
            ("locale", flock.Locale),
            ("timezone_offset", flock.Timezone.ToString(CultureInfo.InvariantCulture)),
            ("created", Math.Clamp(flock.Created, 0, int.MaxValue).ToString(CultureInfo.InvariantCulture)),
            ("health", Float(FlockRules.Health(flock, now)))
        });
        b.WriteVariableInt32(flock.Version);

        b.WriteVariableInt32(flock.Members.Count);
        int inactiveAfter = FlockRules.InactiveSeconds;
        foreach (FlockMember member in flock.Members)
        {
            WriteIdText(b, member.UserId);             // the key of the map
            b.Write(member.Name);
            WriteIdText(b, member.UserId);
            Presence.Here? here = Presence.Find(member.UserId);
            WriteId(b, Presence.ProcId(here));         // ProcId: set = online and joinable
            b.Write(here == null ? "" : Presence.PlaceName(here));
            b.WriteVariableInt32(0);                   // version of the member
            WriteData(b, new List<(string, string)>
            {
                ("role", member.Role.ToString(CultureInfo.InvariantCulture)),
                ("founder", member.Founder ? "true" : "false"),
                ("pending", member.Pending ? "true" : "false"),
                ("banned", member.Banned ? "true" : "false"),
                ("inactive", here == null && now - member.ActiveAt > inactiveAfter ? "true" : "false"),
                ("active_ts", Math.Clamp(here != null ? now : member.ActiveAt, 0, int.MaxValue).ToString(CultureInfo.InvariantCulture)),
                ("joined_at", Math.Clamp(member.JoinedAt, 0, int.MaxValue).ToString(CultureInfo.InvariantCulture)),
                ("muted_by", string.Join(",", member.MutedBy)),
                ("has_muted", string.Join(",", member.HasMuted))
            });
        }
    }

    // GuildInfoJoinClientSerializer: what the join and search pages show about a flock.
    public static void WriteGuildJoin(NetBuffer b, Flock flock, long now)
    {
        FlockMember? founder = flock.FounderMember;
        WriteIdText(b, flock.Id);
        b.Write(flock.Name);
        b.Write(flock.Desc);
        b.Write(founder?.Name ?? "");
        WriteIdText(b, founder?.UserId);
        b.Write((byte)Math.Clamp(flock.InviteMode, 0, 2));
        b.Write(flock.Locale);
        b.Write((sbyte)Math.Clamp(flock.Timezone, -12, 14));
        b.Write(FlockRules.Health(flock, now));
    }

    // GuildInfoShortClientSerializer: the flocks of the player (/myflock and the "switch default flock" page).
    public static void WriteGuildShort(NetBuffer b, Flock flock, bool isDefault)
    {
        WriteIdText(b, flock.Id);
        b.Write(flock.Name);
        b.Write(isDefault);
    }

    // ClientUpdatePlayerDataDefaultGuild(GeneralID guildId, string guildName): the name plate and /f use this flock.
    public static void WriteDefaultGuild(NetBuffer b, string? flockId, string flockName)
    {
        b.Write(MsgUpdateDefaultGuild);
        WriteIdText(b, flockId);
        b.Write(flockName ?? "");
    }

    // ------------------------------------------------------------------ RPC answers

    // Start of a good answer: the caller writes the value after it.
    public static NetOutgoingMessage Reply(NetServer server, ushort rpc)
    {
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgRpcReply);
        m.Write(rpc);
        m.Write(true);
        return m;
    }

    public static void ReplyBool(NetServer server, NetConnection connection, ushort rpc, bool value)
    {
        NetOutgoingMessage m = Reply(server, rpc);
        m.Write(value);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    public static void ReplyFault(NetServer server, NetConnection connection, ushort rpc, string fault)
    {
        NetOutgoingMessage m = server.CreateMessage();
        InventoryWire.WriteRpcFault(m, rpc, fault);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    // ------------------------------------------------------------------ friends

    // ClientReceiveFriendData: friends, mutual friends, blocked, muted and soulmates (FriendDataSerializer: int count, then the ids).
    public static NetOutgoingMessage WriteFriendData(NetServer server, IEnumerable<string> friends, IEnumerable<string> mutuals,
        IEnumerable<string> blocked, IEnumerable<string> muted)
    {
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgFriendData);
        foreach (IEnumerable<string> list in new[] { friends, mutuals, blocked, muted, Enumerable.Empty<string>() })
        {
            List<byte[]> ids = list.Select(PlayerData.FromBase64Url).Where(id => id != null).Select(id => id!).ToList();
            m.Write(ids.Count);
            foreach (byte[] id in ids) WriteId(m, id);
        }
        return m;
    }

    // ClientSendMeToPlayerServerFailed(GeneralID who, string reason): the reason is the key of a text of the client.
    public static void SendJoinFailed(NetServer server, NetConnection connection, byte[] who, string reason)
    {
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgSendMeToPlayerServerFailed);
        WriteId(m, who);
        m.Write(reason);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }
}
