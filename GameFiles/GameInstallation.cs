using System.Diagnostics;
using Microsoft.Win32;

namespace SteambirdsGameFiles;

// The installed game, with the same rules as the launcher (Launcher\Core\GameInstall.cs): the depot downloaded with the
// Steam console goes to <Steam>\steamapps\content\app_386010\depot_386011.
internal static class GameInstallation
{
    public const string ExeName = "Steambirds.exe";
    public const string ProcessName = "Steambirds";
    public const string SteamAppId = "386010";
    public const string DepotCommand = "download_depot 386010 386011 58813776496064724";

    private const string DepotSubfolder = @"steamapps\content\app_386010\depot_386011";
    private const string DefaultSteamFolder = @"C:\Program Files (x86)\Steam";

    public static bool HasGame(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, ExeName));

    public static string ConfigBinaryPath(string folder) =>
        Path.Combine(folder, "Steambirds_Data", "StreamingAssets", "configBinary");

    public static string BepInExFolder(string folder) => Path.Combine(folder, "BepInEx");

    // STEAMBIRDS_GAME, then the depot folder of the Steam client (from the registry), then the default Steam folder.
    public static string? Find()
    {
        var candidates = new List<string>();
        string? fromEnvironment = Environment.GetEnvironmentVariable("STEAMBIRDS_GAME");
        if (!string.IsNullOrWhiteSpace(fromEnvironment)) candidates.Add(fromEnvironment);
        string? steam = SteamFolder();
        if (steam != null) candidates.Add(Path.Combine(steam, DepotSubfolder));
        candidates.Add(Path.Combine(DefaultSteamFolder, DepotSubfolder));

        foreach (string candidate in candidates)
        {
            try
            {
                string full = Path.GetFullPath(candidate);
                if (HasGame(full)) return full;
            }
            catch
            {
                // A path that cannot be checked counts as missing.
            }
        }
        return null;
    }

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

    // "buildId: 201219.1413" in Steambirds_Data\StreamingAssets\version.bytes.
    public static string ReadBuildId(string folder)
    {
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

    // True for a 64-bit game, false for 32-bit, null when the exe cannot be read (machine field of the PE header).
    public static bool? Is64Bit(string folder)
    {
        try
        {
            using FileStream stream = File.OpenRead(Path.Combine(folder, ExeName));
            using var reader = new BinaryReader(stream);
            stream.Position = 0x3C;
            int headerOffset = reader.ReadInt32();
            stream.Position = headerOffset;
            if (reader.ReadUInt32() != 0x00004550) return null; // "PE\0\0"
            ushort machine = reader.ReadUInt16();
            if (machine == 0x8664) return true;
            if (machine == 0x014C) return false;
            return null;
        }
        catch
        {
            return null;
        }
    }

    // steam_appid.txt with 386010 next to Steambirds.exe: the game starts the Steam API (and logs in with the Steam
    // account) when it is opened outside the Steam library. Created only when missing. Returns an error or null.
    public static string? EnsureSteamAppId(string folder)
    {
        try
        {
            string path = Path.Combine(folder, "steam_appid.txt");
            if (!File.Exists(path)) File.WriteAllText(path, SteamAppId);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public static bool IsSteamRunning()
    {
        Process[] found = Process.GetProcessesByName("steam");
        foreach (Process p in found) p.Dispose();
        return found.Length > 0;
    }

    public static void OpenSteam(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e)
        {
            ConsoleUi.Warn("could not open Steam (" + uri + "): " + e.Message);
        }
    }

    public static Process[] RunningGames() => Process.GetProcessesByName(ProcessName);

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
