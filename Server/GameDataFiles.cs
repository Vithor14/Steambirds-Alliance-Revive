// Where the server finds the data taken from the game client (build 201219.1413):
//   actions_config.txt   skill clouds and maneuvers (Game)
//   units_config.txt     units and guns: enemies, bosses, tutorial walls (Enemies)
//   mapinfo_config.txt   ClientMapInfo of each terrain: client hints and conversations (MapInfo)
//   portraits_config.txt pilot portrait pieces (plugin 1.20.0+; PilotExtras picks the portrait)
//   configBinary         all the game configs (GameData/ConfigBinary): XP, skills, unlocks, buffs...
//
// These files were written by the Steambirds Quieter plugin, but they are only data from the client's configBinary:
// they do not change while the game version stays the same. That is why they ship with the server, in the "data"
// folder (Server\data), and the game server runs without BepInEx or any plugin.
//
// Search order: Server\data, the "data" folder next to the executable and, last, the BepInEx folder
// (STEAMBIRDS_BEPINEX or the default game folder), for people who still use the plugin.
//
// The configBinary is looked for in Server\data and then in the installed game (STEAMBIRDS_GAME, the folder above
// BepInEx or the default folder of depot 386011): the server reads it straight from the game, without copying.
public static class GameDataFiles
{
    private const string DefaultGameFolder =
        @"C:\Program Files (x86)\Steam\steamapps\content\app_386010\depot_386011";

    private const string DefaultBepInExFolder = DefaultGameFolder + @"\BepInEx";

    public static string BepInExFolder()
    {
        string? folder = Environment.GetEnvironmentVariable("STEAMBIRDS_BEPINEX");
        return string.IsNullOrWhiteSpace(folder) ? DefaultBepInExFolder : folder;
    }

    public static string ServerFolder()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "data");
    }

    // Folder of the installed game (where Steambirds.exe is).
    public static string GameFolder()
    {
        string? folder = Environment.GetEnvironmentVariable("STEAMBIRDS_GAME");
        if (!string.IsNullOrWhiteSpace(folder)) return folder;

        string? aboveBepInEx = Path.GetDirectoryName(BepInExFolder().TrimEnd('\\', '/'));
        if (!string.IsNullOrWhiteSpace(aboveBepInEx) && Directory.Exists(aboveBepInEx)) return aboveBepInEx;

        return DefaultGameFolder;
    }

    // Path of the requested file: the first place where it exists. If it exists nowhere, the path in the
    // server's "data" folder (this is what the warnings in the log show).
    public static string FilePath(string fileName)
    {
        foreach (string folder in Folders())
        {
            string path = Path.Combine(folder, fileName);
            if (File.Exists(path)) return path;
        }
        return Path.Combine(ServerFolder(), fileName);
    }

    // Places where the configBinary can be, in search order.
    public static IEnumerable<string> ConfigBinaryPlaces()
    {
        yield return Path.Combine(ServerFolder(), "configBinary");
        yield return Path.Combine(AppContext.BaseDirectory, "data", "configBinary");
        yield return Path.Combine(GameFolder(), "Steambirds_Data", "StreamingAssets", "configBinary");
        yield return Path.Combine(DefaultGameFolder, "Steambirds_Data", "StreamingAssets", "configBinary");
    }

    // First configBinary that exists, or null.
    public static string? ConfigBinaryPath()
    {
        foreach (string path in ConfigBinaryPlaces())
        {
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static IEnumerable<string> Folders()
    {
        yield return ServerFolder();
        yield return Path.Combine(AppContext.BaseDirectory, "data");
        yield return BepInExFolder();
    }
}
