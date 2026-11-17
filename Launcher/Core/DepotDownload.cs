using System.Diagnostics;
using System.Text;

namespace SteambirdsLauncher.Core;

// Downloading the game build the server was made for, with Steam itself:
//   1. the launcher copies "download_depot 386010 386011 58813776496064724" and opens the Steam console
//      (steam://open/console); the player pastes the command there and presses Enter;
//   2. Steam downloads into <Steam>\steamapps\content\app_386010\depot_386011 and writes its console to
//      <Steam>\logs\console_log.txt ("Depot download complete" / "Depot download failed");
//   3. this watcher follows both (size of the folder and the console lines) and says when it is done.
// The Steam account needs the game in its library (Steambirds Alliance was free to play).
public sealed class DepotDownloadWatcher
{
    public enum Phase { Waiting, Downloading, Done, Failed }

    private const string ConsoleUrl = "steam://open/console";
    private readonly string? consoleLogPath;
    private long consolePosition;
    private long lastSize = -1;
    private DateTime sizeStableSince = DateTime.UtcNow;
    private Task<long>? sizeTask;

    public DepotDownloadWatcher()
    {
        TargetFolder = GameInstall.DepotFolderInSteam();
        string? steam = GameInstall.SteamFolder();
        consoleLogPath = steam == null ? null : Path.Combine(steam, "logs", "console_log.txt");
        StartedAt = DateTime.UtcNow;
        HadGameBefore = GameInstall.HasGame(TargetFolder);
        // Only the console lines written from now on count.
        try
        {
            consolePosition = consoleLogPath != null && File.Exists(consoleLogPath) ? new FileInfo(consoleLogPath).Length : 0;
        }
        catch
        {
            consolePosition = 0;
        }
    }

    // Where Steam puts the depot (null without Steam).
    public string? TargetFolder { get; }
    public DateTime StartedAt { get; }

    // The folder already had Steambirds.exe when the watcher started (a download over it updates the files).
    public bool HadGameBefore { get; }

    public Phase State { get; private set; } = Phase.Waiting;
    public long DownloadedBytes { get; private set; }

    // Last relevant line of the Steam console ("Downloading depot 386011...", "Depot download complete : ...").
    public string LastConsoleLine { get; private set; } = "";

    public static void OpenSteamConsole()
    {
        Process.Start(new ProcessStartInfo(ConsoleUrl) { UseShellExecute = true });
    }

    // Called about once per second by the window.
    public void Poll()
    {
        if (State == Phase.Done || State == Phase.Failed) return;
        ReadConsole();
        if (State == Phase.Done || State == Phase.Failed) return;

        // Folder size in the background (thousands of files).
        if (sizeTask == null)
        {
            string? folder = TargetFolder;
            sizeTask = Task.Run(() => GameInstall.FolderSize(folder));
        }
        else if (sizeTask.IsCompleted)
        {
            long size = sizeTask.Status == TaskStatus.RanToCompletion ? sizeTask.Result : 0;
            sizeTask = null;
            DownloadedBytes = size;
            if (size != lastSize)
            {
                // The first measure is only the starting point (the folder may already have an older download).
                if (lastSize >= 0 && State == Phase.Waiting) State = Phase.Downloading;
                lastSize = size;
                sizeStableSince = DateTime.UtcNow;
            }
            // No console log: the exe is there and nothing changed for 30 seconds.
            if (State == Phase.Downloading && GameInstall.HasGame(TargetFolder) && DateTime.UtcNow - sizeStableSince > TimeSpan.FromSeconds(30))
                State = Phase.Done;
        }
    }

    private void ReadConsole()
    {
        if (consoleLogPath == null || !File.Exists(consoleLogPath)) return;
        try
        {
            using var stream = new FileStream(consoleLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < consolePosition) consolePosition = 0; // the log started over
            if (stream.Length == consolePosition) return;
            stream.Seek(consolePosition, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string text = reader.ReadToEnd();
            consolePosition = stream.Length;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                bool about = line.Contains(GameInstall.DepotId, StringComparison.Ordinal) ||
                             line.Contains("download_depot", StringComparison.OrdinalIgnoreCase) ||
                             line.Contains("Depot download", StringComparison.OrdinalIgnoreCase);
                if (!about) continue;
                LastConsoleLine = line.Length > 300 ? line.Substring(0, 300) : line;
                if (line.Contains("Depot download complete", StringComparison.OrdinalIgnoreCase)) State = Phase.Done;
                else if (line.Contains("Depot download failed", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("download_depot failed", StringComparison.OrdinalIgnoreCase)) State = Phase.Failed;
                else if (State == Phase.Waiting) State = Phase.Downloading;
            }
        }
        catch
        {
            // The folder watch still works.
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return (bytes / 1024d / 1024 / 1024).ToString("0.00") + " GB";
        if (bytes >= 1024L * 1024) return (bytes / 1024d / 1024).ToString("0.0") + " MB";
        if (bytes >= 1024) return (bytes / 1024d).ToString("0") + " KB";
        return bytes + " B";
    }
}
