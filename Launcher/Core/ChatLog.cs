namespace SteambirdsLauncher.Core;

// The chat of the running server, kept for the "Chat" page of the Server tab. The server prints one structured line per
// chat message (Server\Network\ChatMonitor.cs):
//   HH:mm:ss.fff [CHATMON] channel TAB room TAB mapLabel TAB sender TAB target TAB text
// The launcher reads those lines here instead of showing them in the console. Thread-safe.
public sealed class ChatLog
{
    public const string Marker = "[CHATMON] ";

    public sealed class Entry
    {
        public DateTime Time { get; init; }
        public string Channel { get; init; } = "map";   // map (nearby chat), whisper or flock
        public string Room { get; init; } = "";         // stable key of the map instance: nexus, world, dungeon:3
        public string MapLabel { get; init; } = "";
        public string Sender { get; init; } = "";
        public string Target { get; init; } = "";       // whisper recipient or flock name
        public string Text { get; init; } = "";
    }

    private readonly object gate = new object();
    private readonly List<Entry> entries = new List<Entry>();
    private readonly int maxEntries;
    private long first;                                  // sequence number of entries[0]
    private long generation;

    public ChatLog(int maxEntries)
    {
        this.maxEntries = maxEntries;
    }

    // Goes up every time the log is cleared, so the views know they must start over.
    public long Generation
    {
        get
        {
            lock (gate) return generation;
        }
    }

    // Reads a server line. False when it is not a chat monitor line (the caller shows it in the console as usual).
    public bool TryAddLine(string line)
    {
        int at = line.IndexOf(Marker, StringComparison.Ordinal);
        if (at < 0) return false;
        string[] fields = line.Substring(at + Marker.Length).Split('\t', 6);
        if (fields.Length < 6) return true;              // damaged line: dropped, but not shown as console noise
        Add(new Entry
        {
            Time = DateTime.Now,
            Channel = fields[0],
            Room = fields[1],
            MapLabel = fields[2],
            Sender = fields[3],
            Target = fields[4],
            Text = fields[5]
        });
        return true;
    }

    public void Add(Entry entry)
    {
        lock (gate)
        {
            entries.Add(entry);
            if (entries.Count > maxEntries + 500)
            {
                int remove = entries.Count - maxEntries;
                entries.RemoveRange(0, remove);
                first += remove;
            }
        }
    }

    // Entries after sequence number "since". truncated = some of them already left the log (start over).
    public List<Entry> Since(long since, out long end, out bool truncated, out long currentGeneration)
    {
        lock (gate)
        {
            end = first + entries.Count;
            currentGeneration = generation;
            truncated = since < first;
            int start = truncated ? 0 : (int)Math.Min(entries.Count, since - first);
            return entries.GetRange(start, entries.Count - start);
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            first += entries.Count;
            entries.Clear();
            generation++;
        }
    }
}
