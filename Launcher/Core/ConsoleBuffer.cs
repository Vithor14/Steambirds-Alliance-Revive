namespace SteambirdsLauncher.Core;

// What a console line is, for its color.
public enum LineKind { Normal, Launcher, Error, Warning, Muted, Good }

// Lines of a console (server or client), kept apart from the window that shows them: the launcher can rebuild its
// screens (language change) without losing what the consoles already showed. Thread-safe.
public sealed class ConsoleBuffer
{
    public readonly struct Line
    {
        public Line(string text, LineKind kind)
        {
            Text = text;
            Kind = kind;
        }

        public string Text { get; }
        public LineKind Kind { get; }
    }

    private readonly object gate = new object();
    private readonly List<Line> lines = new List<Line>();
    private long first;   // sequence number of lines[0]

    public ConsoleBuffer(int maxLines)
    {
        MaxLines = maxLines;
    }

    public int MaxLines { get; }

    // Goes up every time the buffer is cleared, so the views know they must start over.
    public long Generation
    {
        get
        {
            lock (gate) return generation;
        }
    }

    private long generation;

    public void Append(string text, LineKind kind)
    {
        lock (gate)
        {
            lines.Add(new Line(text, kind));
            // Old lines leave in chunks, so the views rebuild once every 1000 lines at most.
            if (lines.Count > MaxLines + 1000)
            {
                int remove = lines.Count - MaxLines;
                lines.RemoveRange(0, remove);
                first += remove;
            }
        }
    }

    // Lines after sequence number "since". truncated = some of them already left the buffer (start over).
    public List<Line> Since(long since, out long end, out bool truncated, out long currentGeneration)
    {
        lock (gate)
        {
            end = first + lines.Count;
            currentGeneration = generation;
            truncated = since < first;
            int start = truncated ? 0 : (int)Math.Min(lines.Count, since - first);
            return lines.GetRange(start, lines.Count - start);
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            first += lines.Count;
            lines.Clear();
            generation++;
        }
    }
}
