using SteambirdsLauncher.Core.GameData;

namespace SteambirdsLauncher.Core;

// The game file "configBinary" (every config of the game: items, loot, XP tables, skills, unlocks, plane classes)
// as the server of this launcher sees it.
//
// The server looks for it in this order (Server GameDataFiles.ConfigBinaryPlaces), with
// STEAMBIRDS_GAME = the launcher's game folder:
//   1. server\data\configBinary            copy that travels with the launcher (Build launcher.ps1 puts it there)
//   2. <game folder>\Steambirds_Data\StreamingAssets\configBinary
//   3. the default Steam depot folder of the game
// Without any of them the server still starts, but with no item catalog (no loot, reforge or recycling), the
// built-in XP tables, no skill validation and only the walker unlocked. That is what happens on a host PC that
// does not have the game installed, so the launcher warns before starting the server (LauncherContext.StartServer).
//
// The game's "configBinary" file, as the launcher's server sees it. Without it the server starts with no item catalog
// (no loot), with built-in XP tables and only the walker; the launcher warns before starting the server.
public static class ServerGameData
{
    public const string FileName = "configBinary";

    private const string DefaultGameFolder = @"C:\Program Files (x86)\Steam\steamapps\content\app_386010\depot_386011";

    // Copy kept with the server (first place the server looks).
    public static string ServerCopyPath => Path.Combine(LauncherPaths.ServerFolder, "data", FileName);

    public static string GameCopyPath(string gameFolder) =>
        Path.Combine(gameFolder, "Steambirds_Data", "StreamingAssets", FileName);

    // Places the server checks, in its order.
    public static IEnumerable<string> Places(string? gameFolder)
    {
        yield return ServerCopyPath;
        if (!string.IsNullOrWhiteSpace(gameFolder)) yield return GameCopyPath(gameFolder);
        yield return GameCopyPath(DefaultGameFolder);
    }

    // The configBinary the server will use, or null when it will start without one.
    public static string? Find(string? gameFolder)
    {
        foreach (string path in Places(gameFolder))
        {
            try
            {
                if (File.Exists(path)) return path;
            }
            catch
            {
                // A path the launcher cannot check counts as missing.
            }
        }
        return null;
    }

    // Checks that "source" really is a configBinary (it must unpack into the game's config files) and copies it to
    // server\data. The previous copy, if any, goes to server\_backup first.
    public static bool TryImport(string source, out string error)
    {
        error = "";
        try
        {
            byte[] raw = File.ReadAllBytes(source);
            Dictionary<string, ConfigNode> files = ConfigBinaryReader.Parse(raw);
            if (files.Count == 0)
            {
                error = L.F("{0} does not contain any game config. Choose the configBinary of the game (Steambirds_Data\\StreamingAssets).", source);
                return false;
            }

            string target = ServerCopyPath;
            if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return true;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                string backup = LauncherPaths.NewBackupFolder("configBinary");
                File.Copy(target, Path.Combine(backup, FileName), overwrite: true);
            }
            File.WriteAllBytes(target, raw);
            return true;
        }
        catch (Exception e)
        {
            error = L.F("Could not use {0} as the configBinary: {1}", source, e.Message);
            return false;
        }
    }
}
