using System.Globalization;
using Lidgren.Network;

// Debug and admin features of the game: the messages the client's debug tools send (ServerSet...PlayerDebugSetting, ServerWatchAI,
// ServerQuery) and the chat commands that use the other direction (/debug, /god, /sound, /screenshot, /stats...).
// The chat commands already go through network.json "chatCommands" (Program.HandleNetworkCommand); the ones that touch another
// player or the host's accounts (/screenshot NAME, /soundall, /passwd) are for the game of the host's PC only.
public static class AdminHandler
{
    private const ushort MsgServerQuery = 45861;
    private const ushort MsgServerWatchAI = 19540;
    private const ushort MsgServerSetFloatPlayerDebugSetting = 49275;
    private const ushort MsgServerSetStringPlayerDebugSetting = 15630;
    private const ushort MsgServerClearPlayerDebugSetting = 30792;
    private const ushort MsgClientReceiveAIDebug = 33683;
    private const ushort MsgClientCaptureScreen = 23010;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Same rule as the chat commands: network.json chatCommands.
    private static bool Allowed(NetConnection connection)
    {
        string who = ServerNetworkSettingsFile.NormalizedCommands(ServerNetworkSettingsFile.Current.ChatCommands);
        PlayerSession? session = PlayerSessions.Of(connection);
        return who == "everyone" || (who == "host" && session != null && session.FromThisPc);
    }

    private static bool IsHost(NetConnection connection) => PlayerSessions.Of(connection)?.FromThisPc == true;

    public static void Disconnected(NetConnection? connection)
    {
        ServerStats.Disconnected(connection);
        InventoryAudit.Disconnected(connection);
        DebugSettings.Disconnected(connection);
    }

    // ------------------------------------------------------------------ client messages

    public static bool Handle(NetServer server, NetIncomingMessage msg, ushort id, string role, double now, Action<string, string> log)
    {
        NetConnection connection = msg.SenderConnection;
        string who = PlayerSessions.NameOf(connection);
        switch (id)
        {
            case MsgServerSetFloatPlayerDebugSetting:
            {
                int index = msg.ReadInt32();
                float value = msg.ReadSingle();
                if (!Allowed(connection)) { log("ADMIN", "[" + role + "] " + who + " tried to set debug setting " + DebugSettings.NameOf(index) + ": chat commands are off for this player"); return true; }
                DebugSettings.SetFloat(server, connection, index, value);
                log("ADMIN", "[" + role + "] " + who + ": debug setting " + DebugSettings.NameOf(index) + " = " + value.ToString("0.###", Inv));
                return true;
            }
            case MsgServerSetStringPlayerDebugSetting:
            {
                int index = msg.ReadInt32();
                string value = msg.ReadString() ?? "";
                if (!Allowed(connection)) { log("ADMIN", "[" + role + "] " + who + " tried to set debug setting " + DebugSettings.NameOf(index) + ": chat commands are off for this player"); return true; }
                DebugSettings.SetString(server, connection, index, value);
                log("ADMIN", "[" + role + "] " + who + ": debug setting " + DebugSettings.NameOf(index) + " = \"" + value + "\"");
                return true;
            }
            case MsgServerClearPlayerDebugSetting:
            {
                int index = msg.ReadByte();
                DebugSettings.Clear(server, connection, index);
                log("ADMIN", "[" + role + "] " + who + ": debug setting " + DebugSettings.NameOf(index) + " cleared");
                return true;
            }
            case MsgServerWatchAI:
            {
                int shortId = msg.ReadInt32();
                SendAiDebug(server, connection, shortId);
                return true;
            }
            case MsgServerQuery:
            {
                var ids = new List<int>();
                for (int i = 0; i < 4; i++)
                {
                    int unit = msg.ReadInt32();
                    if (unit != 0 && unit != int.MaxValue) ids.Add(unit);
                }
                int sent = Enemies.Resend(connection, ids, now);
                int count = Count(role + " query");
                if (count <= 5 || count % 100 == 0)
                    log("QUERY", "[" + role + "] " + who + " asked about unit(s) " + string.Join(", ", ids) + ": " + sent + " sent again" + (sent == 0 ? " (not enemies of this server)" : ""));
                return true;
            }
        }
        return false;
    }

    private static readonly Dictionary<string, int> counters = new Dictionary<string, int>();

    private static int Count(string key)
    {
        lock (counters)
        {
            counters.TryGetValue(key, out int value);
            counters[key] = ++value;
            return value;
        }
    }

    // ClientReceiveAIDebug `33683`: the text about one unit. Returns the text (null when the server has no such enemy).
    private static string? SendAiDebug(NetServer server, NetConnection connection, int shortId)
    {
        string? text = Enemies.DebugText(connection, shortId);
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgClientReceiveAIDebug);
        m.Write(shortId);
        m.Write(text ?? "unit " + shortId + ": no enemy of this server");
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
        return text;
    }

    // ------------------------------------------------------------------ chat commands

    public static bool Command(NetServer server, NetConnection? connection, string role, List<string> pieces, double now, Action<string, string> log)
    {
        if (connection == null || pieces.Count == 0) return false;
        string command = pieces[0].Trim().ToLowerInvariant();
        if (!command.StartsWith("/")) command = "/" + command;
        string argument = pieces.Count > 1 ? string.Join(" ", pieces.Skip(1)).Trim() : "";
        string who = PlayerSessions.NameOf(connection);

        switch (command)
        {
            case "/admin":
                Chat(server, connection, "Admin: /debug (settings of your plane), /god, /sound, /soundall (host), /screenshot [player] (host for others), " +
                    "/stats, /serverstats on|off, /audit, /watchai [id], /passwd NAME PASSWORD (host).");
                return true;

            case "/sound":
            {
                if (argument.Length == 0) { Chat(server, connection, "Use /sound NAME or /sound N. Known: " + Sounds.List()); return true; }
                string name = Sounds.Resolve(argument);
                Sounds.Play(server, connection, name);
                Chat(server, connection, "Playing \"" + name + "\".");
                return true;
            }

            case "/soundall":
            {
                if (!IsHost(connection)) { Chat(server, connection, "Only the game of the host's PC can play a sound for everybody."); return true; }
                if (argument.Length == 0) { Chat(server, connection, "Use /soundall NAME or /soundall N. Known: " + Sounds.List()); return true; }
                string name = Sounds.Resolve(argument);
                int count = 0;
                foreach (NetConnection other in server.Connections)
                {
                    Sounds.Play(server, other, name);
                    count++;
                }
                Chat(server, connection, "\"" + name + "\" played for " + count + " player(s) of this server.");
                return true;
            }

            case "/screenshot":
            {
                NetConnection? target = connection;
                if (argument.Length > 0)
                {
                    if (!IsHost(connection)) { Chat(server, connection, "Only the game of the host's PC can take another player's screenshot."); return true; }
                    target = server.Connections.FirstOrDefault(c => string.Equals(PlayerSessions.NameOf(c), argument, StringComparison.OrdinalIgnoreCase));
                    if (target == null) { Chat(server, connection, "No player called \"" + argument + "\" on this part of the server."); return true; }
                }
                PlayerSession? session = PlayerSessions.Of(target);
                if (session == null) { Chat(server, connection, "That player has no session."); return true; }
                var (id, uploadUrl, finishUrl) = Program.NewCaptureSlot(session.ServerAddress, "screenshot", session.Name);
                NetOutgoingMessage m = server.CreateMessage();
                m.Write(MsgClientCaptureScreen);
                m.Write("screenshot");
                m.Write(id);
                m.Write(uploadUrl);
                m.Write(finishUrl);
                server.SendMessage(m, target, NetDeliveryMethod.ReliableOrdered, 0);
                log("ADMIN", "[" + role + "] " + who + " asked for a screenshot of " + session.Name + " (" + id + ")");
                Chat(server, connection, "Screenshot of " + session.Name + " requested; it is saved in client-uploads\\ (" + id + ".jpg) in a few seconds.");
                return true;
            }

            case "/stats":
                foreach (string line in ServerStats.Summary()) Chat(server, connection, line);
                return true;

            case "/serverstats":
            {
                bool? on = argument.Equals("on", StringComparison.OrdinalIgnoreCase) ? true : argument.Equals("off", StringComparison.OrdinalIgnoreCase) ? false : null;
                bool now2 = ServerStats.Toggle(connection, on);
                Chat(server, connection, "Server time chart " + (now2 ? "on: turn on Options > debug display to see it." : "off."));
                return true;
            }

            case "/audit":
                Chat(server, connection, InventoryAudit.Summary(connection));
                return true;

            case "/watchai":
            {
                int shortId;
                if (argument.Length > 0 && int.TryParse(argument, NumberStyles.Integer, Inv, out int typed)) shortId = typed;
                else if (Game.GetPlane(connection.RemoteUniqueIdentifier, out float x, out float y, out _, out _)) shortId = Enemies.NearestId(connection, x, y);
                else shortId = 0;
                if (shortId == 0) { Chat(server, connection, "No enemy found. Use /watchai ID."); return true; }
                string? text = SendAiDebug(server, connection, shortId);
                Chat(server, connection, text == null ? "Unit " + shortId + " is not an enemy of this server." : text.Replace('\n', ' '));
                return true;
            }

            case "/god":
            {
                bool on = !argument.Equals("off", StringComparison.OrdinalIgnoreCase);
                if (on) DebugSettings.SetFloat(server, connection, 2, 0f);
                else DebugSettings.Clear(server, connection, 2);
                Chat(server, connection, on ? "God mode on: nothing hurts you (/god off to end it)." : "God mode off.");
                log("ADMIN", "[" + role + "] " + who + " /god " + (on ? "on" : "off"));
                return true;
            }

            case "/debug":
                return DebugCommand(server, connection, role, argument, who, log);

            case "/passwd":
            {
                if (!IsHost(connection)) { Chat(server, connection, "Only the game of the host's PC can change passwords."); return true; }
                string[] parts = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length < 2) { Chat(server, connection, "Use /passwd NAME NEW_PASSWORD (accounts with username and password, network.json passwordLogin)."); return true; }
                string? key = AccountLogins.KeyOf(parts[0]);
                string? problem = AccountLogins.CheckPassword(parts[1]);
                if (key == null) { Chat(server, connection, "There is no account with the username \"" + parts[0] + "\"."); return true; }
                if (problem != null) { Chat(server, connection, "The password must have " + AccountLogins.MinPassword + " to " + AccountLogins.MaxPassword + " characters."); return true; }
                AccountLogins.SetPassword(key, parts[1]);
                Chat(server, connection, "Password of " + parts[0] + " changed.");
                log("ADMIN", "[" + role + "] " + who + " changed the password of " + parts[0] + " (account " + key + ")");
                return true;
            }
        }
        return false;
    }

    private static bool DebugCommand(NetServer server, NetConnection connection, string role, string argument, string who, Action<string, string> log)
    {
        string[] parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            Chat(server, connection, "Debug settings of your plane: " + DebugSettings.Describe(connection) + ".");
            Chat(server, connection, "Use /debug NAME VALUE, /debug clear [NAME], /debug latency on|off. Names: " +
                string.Join(", ", DebugSettings.All.Select(s => s.Name)));
            return true;
        }

        if (parts[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length == 1)
            {
                int count = DebugSettings.ClearAll(server, connection);
                Chat(server, connection, count + " debug setting(s) cleared.");
                return true;
            }
            var one = DebugSettings.Find(parts[1]);
            if (one == null) { Chat(server, connection, "No setting called \"" + parts[1] + "\"."); return true; }
            DebugSettings.Clear(server, connection, one.Value.Index);
            Chat(server, connection, one.Value.Name + " cleared.");
            return true;
        }

        if (parts[0].Equals("latency", StringComparison.OrdinalIgnoreCase))
        {
            bool on = parts.Length < 2 || !parts[1].Equals("off", StringComparison.OrdinalIgnoreCase);
            DebugSettings.SimulateLatency(server, connection, on);
            Chat(server, connection, "Latency simulation " + (on ? "on" : "off") + " (sent to the game).");
            return true;
        }

        var setting = DebugSettings.Find(parts[0]);
        if (setting == null) { Chat(server, connection, "No setting called \"" + parts[0] + "\". Use /debug to list them."); return true; }
        if (parts.Length < 2) { Chat(server, connection, setting.Value.Name + ": " + setting.Value.Help + "."); return true; }

        if (setting.Value.Switch)
        {
            bool on = parts[1].Equals("on", StringComparison.OrdinalIgnoreCase) || parts[1] == "1" || parts[1].Equals("true", StringComparison.OrdinalIgnoreCase);
            DebugSettings.SetSwitch(server, connection, setting.Value.Index, on);
            Chat(server, connection, setting.Value.Name + " " + (on ? "on" : "off") + ".");
            log("ADMIN", "[" + role + "] " + who + " /debug " + setting.Value.Name + " " + (on ? "on" : "off"));
            return true;
        }
        if (!float.TryParse(parts[1], NumberStyles.Float, Inv, out float value))
        {
            Chat(server, connection, "\"" + parts[1] + "\" is not a number.");
            return true;
        }
        DebugSettings.SetFloat(server, connection, setting.Value.Index, value);
        Chat(server, connection, setting.Value.Name + " = " + value.ToString("0.###", Inv) + " (/debug clear " + setting.Value.Name + " to undo).");
        log("ADMIN", "[" + role + "] " + who + " /debug " + setting.Value.Name + " " + value.ToString("0.###", Inv));
        return true;
    }

    private static void Chat(NetServer server, NetConnection connection, string text) => WorldObjects.SendChat(server, connection, text);
}
