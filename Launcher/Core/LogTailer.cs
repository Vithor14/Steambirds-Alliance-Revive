using System.Text;

namespace SteambirdsLauncher.Core;

// Follows a log file that another program writes (like "tail -f"): BepInEx\LogOutput.log and the Unity Player.log.
// The game recreates both files when it starts; a file that got shorter is read again from the start.
public sealed class LogTailer : IDisposable
{
    private const int PollMilliseconds = 500;
    private const long MaxCatchUpBytes = 2 * 1024 * 1024;

    private readonly object gate = new object();
    private readonly System.Threading.Timer timer;
    private readonly StringBuilder partialLine = new StringBuilder();
    private readonly Decoder decoder = new UTF8Encoding(false).GetDecoder();
    private readonly byte[] buffer = new byte[64 * 1024];
    private string path;
    private long position;
    private bool disposed;

    public string Name { get; }
    public string FilePath
    {
        get
        {
            lock (gate) return path;
        }
    }

    // Raised on a timer thread.
    public event Action<LogTailer, string>? Line;
    public event Action<LogTailer>? Restarted;

    // fromEnd = skip what is already in the file (only new lines are shown).
    public LogTailer(string name, string path, bool fromEnd)
    {
        Name = name;
        this.path = path;
        if (fromEnd) position = CurrentLength(path);
        timer = new System.Threading.Timer(_ => Poll(), null, PollMilliseconds, PollMilliseconds);
    }

    public void ChangePath(string newPath, bool fromEnd)
    {
        lock (gate)
        {
            path = newPath;
            position = fromEnd ? CurrentLength(newPath) : 0;
            partialLine.Clear();
            decoder.Reset();
        }
    }

    private void Poll()
    {
        if (!Monitor.TryEnter(gate)) return;
        try
        {
            if (!disposed) PollLocked();
        }
        catch
        {
            // The file may be locked or replaced for a moment; the next poll tries again.
        }
        finally
        {
            Monitor.Exit(gate);
        }
    }

    private void PollLocked()
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        long length = new FileInfo(path).Length;

        if (length < position)
        {
            // Recreated (the game started again): read it from the start.
            position = 0;
            partialLine.Clear();
            decoder.Reset();
            Restarted?.Invoke(this);
        }
        if (length == position) return;

        // A huge backlog (for example a log several MB long) is cut to its last part.
        if (length - position > MaxCatchUpBytes) position = length - MaxCatchUpBytes;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Seek(position, SeekOrigin.Begin);
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            position += read;
            int charCount = decoder.GetCharCount(buffer, 0, read);
            var chars = new char[charCount];
            decoder.GetChars(buffer, 0, read, chars, 0);
            Emit(chars);
        }
    }

    private void Emit(char[] chars)
    {
        foreach (char c in chars)
        {
            if (c == '\n')
            {
                string line = partialLine.ToString().TrimEnd('\r');
                partialLine.Clear();
                Line?.Invoke(this, line);
            }
            else
            {
                partialLine.Append(c);
            }
        }
    }

    private static long CurrentLength(string file)
    {
        try
        {
            return string.IsNullOrWhiteSpace(file) || !File.Exists(file) ? 0 : new FileInfo(file).Length;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        lock (gate) disposed = true;
        timer.Dispose();
    }
}
