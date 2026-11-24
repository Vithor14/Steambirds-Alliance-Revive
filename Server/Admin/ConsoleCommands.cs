using System.Collections.Concurrent;
using Lidgren.Network;

// Commands typed in the server console. The launcher (Server tab, Console page) writes one line per command to the
// standard input of the server; the same lines also work when the server runs in a terminal.
//
// Line format:
//   NAME<TAB>/command args   runs the chat command as the player NAME (the name the player has in the game)
//   *<TAB>/command args      runs it once for every player online
//   /command args            (no tab) runs it for the players whose game runs on this PC, the host's own game
//   /players                 lists who is online in the log (needs no player)
//
// The command is the same one the player would type in the chat (Game.RunCommand): progression, items, dungeons, tutorial,
// enemies, stores, social and the admin tools. It ignores network.json "chatCommands": the console is the host's. What
// the command answers goes to that player's chat (as always), and each run is written to the log as [CONSOLE].
//
// Threading: every UDP server (Nexus, tutorial, dungeon, open world) has its own thread and owns its connections, so a
// command is queued for each of them and every thread runs it for the players connected to it (ConsoleCommands.Run, called
// by the loop of each server).
public static class ConsoleCommands
{
    private sealed class Pending
    {
        public string Target = "";          // player name, "*" = everybody
        public bool HostOnly;               // no name: the players on this PC
        public List<string> Pieces = new List<string>();
    }

    private static readonly ConcurrentDictionary<string, ConcurrentQueue<Pending>> queues =
        new ConcurrentDictionary<string, ConcurrentQueue<Pending>>();
    private static Action<string, string> logger = (_, _) => { };
    private static bool started;

    // Starts the thread that reads the lines.
    public static void Start(Action<string, string> log)
    {
        if (started) return;
        started = true;
        logger = log;
        var thread = new Thread(ReadLoop) { IsBackground = true, Name = "console-commands" };
        thread.Start();
    }

    private static void ReadLoop()
    {
        try
        {
            // Read as UTF-8 (the launcher writes UTF-8), so player names with accents arrive as they are.
            using var reader = new StreamReader(Console.OpenStandardInput(), new System.Text.UTF8Encoding(false));
            string? line;
            while ((line = reader.ReadLine()) != null) Submit(line);
        }
        catch (Exception)
        {
            // No console to read (service, closed input): the server just has no console commands.
        }
    }

    // One line of the console. Public so a test can feed it.
    public static void Submit(string line)
    {
        line = line.Trim();
        if (line.Length == 0) return;

        string target = "";
        string text = line;
        int tab = line.IndexOf('\t');
        if (tab >= 0)
        {
            target = line.Substring(0, tab).Trim();
            text = line.Substring(tab + 1).Trim();
        }
        if (!text.StartsWith("/")) text = "/" + text;
        List<string> pieces = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (pieces.Count == 0) return;

        if (pieces[0].Equals("/players", StringComparison.OrdinalIgnoreCase))
        {
            List<PlayerSession> online = PlayerSessions.Online();
            logger("CONSOLE", online.Count + " player(s) online" + (online.Count == 0 ? "" : ": " +
                string.Join(", ", online.Select(s => s.Name + (s.FromThisPc ? " (host)" : "")))));
            return;
        }

        bool everyone = target == "*";
        bool hostOnly = target.Length == 0;
        List<PlayerSession> matches = PlayerSessions.Online().Where(s =>
            everyone || (hostOnly ? s.FromThisPc : s.Name.Equals(target, StringComparison.OrdinalIgnoreCase))).ToList();
        if (matches.Count == 0)
        {
            logger("CONSOLE", hostOnly
                ? "no player of this PC is online: " + text + " was not run (type NAME, a tab and the command to pick a player)"
                : everyone ? "nobody is online: " + text + " was not run" : "no player called \"" + target + "\" is online: " + text + " was not run");
            return;
        }

        var pending = new Pending { Target = target, HostOnly = hostOnly, Pieces = pieces };
        foreach (ConcurrentQueue<Pending> queue in queues.Values) queue.Enqueue(pending);
    }

    // Called by the loop of each UDP server: runs what is waiting for the players connected to this server.
    public static void Run(NetServer server, string role, double now, Action<string, string> log)
    {
        ConcurrentQueue<Pending> queue = queues.GetOrAdd(role, _ => new ConcurrentQueue<Pending>());
        while (queue.TryDequeue(out Pending? pending))
        {
            foreach (NetConnection connection in server.Connections.ToList())
            {
                if (connection.Status != NetConnectionStatus.Connected) continue;
                PlayerSession? session = PlayerSessions.Of(connection);
                if (session == null) continue;
                bool match = pending.Target == "*" || (pending.HostOnly
                    ? session.FromThisPc
                    : session.Name.Equals(pending.Target, StringComparison.OrdinalIgnoreCase));
                if (!match) continue;

                string text = string.Join(" ", pending.Pieces);
                log("CONSOLE", "[" + role + "] " + session.Name + ": " + text);
                MessageLog.RunSafe("console command " + text, () =>
                {
                    using Accounts.Scope accountScope = Accounts.UseFor(connection);
                    Game.RunCommand(server, connection, role, new List<string>(pending.Pieces), now, log);
                }, t => log("ERROR", t));
            }
        }
    }
}
