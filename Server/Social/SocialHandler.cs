using Lidgren.Network;

// Social features of the game: the friend list, the flocks (guilds) and the way to a friend ("Join Server" and the teleport).
//
// What the client does by itself and what the server does here:
//  - Friends: the client keeps the list and tells the server (ServerAddFriend / ServerDeleteFriend, for friends, blocks and mutes);
//    the server saves it per account (accounts\<key>\social.json) and answers ServerRequestFriendData with it. The name, plane and
//    server of each friend come from ServerRequestRemotePlayerData, answered for players who are on this server (PlayerRelay), on
//    another server of this host (Presence) or offline (the saved account).
//  - Flocks: every action of the Flock Hall is an RPC (30000-30016). The flocks live in flocks.json (FlockStore), the default flock
//    of each player in their social.json. /f and /w are chat messages the server forwards (SocialHandler.Chat.cs).
//  - Teleport to a player on the same server (radar, friend list, /tp NAME) is done by the client with the planes it already sees
//    (PlayerRelay shows every plane of the room); the server only hears ServerNotifyCompleteTeleport. To a player on another
//    server (Nexus <-> open world) the client asks ServerSendMeToPlayerServer and the server switches it there
//    (SocialHandler.Teleport.cs); the new plane is born next to the friend.
//
// Parts: SocialHandler.cs (start, entry points), .Friends.cs, .Flocks.cs, .Chat.cs, .Teleport.cs. Data and rules: Social\.
public static partial class SocialHandler
{
    public const ushort MsgRequestFriendData = 12200;
    public const ushort MsgAddFriend = 12201;
    public const ushort MsgDeleteFriend = 12202;
    public const ushort MsgWhisperChat = 11731;           // ServerWhisperChat(string text, string recipientName)
    public const ushort MsgGuildChat = 11733;             // ServerGuildChat(string text)
    public const ushort MsgNotifyCompleteTeleport = 12006;
    public const ushort MsgSendMeToPlayerServer = 43983;

    private const ushort RpcCreateFlock = 30000;
    private const ushort RpcSetDefaultFlock = 30001;
    private const ushort RpcGetFlockInfo = 30002;
    private const ushort RpcGetMyFlocks = 30003;
    private const ushort RpcSetFlockNameAndDesc = 30004;
    private const ushort RpcFindSuggestion = 30005;
    private const ushort RpcLeaveFlock = 30007;
    private const ushort RpcJoinFlock = 30008;
    private const ushort RpcResolveInvite = 30009;
    private const ushort RpcFindByName = 30010;
    private const ushort RpcMutePlayer = 30012;
    private const ushort RpcKickPlayer = 30013;
    private const ushort RpcBanPlayer = 30014;
    private const ushort RpcCanJoinAny = 30015;
    private const ushort RpcSetRole = 30016;

    private static Action<string, string> log = (_, _) => { };

    // Links set by Program.cs: the server switches. (Nexus <-> open world.)
    public static Action<NetServer, NetConnection, string>? SwitchToNexus;
    public static Action<NetServer, NetConnection, string>? SwitchToWorld;

    // Once, when the server starts: settings, saved friends and flocks.
    public static void Start(Action<string, string> logger)
    {
        log = logger;
        SocialSettingsFile.Start(logger);
        SocialStore.Start(logger);
        FlockStore.Start(logger);

        // The client's own teleport to a player of the same server (radar, friend list, /tp) can be turned off for the open world:
        // the ClientMapInfo of its terrain says "local teleport off".
        MapInfo.RegisterMap(WorldServer.Terrain, original =>
        {
            MapInfo.MapInfoData info = original ?? new MapInfo.MapInfoData();
            info.LocalTeleportOff = !SocialSettingsFile.Current.ToPlayersInWorld;
            return info;
        });
    }

    // ------------------------------------------------------------------ entry points (called by Game and Program)

    // The non-RPC messages the social features answer.
    public static bool HandlesMessage(ushort id)
    {
        return id == MsgRequestFriendData || id == MsgAddFriend || id == MsgDeleteFriend || id == MsgWhisperChat ||
               id == MsgGuildChat || id == MsgNotifyCompleteTeleport || id == MsgSendMeToPlayerServer;
    }

    // The message is positioned after the message id. role: "nexus", "world"...
    public static void HandleMessage(NetServer server, NetIncomingMessage msg, ushort id, string role)
    {
        role = role.Trim('[', ']');
        NetConnection connection = msg.SenderConnection;
        switch (id)
        {
            case MsgRequestFriendData:
                SendFriendData(server, connection, role);
                break;
            case MsgAddFriend:
                AddFriend(server, msg, connection, role);
                break;
            case MsgDeleteFriend:
                DeleteFriend(msg, connection, role);
                break;
            case MsgWhisperChat:
                Whisper(server, msg, connection, role);
                break;
            case MsgGuildChat:
                FlockChat(server, msg, connection, role);
                break;
            case MsgNotifyCompleteTeleport:
                CompleteTeleport(msg, connection, role);
                break;
            case MsgSendMeToPlayerServer:
                SendMeToPlayer(server, msg, connection, role);
                break;
        }
    }

    public static bool IsRpc(ushort id)
    {
        switch (id)
        {
            case RpcCreateFlock:
            case RpcSetDefaultFlock:
            case RpcGetFlockInfo:
            case RpcGetMyFlocks:
            case RpcSetFlockNameAndDesc:
            case RpcFindSuggestion:
            case RpcLeaveFlock:
            case RpcJoinFlock:
            case RpcResolveInvite:
            case RpcFindByName:
            case RpcMutePlayer:
            case RpcKickPlayer:
            case RpcBanPlayer:
            case RpcCanJoinAny:
            case RpcSetRole:
                return true;
            default:
                return false;
        }
    }

    // The player is identified on a UDP server (after the welcome): they are online there from now on, and their flock goes on their plane.
    public static void PlayerIdentified(NetServer server, NetConnection connection, byte role, string roleName)
    {
        Accounts.Account account = Accounts.Current;
        Presence.Joined(server, connection, role, account.Id, account.Name);
        AccountDirectory.Seen(account);
        FlockStore.Touch(account.IdText, account.Name);
        if (!FlockRules.Enabled) return;
        EnsureDefaultFlock(account);
        // The welcome carries the id of the default flock; its name comes now. Without a flock there is nothing to say.
        if (DefaultFlock(account, out _, out _)) SendDefaultFlock(server, connection, account);
    }

    // The connection dropped.
    public static void PlayerLeft(NetConnection? connection)
    {
        Presence.Left(connection);
    }

    // ------------------------------------------------------------------ helpers shared by the parts

    // The player's default flock, if they still belong to it. Clears it (and returns false) when the flock is gone or they left it.
    public static bool DefaultFlock(Accounts.Account account, out byte[]? id, out string name)
    {
        id = null;
        name = "";
        if (!FlockRules.Enabled) return false;
        string saved = SocialStore.DefaultFlockOf(account);
        if (saved.Length == 0) return false;
        Flock? flock = FlockStore.Get(saved);
        if (flock == null || flock.ActiveMember(account.IdText) == null)
        {
            SocialStore.Change(account, s =>
            {
                if (s.DefaultFlock == saved) s.DefaultFlock = "";
                return 0;
            });
            return false;
        }
        id = PlayerData.FromBase64Url(flock.Id);
        name = flock.Name;
        return id != null;
    }

    // Makes sure a player who belongs to flocks has one as default.
    private static void EnsureDefaultFlock(Accounts.Account account)
    {
        if (DefaultFlock(account, out _, out _)) return;
        List<Flock> mine = FlockStore.MembershipsOf(account.IdText);
        if (mine.Count == 0) return;
        string first = mine.OrderBy(f => f.Members.First(m => m.UserId == account.IdText).JoinedAt).First().Id;
        SocialStore.Change(account, s =>
        {
            s.DefaultFlock = first;
            return 0;
        });
    }

    // ClientUpdatePlayerDataDefaultGuild: the client's copy of the default flock (name plate, /f, friend list).
    private static void SendDefaultFlock(NetServer server, NetConnection connection, Accounts.Account account)
    {
        if (connection.Status != NetConnectionStatus.Connected) return;
        DefaultFlock(account, out byte[]? id, out string name);
        NetOutgoingMessage m = server.CreateMessage();
        SocialWire.WriteDefaultGuild(m, id == null ? null : PlayerData.ToBase64Url(id), name);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    // The same for a player who may be on another server: whoever is online gets it.
    private static void SendDefaultFlockTo(string accountId)
    {
        Presence.Here? here = Presence.Find(accountId);
        Accounts.Account? account = Accounts.FindById(accountId);
        if (here == null || account == null) return;
        SendDefaultFlock(here.Server, here.Connection, account);
    }

    // A line in the chat of an online player, in their language. Nothing happens when they are offline.
    private static void Tell(string accountId, string key, string fallback, params object[] args)
    {
        Presence.Here? here = Presence.Find(accountId);
        if (here == null) return;
        string text = SocialText.Ui(Game.GetLanguage(here.Connection), key, fallback, args);
        WorldObjects.SendChat(here.Server, here.Connection, text);
    }

    // A line for every member of the flock who is online (except one account), in each one's language.
    private static void TellFlock(Flock flock, string? except, string key, string fallback, params object[] args)
    {
        foreach (FlockMember member in flock.Members.Where(m => m.IsMember && m.UserId != except))
            Tell(member.UserId, key, fallback, args);
    }

    // Read problems of a message never stop the UDP loop; the log says what was wrong.
    private static void Warn(string text) => log("SOCIAL", text);
}
