using System.Text.Encodings.Web;
using System.Text.Json;

namespace SteambirdsLauncher.Core;

// launcher.json, next to the exe. Unknown or missing fields fall back to the defaults below.
public sealed class LauncherSettings
{
    // Folder with Steambirds.exe (depot 386011). Empty = detect from the Steam install.
    public string GameFolder { get; set; } = "";

    // Player name the other players see. Empty = the name of the Steam account. The account on the server is always
    // the Steam account (SteamID), whatever the name.
    public string PlayerName { get; set; } = "";

    // Server the Play button joins: "" = the server of this PC; otherwise "<host>:<login port>" of another PC.
    public string ServerAddress { get; set; } = "";

    // Name of that server (shown while the list is not searched again).
    public string ServerAddressName { get; set; } = "";

    // Servers typed in "Connect by IP" ("<host>:<port>"), most recent first. The search also asks them directly.
    public List<string> RecentServers { get; set; } = new List<string>();

    // UDP port where the launcher looks for servers (network.json "searchPort" of the servers; 6444 by default).
    public int SearchPort { get; set; } = ServerNetworkFile.DefaultSearchPort;

    // Searches the local network and the VPNs when the launcher opens and every minute while the Client tab is open.
    public bool AutoSearch { get; set; } = true;

    // Launcher language: "en" (built in) or the name of a file in lang\ without .json ("pt-BR").
    public string Language { get; set; } = L.English;

    // Dark theme for the launcher windows.
    public bool DarkTheme { get; set; }

    // Game language: a locale id of the game ("en-US"...) passed with -locale, or empty to let the game decide
    // (the language picked in the game's own options menu).
    public string GameLocale { get; set; } = "";

    // "Play" starts the server of this PC first when it is not running (only when the chosen server is this PC).
    public bool StartServerWithGame { get; set; } = true;

    // Hides the HarmonyX IL dumps and the "referenced script is missing" warnings in the client console.
    public bool HideClientLogNoise { get; set; } = true;

    // Server console: also show the info lines (what the players do: skills, shots, messages...). Off = warnings,
    // errors and the events of the server (connections, dungeons...). The log file always has every line.
    public bool ShowServerInfoLines { get; set; }

    // Writes logs\server-report.txt every time the server stops (like Run server.ps1 did on Ctrl+C).
    public bool ReportOnServerStop { get; set; } = true;

    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowWidth { get; set; } = 1180;
    public int WindowHeight { get; set; } = 820;
    public bool WindowMaximized { get; set; }
    public int SelectedTab { get; set; }

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static LauncherSettings Load(out string? warning)
    {
        warning = null;
        string path = LauncherPaths.SettingsFile;
        if (!File.Exists(path)) return new LauncherSettings();
        try
        {
            LauncherSettings settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), Options) ?? new LauncherSettings();
            settings.RecentServers ??= new List<string>();
            settings.ServerAddress ??= "";
            settings.ServerAddressName ??= "";
            settings.PlayerName ??= "";
            if (settings.SearchPort < 1 || settings.SearchPort > 65535) settings.SearchPort = ServerNetworkFile.DefaultSearchPort;
            return settings;
        }
        catch (Exception e)
        {
            warning = "Could not read " + path + " (" + e.Message + "); using the default settings.";
            return new LauncherSettings();
        }
    }

    public bool Save(out string? error)
    {
        error = null;
        try
        {
            TextFiles.WriteAtomic(LauncherPaths.SettingsFile, JsonSerializer.Serialize(this, Options), withBom: false);
            return true;
        }
        catch (Exception e)
        {
            error = L.F("Could not save {0}: {1}", LauncherPaths.SettingsFile, e.Message);
            return false;
        }
    }
}
