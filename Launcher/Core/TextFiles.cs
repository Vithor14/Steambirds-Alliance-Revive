using System.Text;

namespace SteambirdsLauncher.Core;

// Small file helpers shared by the launcher.
public static class TextFiles
{
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly UTF8Encoding Utf8WithBom = new UTF8Encoding(true);

    // Reads a text file that another program may have open (the game and the server keep their logs open).
    public static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static List<string> ReadLinesShared(string path)
    {
        var lines = new List<string>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: true);
        string? line;
        while ((line = reader.ReadLine()) != null) lines.Add(line);
        return lines;
    }

    // Reads a config file and tells whether it starts with a UTF-8 BOM, so it can be written back the same way.
    public static string ReadKeepingBom(string path, out bool hasBom)
    {
        byte[] bytes = File.ReadAllBytes(path);
        hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return hasBom ? Utf8NoBom.GetString(bytes, 3, bytes.Length - 3) : Utf8NoBom.GetString(bytes);
    }

    // Writes to a temporary file and moves it over the target, so the server never reads half a file.
    // The server may have the file open for a moment while it re-reads it: the move is retried a few times.
    public static void WriteAtomic(string path, string text, bool withBom)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        Directory.CreateDirectory(folder);
        string temp = Path.Combine(folder, "." + Path.GetFileName(path) + ".launcher-tmp");
        File.WriteAllText(temp, text, withBom ? Utf8WithBom : Utf8NoBom);

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 10)
            {
                Thread.Sleep(100);
            }
            catch
            {
                try { File.Delete(temp); } catch { }
                throw;
            }
        }
    }
}
