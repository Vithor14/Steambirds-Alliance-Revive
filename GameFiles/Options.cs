namespace SteambirdsGameFiles;

// Command line of the tool. Everything is optional: by default the tool finds the game and the folders of the package
// it sits in (see ToolLayout).
internal sealed class Options
{
    public const string Usage =
        "SteambirdsGameFiles.exe [options]\n" +
        "\n" +
        "Takes from your copy of Steambirds Alliance the files the server and the map editor need\n" +
        "(they belong to the game, so they are not in the download).\n" +
        "\n" +
        "  --game <folder>           game folder (default: the Steam depot folder, or STEAMBIRDS_GAME)\n" +
        "  --server <folder>         server folder (default: next to the tool, or server\\ in the launcher package)\n" +
        "  --editor-assets <folder>  where the map editor pictures go (default: editor-assets next to the tool)\n" +
        "  --plugin <file>           QuieterPlugin.dll to install (default: plugin\\QuieterPlugin.dll next to the tool)\n" +
        "  --copy-only               only copy configBinary and the data files already in the game's BepInEx folder;\n" +
        "                            do not install anything and do not open the game\n" +
        "  --no-server               open the game without starting the server of the package\n" +
        "  --no-pause                never ask or wait for Enter (default answers)\n" +
        "  --help                    this text";

    public string? GameFolder { get; private set; }
    public string? ServerFolder { get; private set; }
    public string? EditorAssetsFolder { get; private set; }
    public string? PluginFile { get; private set; }
    public bool CopyOnly { get; private set; }
    public bool NoServer { get; private set; }
    public bool NoPause { get; private set; }
    public bool Help { get; private set; }
    public string? Error { get; private set; }

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (int i = 0; i < args.Length && options.Error == null; i++)
        {
            string arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--game":
                    options.GameFolder = TakeValue(args, ref i, options);
                    break;
                case "--server":
                    options.ServerFolder = TakeValue(args, ref i, options);
                    break;
                case "--editor-assets":
                    options.EditorAssetsFolder = TakeValue(args, ref i, options);
                    break;
                case "--plugin":
                    options.PluginFile = TakeValue(args, ref i, options);
                    break;
                case "--copy-only":
                    options.CopyOnly = true;
                    break;
                case "--no-server":
                    options.NoServer = true;
                    break;
                case "--no-pause":
                    options.NoPause = true;
                    break;
                case "--help":
                case "-h":
                case "/?":
                    options.Help = true;
                    break;
                default:
                    options.Error = "unknown option: " + arg;
                    break;
            }
        }
        return options;
    }

    private static string? TakeValue(string[] args, ref int i, Options options)
    {
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            options.Error = args[i] + " needs a value";
            return null;
        }
        i++;
        return args[i];
    }
}
