using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SteambirdsLauncher.Core;

// A game server found by the search or by "Connect by IP" (fields of GET /revive/info of the server,
// Server\Program.cs, the /revive/info handler).
public sealed class FoundServer
{
    public string Name { get; set; } = "";

    // How the launcher reaches it: an IP, or the host name typed in "Connect by IP".
    public string Host { get; set; } = "";
    public int HttpPort { get; set; } = ServerNetworkFile.DefaultHttpPort;

    // UDP port of the Nexus of that server (0 = not known; older servers use 1111).
    public int NexusPort { get; set; }

    public int Players { get; set; }
    public int MaxPlayers { get; set; }
    public List<string> PlayerNames { get; set; } = new List<string>();
    public string Build { get; set; } = "";

    // Network it answered on ("Radmin VPN", "Hamachi", "local network"...; "IP" for Connect by IP).
    public string Network { get; set; } = "";
    public int PingMs { get; set; } = -1;

    // The server takes the player name in the address (/n/<hex>/ozy). Older servers do not.
    public bool AcceptsPlayerName { get; set; }

    // Typed in "Connect by IP" (kept in the list between sessions).
    public bool Manual { get; set; }

    // Last time it answered (UTC); DateTime.MinValue = not answering.
    public DateTime SeenAt { get; set; } = DateTime.MinValue;

    public string Address => Host + ":" + HttpPort;
    public string Key => Address.ToLowerInvariant();

    public bool Answering => SeenAt != DateTime.MinValue && DateTime.UtcNow - SeenAt < TimeSpan.FromMinutes(2);

    // OZY_URL for the game. The player name goes in the path (see Server\Network\PlayerNamePath.cs).
    public static string OzyUrl(string host, int port, string? playerName)
    {
        string url = "http://" + host + ":" + port;
        if (!string.IsNullOrWhiteSpace(playerName)) url += "/n/" + Convert.ToHexString(Encoding.UTF8.GetBytes(playerName.Trim()));
        return url + "/ozy";
    }

    internal static FoundServer FromJson(JsonElement root, string host, int fallbackPort)
    {
        var server = new FoundServer { Host = host, HttpPort = fallbackPort };
        if (root.ValueKind != JsonValueKind.Object) return server;
        foreach (JsonProperty p in root.EnumerateObject())
        {
            switch (p.Name)
            {
                case "name" when p.Value.ValueKind == JsonValueKind.String:
                    server.Name = p.Value.GetString() ?? "";
                    break;
                case "httpPort" when p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int port) && port is > 0 and < 65536:
                    server.HttpPort = port;
                    break;
                case "nexusPort" when p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int nexus) && nexus is > 0 and < 65536:
                    server.NexusPort = nexus;
                    break;
                case "players" when p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int players):
                    server.Players = players;
                    break;
                case "maxPlayers" when p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int max):
                    server.MaxPlayers = max;
                    break;
                case "build" when p.Value.ValueKind == JsonValueKind.String:
                    server.Build = p.Value.GetString() ?? "";
                    break;
                case "acceptsPlayerName":
                    server.AcceptsPlayerName = p.Value.ValueKind == JsonValueKind.True;
                    break;
                case "playerNames" when p.Value.ValueKind == JsonValueKind.Array:
                    server.PlayerNames = p.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String)
                        .Select(v => v.GetString() ?? "").Where(n => n.Length > 0).ToList();
                    break;
            }
        }
        if (server.Name.Length == 0) server.Name = host;
        return server;
    }
}

// Finds the servers of the local network and of the VPNs (Radmin VPN, Hamachi...) and checks a typed address.
//
// Search: one UDP datagram "SBREVIVE?1 <nonce>" to the search port of the servers (6444 by default) on every network
// of this PC: by broadcast, to each address of the ARP table (PCs this PC talked to) and, on small networks (/22 or
// smaller, like a home LAN), to every address. VPNs like Radmin and Hamachi are /8: only broadcast and ARP there.
// The servers answer "SBREVIVE!1 {json}" (Server\Network\LanDiscovery.cs).
//
// Connect by IP: GET http://<host>:<port>/revive/info, which also checks that the login port (TCP) is reachable.
public static class ServerBrowser
{
    private const string RequestPrefix = "SBREVIVE?1";
    private const string ResponsePrefix = "SBREVIVE!1 ";
    private const int SioUdpConnReset = -1744830452;

    public sealed class SearchResult
    {
        public List<FoundServer> Servers { get; } = new List<FoundServer>();

        // Answer of a server on this PC (the launcher's or another one), kept apart from the list.
        public FoundServer? ThisPc { get; set; }
        public List<LocalNetwork> Networks { get; set; } = new List<LocalNetwork>();
    }

    // extraHosts: addresses to ask directly besides the networks (servers typed before).
    public static async Task<SearchResult> SearchAsync(int searchPort, IEnumerable<string> extraHosts, int timeoutMs, CancellationToken cancel)
    {
        var result = new SearchResult { Networks = LocalNetworks.List() };
        string nonce = Guid.NewGuid().ToString("N").Substring(0, 12);
        byte[] request = Encoding.UTF8.GetBytes(RequestPrefix + " " + nonce);
        var found = new Dictionary<string, FoundServer>(StringComparer.OrdinalIgnoreCase);
        var gate = new object();
        var clock = Stopwatch.StartNew();

        List<IPAddress> neighbors = LocalNetworks.Neighbors();
        var extras = new List<IPAddress>();
        foreach (string host in extraHosts)
        {
            string name = host.Trim();
            int colon = name.LastIndexOf(':');
            if (colon > 0 && name.IndexOf(':') == colon) name = name.Substring(0, colon);
            if (IPAddress.TryParse(name, out IPAddress? ip) && ip.AddressFamily == AddressFamily.InterNetwork) extras.Add(ip);
        }

        var sockets = new List<(UdpClient Socket, string Network, List<IPEndPoint> Targets)>();
        try
        {
            // This PC (a server of the launcher or one started outside it).
            sockets.Add((OpenSocket(IPAddress.Loopback), "this PC", new List<IPEndPoint> { new IPEndPoint(IPAddress.Loopback, searchPort) }));

            var asked = new HashSet<IPAddress>();
            foreach (LocalNetwork network in result.Networks)
            {
                var targets = new List<IPEndPoint>
                {
                    new IPEndPoint(network.Broadcast, searchPort),
                    new IPEndPoint(IPAddress.Broadcast, searchPort)
                };
                foreach (IPAddress neighbor in neighbors.Where(network.Contains))
                {
                    if (!neighbor.Equals(network.Address) && !neighbor.Equals(network.Broadcast) && asked.Add(neighbor))
                        targets.Add(new IPEndPoint(neighbor, searchPort));
                }
                foreach (IPAddress extra in extras.Where(network.Contains))
                {
                    if (asked.Add(extra)) targets.Add(new IPEndPoint(extra, searchPort));
                }
                // Small networks (home LAN): every address, for routers that drop broadcasts.
                if (network.PrefixLength >= 22 && !network.IsVpn)
                {
                    uint baseAddress = LocalNetwork.ToUInt(network.Address) & LocalNetwork.ToUInt(network.Mask);
                    uint hosts = (1u << (32 - network.PrefixLength)) - 1;
                    for (uint i = 1; i < hosts; i++)
                    {
                        IPAddress candidate = LocalNetwork.FromUInt(baseAddress + i);
                        if (candidate.Equals(network.Address) || !asked.Add(candidate)) continue;
                        targets.Add(new IPEndPoint(candidate, searchPort));
                    }
                }
                try
                {
                    sockets.Add((OpenSocket(network.Address), network.Kind, targets));
                }
                catch
                {
                    // An adapter that does not accept a socket is skipped.
                }
            }

            // Typed servers outside every network of this PC (internet): through the default route.
            var outside = extras.Where(e => !asked.Contains(e) && !result.Networks.Any(n => n.Contains(e))).ToList();
            if (outside.Count > 0)
                sockets.Add((OpenSocket(IPAddress.Any), "IP", outside.Select(e => new IPEndPoint(e, searchPort)).ToList()));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(timeoutMs);

            var receivers = new List<Task>();
            foreach ((UdpClient socket, string network, List<IPEndPoint> targets) in sockets)
            {
                receivers.Add(Receive(socket, network, nonce, clock, found, result, gate, timeout.Token));
                foreach (IPEndPoint target in targets)
                {
                    try
                    {
                        await socket.SendAsync(request, request.Length, target);
                    }
                    catch
                    {
                        // Unreachable network or address: the others go on.
                    }
                }
            }

            try
            {
                await Task.WhenAll(receivers);
            }
            catch
            {
                // Each receiver ends by itself at the timeout.
            }
        }
        finally
        {
            foreach (var entry in sockets) entry.Socket.Dispose();
        }

        lock (gate)
        {
            result.Servers.AddRange(found.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase));
        }
        return result;
    }

    private static UdpClient OpenSocket(IPAddress local)
    {
        var socket = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            socket.Client.IOControl(SioUdpConnReset, new byte[] { 0 }, null);
        }
        catch
        {
        }
        socket.EnableBroadcast = true;
        socket.Client.Bind(new IPEndPoint(local, 0));
        return socket;
    }

    private static async Task Receive(UdpClient socket, string network, string nonce, Stopwatch clock,
        Dictionary<string, FoundServer> found, SearchResult result, object gate, CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await socket.ReceiveAsync(cancel);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            try
            {
                string text = Encoding.UTF8.GetString(received.Buffer);
                if (!text.StartsWith(ResponsePrefix, StringComparison.Ordinal)) continue;
                using JsonDocument document = JsonDocument.Parse(text.Substring(ResponsePrefix.Length));
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("nonce", out JsonElement n) && n.ValueKind == JsonValueKind.String && n.GetString() != nonce) continue;

                IPAddress from = received.RemoteEndPoint.Address;
                if (from.IsIPv4MappedToIPv6) from = from.MapToIPv4();
                FoundServer server = FoundServer.FromJson(root, from.ToString(), ServerNetworkFile.DefaultHttpPort);
                server.Network = network == "this PC" ? L.T("this PC") : network;
                server.PingMs = (int)clock.ElapsedMilliseconds;
                server.SeenAt = DateTime.UtcNow;

                lock (gate)
                {
                    if (IPAddress.IsLoopback(from) || IsThisPc(from, result.Networks))
                    {
                        result.ThisPc ??= server;
                        continue;
                    }
                    if (!found.TryGetValue(server.Key, out FoundServer? known) || known.PingMs > server.PingMs) found[server.Key] = server;
                }
            }
            catch
            {
                // Not a server of the game.
            }
        }
    }

    private static bool IsThisPc(IPAddress address, List<LocalNetwork> networks) => networks.Any(n => n.Address.Equals(address));

    // "Connect by IP": host or host:port (port = login port of the server, 6443 by default).
    public static bool TryParseAddress(string text, out string host, out int port)
    {
        host = "";
        port = ServerNetworkFile.DefaultHttpPort;
        string value = text.Trim();
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) value = value.Substring(7);
        int slash = value.IndexOf('/');
        if (slash >= 0) value = value.Substring(0, slash);
        if (value.Length == 0 || value.Contains(' ')) return false;
        int colon = value.LastIndexOf(':');
        if (colon > 0 && value.IndexOf(':') == colon)
        {
            if (!int.TryParse(value.Substring(colon + 1), out port) || port < 1 || port > 65535) return false;
            value = value.Substring(0, colon);
        }
        if (value.Contains(':')) return false; // IPv6 is not supported by the game's login address
        host = value;
        return true;
    }

    public sealed class ProbeResult
    {
        public FoundServer? Server { get; init; }
        public string Error { get; init; } = "";
    }

    // GET /revive/info of a server. An older server (no /revive/info) answers /ozy/alive: found, without the extras.
    public static async Task<ProbeResult> ProbeAsync(string host, int port, int timeoutMs, CancellationToken cancel)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
        string baseUrl = "http://" + host + ":" + port;
        var clock = Stopwatch.StartNew();
        try
        {
            using HttpResponseMessage response = await http.GetAsync(baseUrl + "/revive/info", cancel);
            string body = await response.Content.ReadAsStringAsync(cancel);
            int ping = (int)clock.ElapsedMilliseconds;
            if (response.IsSuccessStatusCode && body.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                using JsonDocument document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("game", out JsonElement game) && game.GetString() == "steambirds-revive")
                {
                    FoundServer server = FoundServer.FromJson(document.RootElement, host, port);
                    // The address typed is the one that worked: keep its port even if the server reports another.
                    server.HttpPort = port;
                    server.PingMs = ping;
                    server.Network = "IP";
                    server.SeenAt = DateTime.UtcNow;
                    return new ProbeResult { Server = server };
                }
            }

            // Older server of the project: /revive/info answers {} (or nothing); the login answers /ozy/alive.
            using HttpResponseMessage alive = await http.GetAsync(baseUrl + "/ozy/alive", cancel);
            string aliveBody = await alive.Content.ReadAsStringAsync(cancel);
            if (alive.IsSuccessStatusCode && aliveBody.Contains("ok", StringComparison.OrdinalIgnoreCase))
            {
                return new ProbeResult
                {
                    Server = new FoundServer
                    {
                        Name = host, Host = host, HttpPort = port, PingMs = (int)clock.ElapsedMilliseconds, Network = "IP",
                        SeenAt = DateTime.UtcNow, AcceptsPlayerName = false
                    }
                };
            }
            return new ProbeResult { Error = L.F("{0} answered, but it is not a Steambirds server (HTTP {1}).", baseUrl, (int)response.StatusCode) };
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new ProbeResult { Error = L.F("{0} did not answer in {1} seconds.", baseUrl, timeoutMs / 1000) };
        }
        catch (HttpRequestException e)
        {
            return new ProbeResult { Error = L.F("Could not reach {0}: {1}", baseUrl, e.Message) };
        }
        catch (Exception e)
        {
            return new ProbeResult { Error = L.F("Could not reach {0}: {1}", baseUrl, e.Message) };
        }
    }
}
