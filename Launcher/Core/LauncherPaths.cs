namespace SteambirdsLauncher.Core;

// Everything the launcher reads or writes lives in the folder of the exe:
//   SteambirdsLauncher.exe
//   launcher.json            launcher settings (game folder, theme, languages, options, window)
//   lang\                    translations (pt-BR.json...; English is built in)
//   logs\                    server session logs, server-report.txt, launcher errors
//   server\                  SteambirdsServer.exe and its data (maps, configs, accounts)
//   server\_backup\          copies made before the launcher (or the build script) replaced a file
//   plugin\                  QuieterPlugin.dll, copied into the game's BepInEx\plugins on request
public static class LauncherPaths
{
    public static string BaseFolder { get; } = ResolveBaseFolder();

    public static string SettingsFile => Path.Combine(BaseFolder, "launcher.json");
    public static string LangFolder => Path.Combine(BaseFolder, "lang");
    public static string LogsFolder => Path.Combine(BaseFolder, "logs");
    public static string ReportFile => Path.Combine(LogsFolder, "server-report.txt");
    public static string ErrorLogFile => Path.Combine(LogsFolder, "launcher-errors.txt");

    public static string ServerFolder => Path.Combine(BaseFolder, "server");
    public static string ServerExeName => "SteambirdsServer.exe";
    public static string ServerExe => Path.Combine(ServerFolder, ServerExeName);
    public static string MapsFolder => Path.Combine(ServerFolder, "maps");
    // Enemy AI files: ai\behaviors\*.json and ai\brains\*.json (the Enemy AI tab edits them; the server reads them).
    public static string AiFolder => Path.Combine(ServerFolder, "ai");
    public static string BackupFolder => Path.Combine(ServerFolder, "_backup");

    public static string PluginFolder => Path.Combine(BaseFolder, "plugin");
    public static string BundledPlugin => Path.Combine(PluginFolder, "QuieterPlugin.dll");

    public static string ServerFile(string name) => Path.Combine(ServerFolder, name);

    public static void EnsureLogsFolder() => Directory.CreateDirectory(LogsFolder);

    // New folder server\_backup\<kind>-<date> for copies made before changing files.
    public static string NewBackupFolder(string kind)
    {
        string folder = Path.Combine(BackupFolder, kind + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string ResolveBaseFolder()
    {
        // Environment.ProcessPath is the real exe, also for single-file builds.
        string? exe = Environment.ProcessPath;
        string? folder = string.IsNullOrEmpty(exe) ? null : Path.GetDirectoryName(exe);
        return string.IsNullOrEmpty(folder) ? AppContext.BaseDirectory : folder;
    }
}
