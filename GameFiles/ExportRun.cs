using System.Diagnostics;

namespace SteambirdsGameFiles;

// Opens the game in export mode and waits for the Steambirds Quieter plugin (QuieterPlugin\GameFilesExport.cs) to
// write export_done.txt. In that mode (environment variable STEAMBIRDS_EXPORT = export folder) the plugin waits for the
// game to load, writes its data files to the BepInEx folder, draws the map editor pictures into the export folder and
// closes the game.
internal static class ExportRun
{
    public const string EnvironmentVariable = "STEAMBIRDS_EXPORT";
    public const string DoneFileName = "export_done.txt";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan ReportEvery = TimeSpan.FromSeconds(20);

    public sealed class Result
    {
        // export_done.txt was written by the plugin.
        public bool Finished { get; init; }

        // "ok", "partial" or "failed" (from the plugin), or the reason the tool stopped waiting.
        public string Outcome { get; init; } = "";

        public List<string> Lines { get; init; } = new List<string>();

        public string Value(string key)
        {
            string prefix = key + "=";
            foreach (string line in Lines)
            {
                if (line.StartsWith(prefix, StringComparison.Ordinal)) return line.Substring(prefix.Length);
            }
            return "";
        }

        public IEnumerable<string> Values(string key)
        {
            string prefix = key + "=";
            return Lines.Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).Select(line => line.Substring(prefix.Length));
        }
    }

    public static Result Run(string game, string exportFolder, int httpPort, int nexusPort, bool connectToLocalServer)
    {
        Directory.CreateDirectory(exportFolder);
        string done = Path.Combine(exportFolder, DoneFileName);
        if (File.Exists(done)) File.Delete(done);

        // Same arguments as the launcher's "This PC" (Launcher\Core\GameProcess.cs).
        var arguments = new List<string>();
        if (connectToLocalServer)
        {
            arguments.Add("-connecttolocal");
            if (nexusPort != 1111) arguments.Add("-connecttolocalport " + nexusPort);
        }

        var info = new ProcessStartInfo(Path.Combine(game, GameInstallation.ExeName))
        {
            WorkingDirectory = game,
            Arguments = string.Join(" ", arguments),
            UseShellExecute = false
        };
        info.Environment["OZY_URL"] = "http://localhost:" + httpPort + "/ozy";
        info.Environment[EnvironmentVariable] = exportFolder;

        ConsoleUi.Info("opening the game: " + info.FileName + " " + info.Arguments);
        using Process? gameProcess = Process.Start(info);
        if (gameProcess == null)
            return new Result { Outcome = "the game did not start" };

        ConsoleUi.Info("waiting for the export (the game closes by itself when it is done)...");
        Stopwatch watch = Stopwatch.StartNew();
        TimeSpan nextReport = ReportEvery;
        while (!File.Exists(done))
        {
            if (gameProcess.HasExited)
            {
                // The plugin writes the file just before closing the game.
                Thread.Sleep(1500);
                if (File.Exists(done)) break;
                return new Result { Outcome = "the game closed before the export finished (exit code " + SafeExitCode(gameProcess) + ")" };
            }

            if (watch.Elapsed > Timeout)
            {
                ConsoleUi.Warn("the export did not finish in " + (int)Timeout.TotalMinutes + " minutes.");
                if (ConsoleUi.Ask("Close the game?", true))
                {
                    try { gameProcess.Kill(); } catch { }
                }
                return new Result { Outcome = "the export did not finish in " + (int)Timeout.TotalMinutes + " minutes" };
            }

            if (watch.Elapsed >= nextReport)
            {
                nextReport += ReportEvery;
                ConsoleUi.Info((int)watch.Elapsed.TotalSeconds + " s... " + CountPictures(exportFolder) + " picture(s) so far");
            }
            Thread.Sleep(1000);
        }

        List<string> lines = ReadDone(done);

        // The plugin calls Application.Quit; if the game is still open after 30 s, it is closed here.
        if (!gameProcess.WaitForExit(30000))
        {
            ConsoleUi.Info("the game is still open after the export: closing it.");
            try { gameProcess.Kill(); } catch { }
        }

        string outcome = lines.FirstOrDefault(line => line.StartsWith("result=", StringComparison.Ordinal))?.Substring("result=".Length) ?? "";
        return new Result { Finished = true, Lines = lines, Outcome = outcome.Length > 0 ? outcome : "unknown" };
    }

    public static int CountPictures(string exportFolder)
    {
        int count = 0;
        foreach (string sub in new[] { "ground", "units" })
        {
            try
            {
                string folder = Path.Combine(exportFolder, sub);
                if (Directory.Exists(folder)) count += Directory.GetFiles(folder, "*.png").Length;
            }
            catch
            {
            }
        }
        return count;
    }

    private static List<string> ReadDone(string done)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                return File.ReadAllLines(done).ToList();
            }
            catch (IOException)
            {
                Thread.Sleep(300);
            }
        }
        return new List<string>();
    }

    private static string SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode.ToString();
        }
        catch
        {
            return "?";
        }
    }
}
