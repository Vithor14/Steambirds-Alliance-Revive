namespace SteambirdsGameFiles;

// Where the tool takes its files from and where it writes, by the package it sits in:
//   launcher package (next to SteambirdsLauncher.exe): server\, server\data\, editor-assets\, plugin\, logs\
//   server package   (next to SteambirdsServer.exe):   .\,      data\,        editor-assets\, plugin\, logs\
// --server, --editor-assets and --plugin choose other places (for example the project folders while developing).
internal sealed class ToolLayout
{
    public const string ServerExeName = "SteambirdsServer.exe";
    public const string LauncherExeName = "SteambirdsLauncher.exe";

    public string BaseFolder { get; init; } = "";
    public string Kind { get; init; } = "";
    public string ServerFolder { get; init; } = "";
    public string EditorAssetsFolder { get; init; } = "";
    public string PluginFile { get; init; } = "";
    public string LogsFolder { get; init; } = "";

    public bool IsLauncherPackage { get; init; }

    public string ServerDataFolder => Path.Combine(ServerFolder, "data");
    public string ServerExe => Path.Combine(ServerFolder, ServerExeName);
    public string NetworkFile => Path.Combine(ServerFolder, "network.json");
    public bool HasServer => File.Exists(ServerExe);

    public static ToolLayout Resolve(Options options)
    {
        string baseFolder = BaseFolderOfExe();
        bool launcher = File.Exists(Path.Combine(baseFolder, LauncherExeName));
        bool server = File.Exists(Path.Combine(baseFolder, ServerExeName));

        string serverFolder = launcher ? Path.Combine(baseFolder, "server") : baseFolder;
        if (!string.IsNullOrWhiteSpace(options.ServerFolder)) serverFolder = Path.GetFullPath(options.ServerFolder);

        string editorAssets = !string.IsNullOrWhiteSpace(options.EditorAssetsFolder)
            ? Path.GetFullPath(options.EditorAssetsFolder)
            : Path.Combine(baseFolder, "editor-assets");

        string plugin = !string.IsNullOrWhiteSpace(options.PluginFile)
            ? Path.GetFullPath(options.PluginFile)
            : Path.Combine(baseFolder, "plugin", "QuieterPlugin.dll");

        string kind = launcher ? "launcher package" : server ? "server package" : "folder of the tool";
        return new ToolLayout
        {
            BaseFolder = baseFolder,
            Kind = kind,
            IsLauncherPackage = launcher,
            ServerFolder = serverFolder,
            EditorAssetsFolder = editorAssets,
            PluginFile = plugin,
            LogsFolder = Path.Combine(baseFolder, "logs")
        };
    }

    private static string BaseFolderOfExe()
    {
        // Environment.ProcessPath is the real exe, also for single-file builds.
        string? exe = Environment.ProcessPath;
        string? folder = string.IsNullOrEmpty(exe) ? null : Path.GetDirectoryName(exe);
        return string.IsNullOrEmpty(folder) ? AppContext.BaseDirectory : folder;
    }
}
