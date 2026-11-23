// Texts of the game for the messages the server sends in the chat ("X joined the flock.", "You got kicked..."). They are the client's
// own lines (Texts/<language>/ui of the configBinary), in the language of the player who reads them; the English text given here is
// only used when the game's data is not loaded or the key is missing.
public static class SocialText
{
    public static string Ui(string? language, string key, string fallback, params object[] args)
    {
        string lang = string.IsNullOrEmpty(language) ? "en-US" : language;
        string text = ConfigBinary.Get("Texts/" + lang + "/ui")[key].AsString("");
        if (text.Length == 0 && lang != "en-US") text = ConfigBinary.Get("Texts/en-US/ui")[key].AsString("");
        if (text.Length == 0) text = fallback;
        if (args.Length == 0) return text;
        try
        {
            return string.Format(text, args);
        }
        catch (FormatException)
        {
            return text;
        }
    }
}
