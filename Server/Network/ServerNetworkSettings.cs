using System.Text.Json;
using System.Text.Json.Serialization;

// Network settings of the server (network.json, next to progression.json): name, who can join, ports and the server
// search of the launchers. The launcher edits this file in Server > Network and multiplayer.
// Ports only change when the server restarts; the rest is reloaded by itself.
public sealed class ServerNetworkSettings
{
    // Name shown in the server lists of the launchers.
    public string ServerName { get; set; } = "Steambirds server";

    // true: players of other PCs (local network, Radmin, Hamachi, internet) can join. false: only this PC.
    public bool AllowOtherPlayers { get; set; } = true;

    // true: answers the "find servers" of the launchers on the local network and on VPNs.
    public bool ShowInServerSearch { get; set; } = true;

    // Players at the same time (accounts online). Whoever comes when it is full gets "server full".
    public int MaxPlayers { get; set; } = 16;

    // Who can use the chat commands (/xp, /dungeon, /items...): "everyone", "host" (only the game of this PC) or "none".
    // /leave and /players always work.
    public string ChatCommands { get; set; } = "everyone";

    // Extra chat message for every player who arrives at the Nexus (empty = none).
    public string WelcomeMessage { get; set; } = "";

    // true: accounts with username and password (Network/AccountLogins.cs). A Steam login with no account yet makes the game open its
    // register screen (name, e-mail, password); accounts that already exist keep logging in through Steam, and the game's own login
    // screen (no Steam) takes username and password. false: one account per Steam ID, no passwords (the default).
    public bool PasswordLogin { get; set; }

    // TCP port of the login (OZY_URL = http://<ip>:<httpPort>/ozy).
    public int HttpPort { get; set; } = NetworkPorts.DefaultHttp;

    // UDP ports of the game servers.
    public int NexusPort { get; set; } = NetworkPorts.DefaultNexus;
    public int TutorialPort { get; set; } = NetworkPorts.DefaultTutorial;
    public int DungeonPort { get; set; } = NetworkPorts.DefaultDungeon;
    public int WorldPort { get; set; } = NetworkPorts.DefaultWorld;

    // UDP port that answers the server search of the launchers. Launchers search on 6444 unless set otherwise.
    public int SearchPort { get; set; } = NetworkPorts.DefaultSearch;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownKeys { get; set; }
}

// Ports the server uses in this run (read once at startup: changing network.json needs a restart).
public sealed class NetworkPorts
{
    public const int DefaultHttp = 6443;
    public const int DefaultNexus = 1111;
    public const int DefaultTutorial = 1112;
    public const int DefaultDungeon = 1113;
    public const int DefaultWorld = 1114;
    public const int DefaultSearch = 6444;

    public int Http { get; init; } = DefaultHttp;
    public int Nexus { get; init; } = DefaultNexus;
    public int Tutorial { get; init; } = DefaultTutorial;
    public int Dungeon { get; init; } = DefaultDungeon;
    public int World { get; init; } = DefaultWorld;
    public int Search { get; init; } = DefaultSearch;

    public override string ToString() =>
        "HTTP " + Http + " (TCP), Nexus " + Nexus + ", tutorial " + Tutorial + ", dungeon " + Dungeon + ", world " + World +
        ", search " + Search + " (UDP)";
}

public static class ServerNetworkSettingsFile
{
    public const string FileName = "network.json";

    private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly object gate = new object();
    private static ServerNetworkSettings current = new ServerNetworkSettings();
    private static DateTime readAt = DateTime.MinValue;
    private static DateTime nextCheck = DateTime.MinValue;
    private static Action<string, string> log = (_, _) => { };

    public static string FilePath => Path.Combine(Directory.GetCurrentDirectory(), FileName);

    // Ports of this run (Start fills them).
    public static NetworkPorts Ports { get; private set; } = new NetworkPorts();

    // true when the HTTP and UDP servers accept other PCs in this run (read at startup, like the ports).
    public static bool OpenToOtherPcs { get; private set; } = true;

    // Current settings. The file is checked again at most once per second (name, players, commands, message).
    public static ServerNetworkSettings Current
    {
        get
        {
            lock (gate)
            {
                if (DateTime.UtcNow >= nextCheck)
                {
                    nextCheck = DateTime.UtcNow.AddSeconds(1);
                    ReloadIfChanged(false);
                }
                return current;
            }
        }
    }

    // Map test mode (MapTest): the ports are given, only this PC, and network.json is not read.
    private static bool fixedForTest;

    public static void StartForMapTest(NetworkPorts ports, Action<string, string> log)
    {
        lock (gate)
        {
            fixedForTest = true;
            ServerNetworkSettingsFile.log = log;
            Ports = ports;
            OpenToOtherPcs = false;
            current = new ServerNetworkSettings
            {
                ServerName = "Map test",
                AllowOtherPlayers = false,
                ShowInServerSearch = false,
                MaxPlayers = 4,
                ChatCommands = "everyone",
                HttpPort = ports.Http, NexusPort = ports.Nexus, TutorialPort = ports.Tutorial,
                DungeonPort = ports.Dungeon, WorldPort = ports.World, SearchPort = ports.Search
            };
            log("NETWORK", "map test: " + Ports + "; only this PC");
        }
    }

    // Reads (or creates) network.json and fixes the ports of this run.
    public static void Start(Action<string, string> log)
    {
        lock (gate)
        {
            ServerNetworkSettingsFile.log = log;
            if (!File.Exists(FilePath))
            {
                try
                {
                    File.WriteAllText(FilePath, DefaultFileText);
                    log("NETWORK", FilePath + " created with the default values");
                }
                catch (Exception e)
                {
                    log("NETWORK", "could not create " + FilePath + ": " + e.Message);
                }
            }
            ReloadIfChanged(true);

            ServerNetworkSettings s = current;
            int http = ValidPort(s.HttpPort, NetworkPorts.DefaultHttp, "httpPort");
            int nexus = ValidPort(s.NexusPort, NetworkPorts.DefaultNexus, "nexusPort");
            int tutorial = ValidPort(s.TutorialPort, NetworkPorts.DefaultTutorial, "tutorialPort");
            int dungeon = ValidPort(s.DungeonPort, NetworkPorts.DefaultDungeon, "dungeonPort");
            int world = ValidPort(s.WorldPort, NetworkPorts.DefaultWorld, "worldPort");
            int search = ValidPort(s.SearchPort, NetworkPorts.DefaultSearch, "searchPort");

            // The four UDP game ports and the search port must be different (TCP and UDP can share a number).
            var udp = new[] { nexus, tutorial, dungeon, world, search };
            if (udp.Distinct().Count() != udp.Length)
            {
                log("NETWORK", "repeated UDP ports in " + FileName + " (nexus " + nexus + ", tutorial " + tutorial + ", dungeon " + dungeon +
                    ", world " + world + ", search " + search + "): using the default ports 1111, 1112, 1113, 1114 and 6444");
                nexus = NetworkPorts.DefaultNexus;
                tutorial = NetworkPorts.DefaultTutorial;
                dungeon = NetworkPorts.DefaultDungeon;
                world = NetworkPorts.DefaultWorld;
                search = NetworkPorts.DefaultSearch;
            }

            Ports = new NetworkPorts { Http = http, Nexus = nexus, Tutorial = tutorial, Dungeon = dungeon, World = world, Search = search };
            OpenToOtherPcs = s.AllowOtherPlayers;
            log("NETWORK", "server '" + s.ServerName + "': " + Ports + "; " +
                (OpenToOtherPcs ? "accepts players from other PCs" : "only this PC (allowOtherPlayers = false)") +
                ", up to " + Math.Max(1, s.MaxPlayers) + " player(s), " +
                (s.ShowInServerSearch ? "shows in the launchers' search" : "does not show in the search") +
                ", chat commands: " + NormalizedCommands(s.ChatCommands));
        }
    }

    // "everyone", "host" or "none" (anything else = "everyone").
    public static string NormalizedCommands(string? value)
    {
        string v = (value ?? "").Trim().ToLowerInvariant();
        return v == "host" || v == "none" ? v : "everyone";
    }

    private static int ValidPort(int port, int fallback, string key)
    {
        if (port >= 1 && port <= 65535) return port;
        log("NETWORK", key + " = " + port + " is not a valid port (1 to 65535): using " + fallback);
        return fallback;
    }

    private static void ReloadIfChanged(bool atStart)
    {
        try
        {
            if (fixedForTest || !File.Exists(FilePath)) return;
            DateTime modified = File.GetLastWriteTimeUtc(FilePath);
            if (modified == readAt) return;
            readAt = modified;

            ServerNetworkSettings? read = JsonSerializer.Deserialize<ServerNetworkSettings>(File.ReadAllText(FilePath), ReadOptions);
            if (read == null) return;
            read.ServerName = string.IsNullOrWhiteSpace(read.ServerName) ? "Steambirds server" : read.ServerName.Trim();
            if (read.ServerName.Length > 48) read.ServerName = read.ServerName.Substring(0, 48);
            read.WelcomeMessage ??= "";
            if (read.MaxPlayers < 1) read.MaxPlayers = 1;

            bool portsChanged = !atStart && (read.HttpPort != Ports.Http || read.NexusPort != Ports.Nexus ||
                                             read.TutorialPort != Ports.Tutorial || read.DungeonPort != Ports.Dungeon || read.WorldPort != Ports.World ||
                                             read.SearchPort != Ports.Search || read.AllowOtherPlayers != OpenToOtherPcs);
            current = read;
            if (!atStart)
            {
                log("NETWORK", FilePath + " read again: name '" + read.ServerName + "', up to " + read.MaxPlayers + " player(s), commands " +
                    NormalizedCommands(read.ChatCommands) + (portsChanged ? " (ports and allowOtherPlayers only change when the server restarts)" : ""));
            }
            if (read.UnknownKeys != null && read.UnknownKeys.Count > 0)
                log("NETWORK", "key(s) this version does not use in " + FileName + ": " + string.Join(", ", read.UnknownKeys.Keys));
        }
        catch (Exception e)
        {
            // A broken file keeps the last good settings.
            log("NETWORK", "error in " + FilePath + " (" + e.Message + "): keeping the previous configuration");
        }
    }

    // Default file (the reader skips the comments).
    private const string DefaultFileText =
@"// Network settings of the server. Ports and allowOtherPlayers only change when the server restarts.
{
  // Name shown in the server lists of the launchers.
  ""serverName"": ""Steambirds server"",

  // true: players of other PCs (local network, Radmin, Hamachi, internet) can join. false: only this PC.
  ""allowOtherPlayers"": true,

  // true: answers the ""find servers"" of the launchers on the local network and on VPNs.
  ""showInServerSearch"": true,

  // Players at the same time.
  ""maxPlayers"": 16,

  // Who can use the chat commands (/xp, /dungeon, /items...): ""everyone"", ""host"" (only the game of this PC) or ""none"".
  ""chatCommands"": ""everyone"",

  // Extra chat message for every player who arrives at the Nexus (empty = none).
  ""welcomeMessage"": """",

  // true: accounts with username and password. A new Steam player registers in the game (name, e-mail, password);
  // accounts that already exist keep logging in through Steam. false: one account per Steam ID, no passwords.
  ""passwordLogin"": false,

  // TCP port of the login (the launcher opens the game with OZY_URL=http://<ip>:<httpPort>/ozy).
  ""httpPort"": 6443,

  // UDP ports of the Nexus, the tutorial, the dungeons and the open world.
  ""nexusPort"": 1111,
  ""tutorialPort"": 1112,
  ""dungeonPort"": 1113,
  ""worldPort"": 1114,

  // UDP port of the server search of the launchers (they search on 6444 unless set otherwise).
  ""searchPort"": 6444
}
";
}
