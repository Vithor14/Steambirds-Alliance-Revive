using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace SteambirdsLauncher.Core;

public enum ServerState { Stopped, Starting, Running, Stopping }

// The server of this PC: server\SteambirdsServer.exe, run the way "Run server.ps1" ran "dotnet run":
//   - working folder = server\ (maps, configs and accounts are read and written there);
//   - STEAMBIRDS_GAME / STEAMBIRDS_BEPINEX point to the game, so the server reads the configBinary and the plugin files;
//   - everything the server prints goes to the console of the Server tab and to logs\server-<date>.log.
public sealed class ServerProcess : IDisposable
{
    // "12:00:00.000 [UDP] [nexus] status 127.0.0.1:50000: Connected (...)" (Program.cs, StartUdp).
    private static readonly Regex StatusLine = new Regex(@"\[UDP\] \[([\w-]+)\] status (\S+): (\w+)", RegexOptions.Compiled);
    private static readonly Regex AnsiCodes = new Regex(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

    private readonly object gate = new object();
    private readonly HashSet<string> connections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private Process? process;
    private StreamWriter? sessionLog;
    private bool stopRequested;

    public ServerState State { get; private set; } = ServerState.Stopped;
    public DateTime? StartedAt { get; private set; }
    public int? LastExitCode { get; private set; }

    // Log of the current (or last) session.
    public string? SessionLogPath { get; private set; }

    public bool IsActive => State != ServerState.Stopped;

    public int? ProcessId
    {
        get
        {
            lock (gate)
            {
                try
                {
                    return process?.Id;
                }
                catch
                {
                    return null;
                }
            }
        }
    }

    // Open UDP connections seen in the log (each player is one connection to the server they are on).
    public int ConnectionCount
    {
        get
        {
            lock (gate) return connections.Count;
        }
    }

    // All events are raised on background threads.
    public event Action<string, bool>? LineReceived;    // text, came from stderr
    public event Action? StateChanged;
    public event Action<bool, int?>? Exited;            // stopped by the launcher, exit code

    public bool Start(string? gameFolder, out string error)
    {
        error = "";
        lock (gate)
        {
            if (process != null)
            {
                error = L.T("The server is already running.");
                return false;
            }
        }

        if (!File.Exists(LauncherPaths.ServerExe))
        {
            error = L.F("The server was not found at:\n{0}\n\nRun \"Build launcher.ps1\" in the project folder: it creates the server folder next to the launcher.", LauncherPaths.ServerExe);
            return false;
        }

        LauncherPaths.EnsureLogsFolder();
        TrimOldLogs(LauncherPaths.LogsFolder, "server-*.log", 30);
        string logPath = Path.Combine(LauncherPaths.LogsFolder, "server-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");

        var startInfo = new ProcessStartInfo(LauncherPaths.ServerExe)
        {
            WorkingDirectory = LauncherPaths.ServerFolder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (!string.IsNullOrWhiteSpace(gameFolder))
        {
            startInfo.Environment["STEAMBIRDS_GAME"] = gameFolder;
            startInfo.Environment["STEAMBIRDS_BEPINEX"] = GameInstall.BepInExFolder(gameFolder);
        }

        StreamWriter writer;
        try
        {
            var stream = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception e)
        {
            error = L.F("Could not create the log {0}: {1}", logPath, e.Message);
            return false;
        }

        var started = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        started.OutputDataReceived += (_, e) => { if (e.Data != null) HandleLine(e.Data, false); };
        started.ErrorDataReceived += (_, e) => { if (e.Data != null) HandleLine(e.Data, true); };
        started.Exited += (_, _) => OnProcessExited(started);

        lock (gate)
        {
            process = started;
            sessionLog = writer;
            SessionLogPath = logPath;
            connections.Clear();
            stopRequested = false;
            LastExitCode = null;
            StartedAt = DateTime.Now;
        }

        WriteLog("=== STEAMBIRDS SERVER (launcher " + LauncherInfo.Version + ") ===");
        WriteLog("Started at: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        WriteLog("Executable: " + LauncherPaths.ServerExe);
        WriteLog("Data folder: " + LauncherPaths.ServerFolder);
        WriteLog("Game folder (STEAMBIRDS_GAME): " + (string.IsNullOrWhiteSpace(gameFolder) ? "(not set)" : gameFolder));
        WriteLog("");

        // "Starting" comes before Process.Start: a server that dies at once still ends in "Stopped".
        SetState(ServerState.Starting);
        try
        {
            if (!started.Start()) throw new InvalidOperationException(L.T("Windows did not start the process."));
        }
        catch (Exception e)
        {
            lock (gate)
            {
                process = null;
                sessionLog = null;
                StartedAt = null;
            }
            try
            {
                writer.WriteLine(">> could not start the server: " + e.Message);
                writer.Dispose();
            }
            catch
            {
            }
            started.Dispose();
            SetState(ServerState.Stopped);
            error = L.F("Could not start the server: {0}", e.Message);
            return false;
        }

        NativeMethods.AssignToKillOnCloseJob(started);
        try
        {
            started.BeginOutputReadLine();
            started.BeginErrorReadLine();
        }
        catch (InvalidOperationException)
        {
            // The server already ended (Exited takes care of the state).
        }
        return true;
    }

    // Ends the server. It saves its files (account.json, inventory.json...) as soon as they change, so nothing is lost.
    public void Stop()
    {
        Process? running;
        lock (gate)
        {
            running = process;
            if (running == null) return;
            stopRequested = true;
        }

        SetState(ServerState.Stopping);
        WriteLog(">> stop requested by the launcher");
        try
        {
            if (!running.HasExited) running.Kill(entireProcessTree: true);
        }
        catch (Exception e)
        {
            WriteLog(">> error stopping the server: " + e.Message);
        }
    }

    // Blocks until the server is gone (used when the launcher closes).
    public void StopAndWait(int milliseconds)
    {
        Process? running;
        lock (gate) running = process;
        Stop();
        try
        {
            running?.WaitForExit(milliseconds);
        }
        catch
        {
        }
    }

    private void HandleLine(string rawLine, bool fromStdErr)
    {
        string line = rawLine.IndexOf('\x1B') >= 0 ? AnsiCodes.Replace(rawLine, "") : rawLine;
        WriteLog(fromStdErr ? "[stderr] " + line : line);

        // Program.cs prints "[HTTP] listening on http://localhost:6443" right before app.Run();
        //         // ASP.NET prints "Now listening on" when the HTTP port is really open.
        if (State == ServerState.Starting &&
            (line.Contains("listening on http://", StringComparison.OrdinalIgnoreCase) ||
             line.Contains("Now listening on", StringComparison.OrdinalIgnoreCase)))
        {
            SetState(ServerState.Running);
        }

        Match status = StatusLine.Match(line);
        if (status.Success)
        {
            string key = status.Groups[1].Value + "|" + status.Groups[2].Value;
            string value = status.Groups[3].Value;
            lock (gate)
            {
                if (value.Equals("Connected", StringComparison.OrdinalIgnoreCase)) connections.Add(key);
                else if (value.Equals("Disconnected", StringComparison.OrdinalIgnoreCase)) connections.Remove(key);
            }
        }

        LineReceived?.Invoke(line, fromStdErr);
    }

    private void OnProcessExited(Process exited)
    {
        // WaitForExit() without a timeout also waits until all redirected output was read.
        try
        {
            exited.WaitForExit();
        }
        catch
        {
        }

        int? code = null;
        try
        {
            code = exited.ExitCode;
        }
        catch
        {
        }

        bool requested;
        StreamWriter? writer;
        lock (gate)
        {
            if (!ReferenceEquals(process, exited)) return;
            process = null;
            requested = stopRequested;
            writer = sessionLog;
            sessionLog = null;
            connections.Clear();
            LastExitCode = code;
            StartedAt = null;
        }

        try
        {
            writer?.WriteLine("");
            writer?.WriteLine(requested
                ? ">> server stopped by the launcher at " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")
                : ">> the server closed by itself at " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + " (codigo " + code + ")");
            writer?.Dispose();
        }
        catch
        {
        }

        exited.Dispose();
        SetState(ServerState.Stopped);
        Exited?.Invoke(requested, code);
    }

    private void WriteLog(string text)
    {
        lock (gate)
        {
            try
            {
                sessionLog?.WriteLine(text);
            }
            catch
            {
                // A full disk must not stop the server output.
            }
        }
    }

    private void SetState(ServerState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke();
    }

    private static void TrimOldLogs(string folder, string pattern, int keep)
    {
        try
        {
            FileInfo[] files = new DirectoryInfo(folder).GetFiles(pattern).OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
            for (int i = keep; i < files.Length; i++)
            {
                try
                {
                    files[i].Delete();
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        StopAndWait(3000);
    }
}
