using SteambirdsAi;

// Formations the host can edit (ai\formations\*.json). The old formations.json next to the server is still read, and a
// file in the folder wins for the same leader unit. See FormationFile in Shared\EnemyAi.
public static class Formations
{
    public const string LegacyFileName = "formations.json";

    private static FormationLibrary library = new FormationLibrary();

    public static FormationLibrary Library => library;

    public static FormationFile? For(string? unit) => library.ForUnit(unit);

    public static FormationFile? ForAi(string? aiId) => library.ForAi(aiId);

    public static string? DefaultAi(string? unit)
    {
        string ai = library.ForUnit(unit)?.LeaderAi ?? "";
        return ai.Length == 0 ? null : ai;
    }

    // A generic AI does not block the formation registered for the unit. A specific AI brings a formation only when it names one.
    public static FormationFile? Resolve(string? aiId, string? formationOnBrain, string? unit, bool genericAi)
    {
        if (!string.IsNullOrWhiteSpace(formationOnBrain))
        {
            FormationFile? named = library.Get(formationOnBrain);
            if (named != null) return named;
        }
        if (!string.IsNullOrWhiteSpace(aiId))
        {
            FormationFile? byAi = library.ForAi(aiId);
            if (byAi != null) return byAi;
        }
        if (genericAi) return library.ForUnit(unit);
        return null;
    }

    public static void Reload(string aiFolder, AiLibrary? brains)
    {
        var fresh = FormationLibrary.Load(aiFolder);
        string legacy = Path.Combine(Directory.GetCurrentDirectory(), LegacyFileName);
        try
        {
            if (File.Exists(legacy)) fresh.ImportLegacy(File.ReadAllText(legacy));
        }
        catch (Exception e) when (e is IOException || e is System.Text.Json.JsonException)
        {
            fresh.Problems.Add(legacy + ": " + e.Message);
        }
        if (brains != null)
        {
            foreach (KeyValuePair<string, AiBrainFile> pair in brains.BrainFiles)
            {
                string id = (pair.Value.Formation ?? "").Trim();
                if (id.Length > 0) fresh.NoteBrain(pair.Key, id);
            }
        }
        library = fresh;
    }
}
