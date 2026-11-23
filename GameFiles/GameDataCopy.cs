namespace SteambirdsGameFiles;

// Copies the game files to the server and to the map editor:
//   configBinary (the game's configs)              game\Steambirds_Data\StreamingAssets -> server\data
//   units, actions, map info and portraits data    game\BepInEx (written by the plugin)  -> server\data
//   ground_assets.txt (names of the floor pieces)  game\BepInEx                          -> editor-assets and server\data (open world)
//   pictures and thumbs.json                       export folder (drawn by the plugin)   -> editor-assets
// The server looks in its data folder first (Server\GameDataFiles.cs), so it no longer depends on the game folder.
internal static class GameDataCopy
{
    public static readonly string[] ServerDataFiles =
    {
        "units_config.txt", "actions_config.txt", "mapinfo_config.txt", "portraits_config.txt"
    };

    public const string GroundAssetsFile = "ground_assets.txt";

    // What each data file means for the server, shown when it is missing.
    private static readonly Dictionary<string, string> Purpose = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["units_config.txt"] = "enemies, bosses and tutorial walls",
        ["actions_config.txt"] = "skill clouds and maneuvers",
        ["mapinfo_config.txt"] = "tutorial and Nexus hints",
        ["portraits_config.txt"] = "pilot portraits",
        [GroundAssetsFile] = "names of the floor pieces in the map editor"
    };

    // Returns false when the game has no configBinary.
    public static bool CopyConfigBinary(string game, string serverData)
    {
        string source = GameInstallation.ConfigBinaryPath(game);
        if (!File.Exists(source))
        {
            ConsoleUi.Warn("the game has no " + source + ": the server will have no items, loot or XP tables.");
            return false;
        }
        Report("configBinary", CopyIfChanged(source, Path.Combine(serverData, "configBinary")), null);
        return true;
    }

    // Data files written by the plugin in the game's BepInEx folder. sinceUtc: the start of the export (older files
    // came from an earlier run of the game; they are copied too, with a note).
    public static int CopyPluginFiles(string game, string serverData, string editorAssets, DateTime? sinceUtc)
    {
        string bepInEx = GameInstallation.BepInExFolder(game);
        int copied = 0;
        foreach (string name in ServerDataFiles)
        {
            if (CopyPluginFile(Path.Combine(bepInEx, name), Path.Combine(serverData, name), name, sinceUtc)) copied++;
        }
        if (CopyPluginFile(Path.Combine(bepInEx, GroundAssetsFile), Path.Combine(editorAssets, GroundAssetsFile), GroundAssetsFile, sinceUtc)) copied++;
        // The open world server also needs the ids of the floor pieces (it draws the world floor itself).
        if (CopyPluginFile(Path.Combine(bepInEx, GroundAssetsFile), Path.Combine(serverData, GroundAssetsFile), GroundAssetsFile, sinceUtc)) copied++;
        return copied;
    }

    // Pictures and thumbs.json drawn by the plugin. Returns the number of pictures copied.
    public static int CopyPictures(string exportFolder, string editorAssets)
    {
        int pictures = 0;
        foreach (string sub in new[] { "ground", "units" })
        {
            string source = Path.Combine(exportFolder, sub);
            if (!Directory.Exists(source)) continue;
            string target = Path.Combine(editorAssets, sub);
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source, "*.png"))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
                pictures++;
            }
        }

        string thumbs = Path.Combine(exportFolder, "thumbs.json");
        if (File.Exists(thumbs))
        {
            Directory.CreateDirectory(editorAssets);
            File.Copy(thumbs, Path.Combine(editorAssets, "thumbs.json"), overwrite: true);
        }
        else
        {
            ConsoleUi.Warn("the export has no thumbs.json: the map editor will not use the pictures.");
        }
        return pictures;
    }

    private static bool CopyPluginFile(string source, string target, string name, DateTime? sinceUtc)
    {
        if (!File.Exists(source))
        {
            ConsoleUi.Warn(name + " was not written by the plugin (" + Purpose[name] + ").");
            return false;
        }
        string? note = null;
        if (sinceUtc.HasValue && File.GetLastWriteTimeUtc(source) < sinceUtc.Value)
            note = "from an earlier run of the game";
        Report(name, CopyIfChanged(source, target), note);
        return true;
    }

    private static void Report(string name, string state, string? note)
    {
        string text = name + ": " + state + (note != null ? " (" + note + ")" : "");
        if (note != null) ConsoleUi.Warn(text);
        else ConsoleUi.Done(text);
    }

    // "new", "updated" or "unchanged".
    private static string CopyIfChanged(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            if (BepInExSetup.SameContent(source, target)) return "unchanged";
            File.Copy(source, target, overwrite: true);
            return "updated";
        }
        File.Copy(source, target, overwrite: true);
        return "new";
    }
}
