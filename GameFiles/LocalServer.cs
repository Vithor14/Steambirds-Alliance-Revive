using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SteambirdsGameFiles;

// The server of the package, started only while the game is open for the export. With it the game logs in and loads
// as in normal play on this PC (the launcher's "This PC", "Run game.ps1"): OZY_URL=http://localhost:<httpPort>/ozy and
// -connecttolocal. A server that is already answering on the port (the launcher's, for example) is used as it is.
internal sealed class LocalServer : IDisposable
{
    private readonly object gate = new object();
    private Process? process;
    private StreamWriter? output;

    public int HttpPort { get; private set; } = 6443;
    public int NexusPort { get; private set; } = 1111;
    public bool StartedHere => process != null;

    // Ports of network.json (a JSON file with comments). Missing file or keys keep the defaults.
    public void ReadPorts(string networkFile)
    {
        try
        {
            if (!File.Exists(networkFile)) return;
            var settings = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(networkFile), settings);
            if (document.RootElement.TryGetProperty("httpPort", out JsonElement http) && http.TryGetInt32(out int httpPort) && httpPort > 0)
                HttpPort = httpPort;
            if (document.RootElement.TryGetProperty("nexusPort", out JsonElement nexus) && nexus.TryGetInt32(out int nexusPort) && nexusPort > 0)
                NexusPort = nexusPort;
        }
        catch
        {
            // Defaults.
        }
    }

    // Does a Steambirds server answer GET /revive/info on the HTTP port of this PC?
    public bool IsAnswering()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using HttpResponseMessage response = http.GetAsync("http://localhost:" + HttpPort + "/revive/info").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // Starts SteambirdsServer.exe in its folder, with its output in logFile, and waits until it answers.
    // Returns an error or null.
    public string? Start(string serverExe, string serverFolder, string game, string logFile)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
            output = new StreamWriter(logFile, append: false, new UTF8Encoding(false)) { AutoFlush = true };

            var info = new ProcessStartInfo(serverExe)
            {
                WorkingDirectory = serverFolder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            // The server reads the configBinary of the game and, before the export, the BepInEx folder of the game.
            info.Environment["STEAMBIRDS_GAME"] = game;
            info.Environment["STEAMBIRDS_BEPINEX"] = GameInstallation.BepInExFolder(game);

            process = new Process { StartInfo = info };
            process.OutputDataReceived += (_, e) => WriteOutput(e.Data);
            process.ErrorDataReceived += (_, e) => WriteOutput(e.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            Stopwatch watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(60))
            {
                if (process.HasExited)
                    return "the server closed right after starting (exit code " + process.ExitCode + "); see " + logFile;
                if (IsAnswering()) return null;
                Thread.Sleep(500);
            }
            return "the server did not answer on port " + HttpPort + " in 60 s; see " + logFile;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    // Closes the server started by the tool (a server that was already running is left alone).
    public void Stop()
    {
        if (process == null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10000);
            }
        }
        catch
        {
            // Already closed.
        }
        process.Dispose();
        process = null;
        lock (gate)
        {
            output?.Dispose();
            output = null;
        }
    }

    public void Dispose() => Stop();

    private void WriteOutput(string? line)
    {
        if (line == null) return;
        lock (gate)
        {
            try
            {
                output?.WriteLine(line);
            }
            catch
            {
            }
        }
    }
}
