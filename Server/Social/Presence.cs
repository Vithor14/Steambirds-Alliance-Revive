using System.Security.Cryptography;
using Lidgren.Network;

// Who is connected right now and where: the Nexus, the open world, a dungeon or the tutorial. Every UDP server of this process
// reports its players here (Program.Messages.cs after the welcome, Program.Udp.cs when the connection drops), so the friend list,
// the flock roster, the chats and "Join Server" can find a player on any server.
public static class Presence
{
    // ServerRole of the client.
    public const byte RoleNexus = 0;
    public const byte RoleWorld = 1;
    public const byte RoleDungeon = 2;
    public const byte RoleTutorial = 3;

    // GeneralIDType.Proc.
    private const int ProcType = 6;

    public sealed class Here
    {
        public NetServer Server = null!;
        public NetConnection Connection = null!;
        public byte Role;
        public string AccountId = "";
        public byte[] Account = new byte[16];
        public string Name = "";
        public DateTime Since = DateTime.UtcNow;
    }

    private static readonly object gate = new object();
    private static readonly Dictionary<NetConnection, Here> byConnection = new Dictionary<NetConnection, Here>();

    public static void Joined(NetServer server, NetConnection connection, byte role, byte[] account, string name)
    {
        lock (gate)
        {
            byConnection[connection] = new Here
            {
                Server = server,
                Connection = connection,
                Role = role,
                Account = (byte[])account.Clone(),
                AccountId = PlayerData.ToBase64Url(account),
                Name = name
            };
        }
    }

    public static void Left(NetConnection? connection)
    {
        if (connection == null) return;
        lock (gate) byConnection.Remove(connection);
    }

    public static Here? Of(NetConnection? connection)
    {
        if (connection == null) return null;
        lock (gate) return byConnection.TryGetValue(connection, out Here? here) ? here : null;
    }

    // Where this account is (the newest connection that is up). Null when it is offline.
    public static Here? Find(string? accountId)
    {
        if (string.IsNullOrEmpty(accountId)) return null;
        lock (gate)
        {
            Here? best = null;
            foreach (Here here in byConnection.Values)
            {
                if (here.AccountId != accountId || here.Connection.Status != NetConnectionStatus.Connected) continue;
                if (best == null || here.Since > best.Since) best = here;
            }
            return best;
        }
    }

    // Online player by name (ignoring case).
    public static Here? FindByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        lock (gate)
        {
            Here? best = null;
            foreach (Here here in byConnection.Values)
            {
                if (!string.Equals(here.Name, name, StringComparison.OrdinalIgnoreCase) || here.Connection.Status != NetConnectionStatus.Connected) continue;
                if (best == null || here.Since > best.Since) best = here;
            }
            return best;
        }
    }

    // One entry per online account (the newest connection).
    public static List<Here> All()
    {
        lock (gate)
        {
            return byConnection.Values.Where(h => h.Connection.Status == NetConnectionStatus.Connected)
                .GroupBy(h => h.AccountId).Select(g => g.OrderByDescending(h => h.Since).First()).ToList();
        }
    }

    // The name the friend list and the roster show for the server.
    public static string PlaceName(Here here)
    {
        switch (here.Role)
        {
            case RoleNexus: return Nexus.Name;
            case RoleWorld: return WorldSettingsFile.Current.Name;
            case RoleDungeon: return "Dungeon";
            default: return "Training Grounds";
        }
    }

    // Can another player of this host come to this place? The Nexus and the open world (while it has room); dungeons are an
    // instance of their own player and the tutorial is private.
    public static bool IsJoinable(Here here)
    {
        if (!SocialSettingsFile.Current.JoinPlayerServers) return false;
        switch (here.Role)
        {
            case RoleNexus: return true;
            case RoleWorld: return WorldServer.Available(out _) && WorldServer.HasRoom();
            default: return false;
        }
    }

    // GeneralID (type Proc) the roster uses for "the server this member is in". The same place always gives the same id.
    public static byte[]? ProcId(Here? here)
    {
        if (here == null || !IsJoinable(here)) return null;
        string key = here.Role == RoleWorld ? "world" : "nexus";
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("proc|" + key));
        byte[] id = new byte[16];
        Array.Copy(hash, id, 16);
        id[0] = (byte)((ProcType << 2) | (id[0] & 0x03));
        if (id[8] == 0 && id[15] == 0) id[15] = 1;
        return id;
    }
}
