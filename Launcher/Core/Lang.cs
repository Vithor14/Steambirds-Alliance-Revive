using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SteambirdsLauncher.Core;

// Launcher translations.
//
// The text in the code is the English text, and it is also the key of the translation files:
//   lang\pt-BR.json  ->  { "_language": "Português (Brasil)", "Start server": "Iniciar servidor", ... }
// English needs no file. A text with no translation shows in English, and the launcher writes the texts it
// missed to lang\<code>.missing.json when it closes, so translators know what is left.
// To add a language: copy lang\_template.json (every text, empty values) to lang\<code>.json (for example
// es-ES.json), fill in "_language" and translate the values. Keys starting with "_" are information about the
// file, not texts, and files starting with "_" are not languages.
public static class L
{
    public const string English = "en";

    public sealed class LanguageInfo
    {
        public LanguageInfo(string code, string name)
        {
            Code = code;
            Name = name;
        }

        public string Code { get; }
        public string Name { get; }

        public override string ToString() => Name;
    }

    private static readonly object Gate = new object();
    private static Dictionary<string, string> table = new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly HashSet<string> Missing = new HashSet<string>(StringComparer.Ordinal);

    public static string Code { get; private set; } = English;
    public static bool IsEnglish => Code == English;

    // Loads lang\<code>.json. Unknown or broken files fall back to English (the error goes to "error").
    public static void Load(string? code, out string? error)
    {
        error = null;
        var loaded = new Dictionary<string, string>(StringComparer.Ordinal);
        string chosen = string.IsNullOrWhiteSpace(code) ? English : code.Trim();

        if (chosen != English)
        {
            string path = FilePath(chosen);
            try
            {
                if (!File.Exists(path)) throw new FileNotFoundException("file not found", path);
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    if (property.Name.StartsWith("_", StringComparison.Ordinal)) continue;
                    if (property.Value.ValueKind != JsonValueKind.String) continue;
                    string? value = property.Value.GetString();
                    if (!string.IsNullOrEmpty(value)) loaded[property.Name] = value;
                }
            }
            catch (Exception e)
            {
                error = "Could not read the language file " + path + ": " + e.Message;
                chosen = English;
                loaded.Clear();
            }
        }

        lock (Gate)
        {
            table = loaded;
            Code = chosen;
            Missing.Clear();
        }
    }

    // Translation of an English text (the text itself when there is none).
    public static string T(string english)
    {
        lock (Gate)
        {
            if (table.TryGetValue(english, out string? translated)) return translated;
            if (Code != English) Missing.Add(english);
        }
        return english;
    }

    // Translation with {0}, {1}... placeholders.
    public static string F(string english, params object?[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, T(english), args);
    }

    // Whether the current language has its own text for this English text (no "missing" record).
    public static bool Has(string english)
    {
        if (english.Length == 0) return false;
        lock (Gate) return table.ContainsKey(english);
    }

    // English plus every lang\*.json (name from "_language", or the file name).
    public static List<LanguageInfo> Available()
    {
        var list = new List<LanguageInfo> { new LanguageInfo(English, "English") };
        try
        {
            if (!Directory.Exists(LauncherPaths.LangFolder)) return list;
            foreach (string file in Directory.GetFiles(LauncherPaths.LangFolder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string code = Path.GetFileNameWithoutExtension(file);
                if (code.StartsWith("_", StringComparison.Ordinal) ||
                    code.EndsWith(".missing", StringComparison.OrdinalIgnoreCase) ||
                    code.Equals(English, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new LanguageInfo(code, ReadLanguageName(file) ?? code));
            }
        }
        catch
        {
            // A broken folder only leaves English.
        }
        return list;
    }

    // Writes lang\<code>.missing.json with the English texts that had no translation in this session.
    public static void SaveMissing()
    {
        List<string> missing;
        string code;
        lock (Gate)
        {
            if (Code == English || Missing.Count == 0) return;
            missing = Missing.OrderBy(m => m, StringComparer.Ordinal).ToList();
            code = Code;
        }
        try
        {
            Directory.CreateDirectory(LauncherPaths.LangFolder);
            var content = new Dictionary<string, string>
            {
                ["_info"] = "Texts the launcher showed in English because " + code + ".json has no translation for them. " +
                            "Copy the lines into " + code + ".json and translate the values."
            };
            foreach (string text in missing) content[text] = "";
            string json = JsonSerializer.Serialize(content, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            TextFiles.WriteAtomic(Path.Combine(LauncherPaths.LangFolder, code + ".missing.json"), json, withBom: false);
        }
        catch
        {
            // Only a help for translators.
        }
    }

    private static string FilePath(string code) => Path.Combine(LauncherPaths.LangFolder, code + ".json");

    private static string? ReadLanguageName(string file)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            if (document.RootElement.TryGetProperty("_language", out JsonElement name) && name.ValueKind == JsonValueKind.String)
                return name.GetString();
        }
        catch
        {
        }
        return null;
    }
}
