using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

// Answers the "find servers" of the launchers (local network and VPNs such as Radmin and Hamachi).
// The launcher sends a UDP datagram to the search port (network.json "searchPort", 6444 by default), by broadcast on
// every network of its PC and directly to known addresses:
//   "SBREVIVE?1 <nonce>"
// and the server answers to the sender:
//   "SBREVIVE!1 " + JSON (the same fields as GET /revive/info, plus "nonce")
public static class LanDiscovery
{
    public const string RequestPrefix = "SBREVIVE?1";
    public const string ResponsePrefix = "SBREVIVE!1 ";

    // SIO_UDP_CONNRESET: without it, Windows makes the next Receive fail after an answer reaches a closed port.
    private const int SioUdpConnReset = -1744830452;

    private static Thread? thread;

    // info: the JSON fields of the server at the moment of the question (ServerInfo.Build in Program.cs).
    public static void Start(int port, Func<Dictionary<string, object?>> info, Action<string, string> log)
    {
        if (thread != null) return;
        UdpClient socket;
        try
        {
            socket = new UdpClient(AddressFamily.InterNetwork);
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            socket.EnableBroadcast = true;
            try
            {
                socket.Client.IOControl(SioUdpConnReset, new byte[] { 0 }, null);
            }
            catch
            {
                // Not Windows.
            }
        }
        catch (Exception e)
        {
            log("NETWORK", "server search: could not open UDP port " + port + " (" + e.Message +
                "). Launchers will not find this server by themselves; use Connect by IP.");
            return;
        }

        log("NETWORK", "server search: answering on UDP port " + port);
        thread = new Thread(() => Loop(socket, info, log)) { IsBackground = true, Name = "LanDiscovery" };
        thread.Start();
    }

    private static void Loop(UdpClient socket, Func<Dictionary<string, object?>> info, Action<string, string> log)
    {
        var answered = new Dictionary<IPAddress, DateTime>();
        while (true)
        {
            IPEndPoint? received = new IPEndPoint(IPAddress.Any, 0);
            byte[] data;
            try
            {
                data = socket.Receive(ref received);
            }
            catch (SocketException)
            {
                continue;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            if (received == null) continue;
            IPEndPoint from = received;

            try
            {
                if (data.Length > 200) continue;
                string text = Encoding.UTF8.GetString(data);
                if (!text.StartsWith(RequestPrefix, StringComparison.Ordinal)) continue;
                string nonce = text.Length > RequestPrefix.Length ? text.Substring(RequestPrefix.Length).Trim() : "";
                if (nonce.Length > 40) nonce = nonce.Substring(0, 40);

                IPAddress sender = PlayerSessions.Normalize(from.Address);
                bool fromThisPc = PlayerSessions.IsThisPc(sender);
                ServerNetworkSettings settings = ServerNetworkSettingsFile.Current;
                // Hidden servers, and servers closed to other PCs, only answer the launcher of their own PC.
                if (!fromThisPc && (!settings.ShowInServerSearch || !ServerNetworkSettingsFile.OpenToOtherPcs)) continue;

                Dictionary<string, object?> fields = info();
                fields["nonce"] = nonce;
                byte[] answer = Encoding.UTF8.GetBytes(ResponsePrefix + JsonSerializer.Serialize(fields));
                socket.Send(answer, answer.Length, from);

                // One line per address every 5 minutes (a launcher searches every few seconds).
                DateTime now = DateTime.UtcNow;
                if (!answered.TryGetValue(sender, out DateTime last) || now - last > TimeSpan.FromMinutes(5))
                {
                    answered[sender] = now;
                    log("NETWORK", "server search: answered the launcher at " + sender + (fromThisPc ? " (this PC)" : ""));
                }
            }
            catch (Exception e)
            {
                log("NETWORK", "server search: error answering " + from + ": " + e.Message);
            }
        }
    }
}
