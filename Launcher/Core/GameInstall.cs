using Microsoft.Win32;

namespace SteambirdsLauncher.Core;

// Where the game is and what is installed in it.
// The game comes from the Steam console: "download_depot 386010 386011 58813776496064724", which Steam always saves in
// <Steam>\steamapps\content\app_386010\depot_386011 (in the main Steam folder, not in other libraries).
public static class GameInstall
{
    public const string ExeName = "Steambirds.exe";
    public const string ProcessName = "Steambirds";
    private const string DepotSubfolder = @"steamapps\content\app_386010\depot_386011";
    private const string DefaultSteamFolder = @"C:\Program Files (x86)\Steam";

    public const string SteamAppId = "386010";
    public const string DepotId = "386011";
    public const string ManifestId = "58813776496064724";

    public static string ExePath(string folder) => Path.Combine(folder, ExeName);
    public static string SteamAppIdPath(string folder) => Path.Combine(folder, "steam_appid.txt");
    public static string BepInExFolder(string folder) => Path.Combine(folder, "BepInEx");
    public static string BepInExLog(string folder) => Path.Combine(folder, "BepInEx", "LogOutput.log");
    public static string InstalledPluginPath(string folder) => Path.Combine(folder, "BepInEx", "plugins", "QuieterPlugin.dll");

    // Unity log of the game (the folder name comes from the company and product name of the game).
    public static string PlayerLog => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "SpryFox", "Steambirds Alliance", "Player.log");

    public static bool HasGame(string? folder) => !string.IsNullOrWhiteSpace(folder) && File.Exists(ExePath(folder));

    public static bool HasBepInEx(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, "BepInEx", "core", "BepInEx.dll"));

    // Folder of the Steam client, from the registry.
    public static string? SteamFolder()
    {
        string? path = ReadRegistry(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath")
                       ?? ReadRegistry(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
                       ?? ReadRegistry(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath");
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return Path.GetFullPath(path.Replace('/', '\\'));
        }
        catch
        {
            return null;
        }
    }

    // Steam folder\steamapps\content\app_386010\depot_386011 if it has the game; otherwise the default path.
    public static string DetectFolder()
    {
        string? steam = SteamFolder();
        if (steam != null)
        {
            string candidate = Path.Combine(steam, DepotSubfolder);
            if (HasGame(candidate)) return candidate;
        }
        string fallback = Path.Combine(DefaultSteamFolder, DepotSubfolder);
        if (HasGame(fallback)) return fallback;
        return steam != null ? Path.Combine(steam, DepotSubfolder) : fallback;
    }

    // Folder where "download_depot 386010 386011 ..." puts the game: <Steam>\steamapps\content\app_386010\depot_386011.
    // null when Steam is not installed.
    public static string? DepotFolderInSteam()
    {
        string? steam = SteamFolder();
        return steam == null ? null : Path.Combine(steam, DepotSubfolder);
    }

    // steam_appid.txt with 386010 next to Steambirds.exe: with it the game starts the Steam API (and logs in with the
    // Steam account) when it is opened outside the Steam library, as the launcher does. Created only when missing.
    // Returns null when the file is there (or was created), or the error.
    public static string? EnsureSteamAppId(string? folder)
    {
        if (!HasGame(folder)) return null;
        try
        {
            string path = SteamAppIdPath(folder!);
            if (File.Exists(path)) return null;
            File.WriteAllText(path, SteamAppId);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    // Size of the files already in a folder (download progress).
    public static long FolderSize(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return 0;
        long total = 0;
        try
        {
            foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    total += file.Length;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        return total;
    }

    // "buildId: 201219.1413" in Steambirds_Data\StreamingAssets\version.bytes (the same file the server reads).
    public static string ReadBuildId(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "";
        try
        {
            string file = Path.Combine(folder, "Steambirds_Data", "StreamingAssets", "version.bytes");
            if (!File.Exists(file)) return "";
            foreach (string line in File.ReadAllLines(file))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("buildId:", StringComparison.OrdinalIgnoreCase))
                    return trimmed.Substring("buildId:".Length).Trim();
            }
        }
        catch
        {
        }
        return "";
    }

    private static string? ReadRegistry(string key, string value)
    {
        try
        {
            return Registry.GetValue(key, value, null) as string;
        }
        catch
        {
            return null;
        }
    }
}
