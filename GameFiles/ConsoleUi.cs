using System.Text;

namespace SteambirdsGameFiles;

// Console output of the tool. Everything shown is also written to the log file (logs\game-files.log).
internal static class ConsoleUi
{
    private static readonly object Gate = new object();
    private static StreamWriter? log;

    // False with --no-pause: questions take their default answer and the tool never waits for Enter.
    public static bool Interactive { get; set; } = true;

    public static string? LogPath { get; private set; }

    public static void OpenLog(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            log = new StreamWriter(path, append: false, new UTF8Encoding(false)) { AutoFlush = true };
            LogPath = path;
            Write("Steambirds Game Files - " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
        }
        catch
        {
            log = null;
            LogPath = null;
        }
    }

    public static void Close()
    {
        lock (Gate)
        {
            log?.Dispose();
            log = null;
        }
    }

    public static void Say(string text = "", ConsoleColor? color = null)
    {
        if (color.HasValue) Console.ForegroundColor = color.Value;
        Console.WriteLine(text);
        if (color.HasValue) Console.ResetColor();
        Write(text);
    }

    public static void Title(string text)
    {
        Say();
        Say("=== " + text + " ===", ConsoleColor.Cyan);
    }

    public static void Info(string text) => Say("  " + text);

    public static void Done(string text) => Say("  " + text, ConsoleColor.Green);

    public static void Warn(string text) => Say("  WARNING: " + text, ConsoleColor.Yellow);

    public static void Error(string text) => Say("ERROR: " + text, ConsoleColor.Red);

    // Yes/no question. Enter alone takes the default; "s" (sim) also means yes.
    public static bool Ask(string question, bool defaultYes)
    {
        string hint = defaultYes ? " (Y/n) " : " (y/N) ";
        if (!Interactive)
        {
            Say(question + hint + (defaultYes ? "Y" : "N") + " (automatic)");
            return defaultYes;
        }
        Console.Write(question + hint);
        string answer = (Console.ReadLine() ?? "").Trim();
        Write(question + hint + answer);
        if (answer.Length == 0) return defaultYes;
        char first = char.ToLowerInvariant(answer[0]);
        return first == 'y' || first == 's';
    }

    public static void WaitForEnter(string text)
    {
        if (!Interactive) return;
        Console.Write(text);
        Console.ReadLine();
    }

    // Writes only to the log file (details that would flood the console).
    public static void Write(string text)
    {
        lock (Gate)
        {
            try
            {
                log?.WriteLine(text);
            }
            catch
            {
                // The log is a help, never a reason to stop.
            }
        }
    }
}
