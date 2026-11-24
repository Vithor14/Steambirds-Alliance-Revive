using System.Diagnostics;
using System.Globalization;
using System.Text;
using Lidgren.Network;

// Server statistics: how long the update loop of each game server (Nexus, tutorial, dungeon, world) takes.
//
// The game has a chart for it (ServerTimeChart, shown with the "debug display" option): it draws the records of
// ClientReceiveServerStats `59268` (frame number, frame time, update time, fixed time, graphics time, garbage collection).
// A player who turned it on with /serverstats on gets one record every half second per server: the longest loop of that
// half second. /stats shows the same numbers, the network counters and the memory in the chat.
public static class ServerStats
{
    public const ushort MsgClientReceiveServerStats = 59268;
    private const double Window = 0.5;

    private sealed class RoleStats
    {
        public string Name = "";
        public NetServer Server = null!;
        public double WindowStart = -1;
        public double WindowMax;
        public double WindowSum;
        public int WindowFrames;
        public double LastMax;
        public double LastAvg;
        public double LastLoopsPerSecond;
        public double WorstEver;
        public int FrameNumber;
        public int GcCount;
        public bool GcInWindow;
    }

    private static readonly object gate = new object();
    private static readonly Dictionary<string, RoleStats> roles = new Dictionary<string, RoleStats>();
    private static readonly HashSet<NetConnection> watchers = new HashSet<NetConnection>();
    private static readonly DateTime startedAt = DateTime.Now;

    // Called at the end of each turn of a server's loop. work = seconds the turn took (without the sleep).
    public static void Tick(NetServer server, string role, double now, double work)
    {
        List<NetConnection>? send = null;
        RoleStats r;
        double max = 0, avg = 0;
        int frame = 0;
        bool gc = false;
        lock (gate)
        {
            if (!roles.TryGetValue(role, out RoleStats? found))
            {
                found = new RoleStats { Name = role, Server = server, WindowStart = now, GcCount = GC.CollectionCount(0) };
                roles[role] = found;
            }
            r = found;
            r.WindowMax = Math.Max(r.WindowMax, work);
            r.WindowSum += work;
            r.WindowFrames++;
            r.WorstEver = Math.Max(r.WorstEver, work);
            if (now - r.WindowStart < Window) return;

            int collections = GC.CollectionCount(0);
            gc = collections != r.GcCount;
            r.GcCount = collections;
            r.LastMax = r.WindowMax;
            r.LastAvg = r.WindowFrames > 0 ? r.WindowSum / r.WindowFrames : 0;
            r.LastLoopsPerSecond = r.WindowFrames / Math.Max(0.001, now - r.WindowStart);
            r.FrameNumber++;
            max = r.LastMax;
            avg = r.LastAvg;
            frame = r.FrameNumber;
            r.WindowStart = now;
            r.WindowMax = 0;
            r.WindowSum = 0;
            r.WindowFrames = 0;
            foreach (NetConnection c in watchers)
                if (c.Peer == server && c.Status == NetConnectionStatus.Connected) (send ??= new List<NetConnection>()).Add(c);
        }
        if (send == null) return;
        // The loop sleeps 5 ms per turn: the frame time the chart draws counts it, the update time does not.
        float frameTime = (float)(max + 0.005), update = (float)max;
        foreach (NetConnection c in send)
        {
            NetOutgoingMessage m = server.CreateMessage();
            m.Write(MsgClientReceiveServerStats);
            m.Write(frame);
            m.Write(frameTime);
            m.Write(update);
            m.Write((float)avg);
            m.Write(0f);
            m.Write(gc);
            server.SendMessage(m, c, NetDeliveryMethod.Unreliable, 0);
        }
    }

    public static bool Toggle(NetConnection connection, bool? on)
    {
        lock (gate)
        {
            bool now = on ?? !watchers.Contains(connection);
            if (now) watchers.Add(connection);
            else watchers.Remove(connection);
            return now;
        }
    }

    public static void Disconnected(NetConnection? connection)
    {
        if (connection == null) return;
        lock (gate) watchers.Remove(connection);
    }

    // The text of /stats: one line per game server plus the machine.
    public static List<string> Summary()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        var lines = new List<string>();
        TimeSpan up = DateTime.Now - startedAt;
        Process process = Process.GetCurrentProcess();
        lines.Add($"Server up {(int)up.TotalHours}h{up.Minutes:00}m, {PlayerSessions.OnlineCount()} player(s) online, memory {process.WorkingSet64 / 1048576} MB " +
            $"(managed {GC.GetTotalMemory(false) / 1048576} MB), {process.Threads.Count} threads, {GC.CollectionCount(0)} GC");
        lock (gate)
        {
            foreach (RoleStats r in roles.Values.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                NetPeerStatistics s = r.Server.Statistics;
                lines.Add($"[{r.Name}] {r.Server.ConnectionsCount} connected, loop {(r.LastLoopsPerSecond).ToString("0", c)}/s, " +
                    $"work avg {(r.LastAvg * 1000).ToString("0.0", c)} ms, max {(r.LastMax * 1000).ToString("0.0", c)} ms (worst {(r.WorstEver * 1000).ToString("0", c)} ms), " +
                    $"packets in {s.ReceivedPackets} / out {s.SentPackets}, {s.ReceivedBytes / 1024} KB in / {s.SentBytes / 1024} KB out");
            }
        }
        return lines;
    }
}
