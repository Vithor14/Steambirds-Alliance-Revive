using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Lidgren.Network;

// One login of a player (HTTP /ozy/user/login). The game then sends the same session ticket (sessid) in the
// ServerSetSessionTicket of every UDP server it joins (Nexus, tutorial, dungeon), which binds the connection to the
// account and the name of this login.
public sealed class PlayerSession
{
    public string Ticket { get; init; } = "";
    public Accounts.Account Account { get; init; } = null!;
    public string Name { get; set; } = "";

    // Address of this server as the player's game knows it (from the address it used for the login). It goes in
    // every server switch and portal, so the game of another PC does not try to join 127.0.0.1.
    public IPAddress ServerAddress { get; set; } = IPAddress.Loopback;

    public IPAddress? ClientAddress { get; init; }

    // The player's game runs on the PC of the server (loopback or one of the PC's own addresses).
    public bool FromThisPc { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastUsed { get; set; } = DateTime.UtcNow;
}

// Sessions of the logins and the connections bound to them. "gate" is never held while calling other modules, so it
// cannot take part in a deadlock.
public static class PlayerSessions
{
    private static readonly object gate = new object();
    private static readonly Dictionary<string, PlayerSession> byTicket = new Dictionary<string, PlayerSession>(StringComparer.Ordinal);
    private static readonly Dictionary<NetConnection, PlayerSession> byConnection = new Dictionary<NetConnection, PlayerSession>();
    private static HashSet<IPAddress> localAddresses = new HashSet<IPAddress>();
    private static DateTime localAddressesRead = DateTime.MinValue;

    // ------------------------------------------------------------------ logins

    public static PlayerSession Create(Accounts.Account account, string name, IPAddress serverAddress, IPAddress? clientAddress)
    {
        var session = new PlayerSession
        {
            Ticket = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)),
            Account = account,
            Name = name,
            ServerAddress = Normalize(serverAddress),
            ClientAddress = clientAddress == null ? null : Normalize(clientAddress),
            FromThisPc = IsThisPc(clientAddress)
        };
        lock (gate)
        {
            // Old sessions without a connection (the player logged in again, or left a day ago) leave.
            DateTime limit = DateTime.UtcNow.AddHours(-12);
            var bound = new HashSet<PlayerSession>(byConnection.Values);
            foreach (string old in byTicket.Where(p => !bound.Contains(p.Value) && p.Value.LastUsed < limit).Select(p => p.Key).ToList())
                byTicket.Remove(old);
            byTicket[session.Ticket] = session;
        }
        return session;
    }

    public static PlayerSession? ByTicket(string? ticket)
    {
        if (string.IsNullOrEmpty(ticket)) return null;
        lock (gate)
        {
            if (!byTicket.TryGetValue(ticket, out PlayerSession? session)) return null;
            session.LastUsed = DateTime.UtcNow;
            return session;
        }
    }

    // Most recent login from this client address (for a game that lost its session, e.g. after a restart).
    public static PlayerSession? LatestFrom(IPAddress? clientAddress)
    {
        if (clientAddress == null) return null;
        IPAddress address = Normalize(clientAddress);
        lock (gate)
        {
            return byTicket.Values.Where(s => s.ClientAddress != null && s.ClientAddress.Equals(address))
                .OrderByDescending(s => s.LastUsed).FirstOrDefault();
        }
    }

    // ------------------------------------------------------------------ connections

    // ServerSetSessionTicket: binds the UDP connection to the login with this ticket (or, when the ticket is unknown,
    // to the latest login of the same address). how: how it was found, for the log.
    public static PlayerSession? Bind(NetConnection connection, string? ticket, out string how)
    {
        PlayerSession? session = ByTicket(ticket);
        how = "login session";
        if (session == null)
        {
            session = LatestFrom(connection.RemoteEndPoint?.Address);
            how = session != null ? "unknown sessid: last login of the same IP" : "unknown sessid";
        }
        if (session == null) return null;
        lock (gate)
        {
            byConnection[connection] = session;
            session.LastUsed = DateTime.UtcNow;
        }
        return session;
    }

    // Binds a connection to a session created on the spot (guest account of an unknown ticket).
    public static void BindTo(NetConnection connection, PlayerSession session)
    {
        lock (gate)
        {
            byTicket[session.Ticket] = session;
            byConnection[connection] = session;
        }
    }

    public static void Unbind(NetConnection? connection)
    {
        if (connection == null) return;
        lock (gate) byConnection.Remove(connection);
    }

    public static PlayerSession? Of(NetConnection? connection)
    {
        if (connection == null) return null;
        lock (gate) return byConnection.TryGetValue(connection, out PlayerSession? session) ? session : null;
    }

    public static Accounts.Account? AccountOf(NetConnection? connection) => Of(connection)?.Account;

    public static string NameOf(NetConnection? connection) => Of(connection)?.Name ?? "?";

    // ------------------------------------------------------------------ who is online

    // Sessions with at least one open connection, one per account.
    public static List<PlayerSession> Online()
    {
        lock (gate)
        {
            return byConnection
                .Where(p => p.Key.Status != NetConnectionStatus.Disconnected)
                .Select(p => p.Value)
                .GroupBy(s => s.Account)
                .Select(g => g.OrderByDescending(s => s.LastUsed).First())
                .ToList();
        }
    }

    public static int OnlineCount() => Online().Count;

    public static List<string> OnlineNames() => Online().Select(s => s.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    // The account of the only player online (null with nobody or with several).
    public static Accounts.Account? OnlyOnlineAccount()
    {
        List<PlayerSession> online = Online();
        return online.Count == 1 ? online[0].Account : null;
    }

    // Is this account online through another connection than "except"?
    public static bool IsOnline(Accounts.Account account, NetConnection? except)
    {
        lock (gate)
        {
            return byConnection.Any(p => !ReferenceEquals(p.Key, except) && ReferenceEquals(p.Value.Account, account) &&
                                         p.Key.Status != NetConnectionStatus.Disconnected);
        }
    }

    // ------------------------------------------------------------------ addresses

    // The address to write in a server switch or portal for the player of this connection. The server keeps its own
    // endpoints as 127.0.0.1:<port>; for a player on another PC, 127.0.0.1 becomes the address their game used to
    // reach this server.
    public static IPEndPoint AddressFor(NetConnection? connection, IPEndPoint target)
    {
        if (!IPAddress.IsLoopback(target.Address)) return target;
        IPAddress? address = Of(connection)?.ServerAddress;
        if (address == null || IPAddress.IsLoopback(address))
        {
            IPAddress? remote = connection?.RemoteEndPoint?.Address;
            if (remote == null || IsThisPc(remote)) return target;
            address = LocalAddressToward(remote);
        }
        if (IPAddress.IsLoopback(address)) return target;
        return new IPEndPoint(address, target.Port);
    }

    // Local address the PC uses to reach "remote" (the routing table picks it: LAN, Radmin, Hamachi...).
    // Connecting a UDP socket sends nothing.
    public static IPAddress LocalAddressToward(IPAddress remote)
    {
        remote = Normalize(remote);
        if (IPAddress.IsLoopback(remote)) return IPAddress.Loopback;
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(remote, 9);
            if (socket.LocalEndPoint is IPEndPoint local && !local.Address.Equals(IPAddress.Any)) return local.Address;
        }
        catch
        {
        }
        return LocalAddresses().FirstOrDefault(a => !IPAddress.IsLoopback(a)) ?? IPAddress.Loopback;
    }

    // Loopback, or one of the addresses of this PC (a game of the host that joined through its own LAN/VPN address).
    public static bool IsThisPc(IPAddress? address)
    {
        if (address == null) return false;
        address = Normalize(address);
        if (IPAddress.IsLoopback(address)) return true;
        return LocalAddresses().Contains(address);
    }

    // IPv4 addresses of this PC (read again every 30 seconds: VPNs come and go).
    public static List<IPAddress> LocalAddresses()
    {
        lock (gate)
        {
            if (DateTime.UtcNow - localAddressesRead > TimeSpan.FromSeconds(30))
            {
                var found = new HashSet<IPAddress>();
                try
                {
                    foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (nic.OperationalStatus != OperationalStatus.Up) continue;
                        foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
                        {
                            if (info.Address.AddressFamily == AddressFamily.InterNetwork) found.Add(info.Address);
                        }
                    }
                }
                catch
                {
                }
                localAddresses = found;
                localAddressesRead = DateTime.UtcNow;
            }
            return localAddresses.ToList();
        }
    }

    public static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
