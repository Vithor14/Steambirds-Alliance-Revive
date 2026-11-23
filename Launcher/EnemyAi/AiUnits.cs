using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;

namespace SteambirdsLauncher.EnemyAi;

// One thing the Enemy AI tab can put in the simulator: an enemy type of enemies.json (a game unit plus the AI and the
// settings the server gives it) or a plain game unit (a test dummy that has no AI of its own).
internal sealed class AiUnitChoice
{
    public string Label { get; init; } = "";

    // The game unit that is drawn and flown (units_config.txt). Empty = the built-in test plane.
    public string UnitName { get; init; } = "";

    // The enemy type of enemies.json this comes from (empty for a plain unit).
    public string TypeId { get; init; } = "";
    public string TypeName { get; init; } = "";

    // "ai" of the type: the AI file the unit really runs. Empty when the type uses a built-in behavior.
    public string OwnAi { get; init; } = "";
    public string BuiltInBehavior { get; init; } = "";

    // What the type says about flying and shooting (enemies.json).
    public float SpeedMult { get; init; } = 1f;
    public float TurnMult { get; init; } = 1f;
    public float FireRateMult { get; init; } = 1f;
    public float AimLead { get; init; }
    public bool FreeAim { get; init; }

    public bool IsType => TypeId.Length > 0;
}

// Builds the list of units of the tab from enemies.json and the game's unit catalog.
internal static class AiUnits
{
    // The guns of the unit that is in the simulator: the weapons checklist of the forms shows these.
    public static IReadOnlyList<SteambirdsAi.AiGunStats> CurrentGuns { get; set; } = Array.Empty<SteambirdsAi.AiGunStats>();

    // "mapTypes": unit types of the map editor the tab was asked to open (they go first).
    public static List<AiUnitChoice> Build(string serverFolder, bool armedOnly, IEnumerable<AiUnitChoice>? mapTypes = null)
    {
        var list = new List<AiUnitChoice>();
        if (mapTypes != null) list.AddRange(mapTypes);
        list.AddRange(ReadTypes(System.IO.Path.Combine(serverFolder, "enemies.json")));
        IEnumerable<GameUnit> units = EditorAssets.Current.Units.Values.Where(u => !u.Wall);
        if (armedOnly) units = units.Where(u => u.Armed);
        foreach (GameUnit unit in units.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase))
            list.Add(new AiUnitChoice { Label = unit.Name, UnitName = unit.Name });
        if (list.Count(c => !c.IsType) == 0) list.Add(new AiUnitChoice { Label = L.T("(test plane)") });
        return list;
    }

    // The enemy types, those with an AI first. A type whose game unit is not in the catalog still shows (with the test plane).
    private static IEnumerable<AiUnitChoice> ReadTypes(string path)
    {
        var found = new List<AiUnitChoice>();
        try
        {
            if (!File.Exists(path)) return found;
            JsonNode? root = JsonNode.Parse(TextFiles.ReadShared(path), null, JsoncFile.ParseOptions);
            if (root is not JsonObject top || top["types"] is not JsonObject types) return found;
            foreach (KeyValuePair<string, JsonNode?> entry in types)
            {
                if (entry.Value is not JsonObject type) continue;
                found.Add(FromType(entry.Key, type, L.T("type")));
            }
        }
        catch (Exception e) when (e is System.Text.Json.JsonException || e is IOException || e is UnauthorizedAccessException)
        {
            // enemies.json is not readable now: the tab still works with plain units.
        }
        return found.OrderBy(c => c.OwnAi.Length > 0 ? 0 : 1).ThenBy(c => c.TypeId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // One unit type (of enemies.json, or of the "types" of a map) as something the simulator can run. "kind" is the first word
    // of the label: "type" for enemies.json, "map type" for a type of the map being edited.
    public static AiUnitChoice FromType(string key, JsonObject type, string kind)
    {
        string ai = Json.Str(type, "ai").Trim();
        string behavior = Json.Str(type, "behavior").Trim();
        GameUnit? unit = Resolve(Json.Str(type, "template"));
        string name = Json.Str(type, "name");
        string label = kind + ": " + key + (name.Length > 0 && !name.Equals(key, StringComparison.OrdinalIgnoreCase) ? " (" + name + ")" : "") +
                       " - " + (ai.Length > 0 ? "AI " + ai : behavior.Length > 0 ? L.T("built-in") + " " + behavior : L.T("no AI"));
        return new AiUnitChoice
        {
            Label = label,
            UnitName = unit?.Name ?? "",
            TypeId = key,
            TypeName = name.Length > 0 ? name : key,
            OwnAi = ai,
            BuiltInBehavior = behavior,
            SpeedMult = Positive(type, "speedMultiplier"),
            TurnMult = Positive(type, "turnMultiplier"),
            FireRateMult = Positive(type, "fireRateMultiplier"),
            AimLead = (float)Math.Max(0.0, Json.Num(type, "aimLead")),
            FreeAim = Json.Bool(type, "freeAim")
        };
    }

    private static float Positive(JsonObject type, string key)
    {
        double v = Json.Num(type, key);
        return v > 0.0 ? (float)v : 1f;
    }

    // "name|auto:weak": the first option that exists wins (the server's rule; its auto: choice also looks at island
    // missions and player planes, which the launcher does not know, so an auto: unit can differ from the real one).
    public static GameUnit? Resolve(string template)
    {
        foreach (string raw in template.Split('|'))
        {
            string option = raw.Trim();
            if (option.Length == 0) continue;
            GameUnit? found = option.StartsWith("auto:", StringComparison.OrdinalIgnoreCase)
                ? Automatic(option.Substring(5).Trim().ToLowerInvariant())
                : EditorAssets.Current.Units.Values.FirstOrDefault(u => string.Equals(u.Name, option, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
        }
        return null;
    }

    private static GameUnit? Automatic(string kind)
    {
        List<GameUnit> armed = EditorAssets.Current.Units.Values
            .Where(u => u.Armed && !u.Wall && u.MaxHealth > 0 && u.Radius > 0)
            .OrderBy(u => u.MaxHealth).ThenBy(u => u.Name, StringComparer.Ordinal).ToList();
        if (armed.Count == 0) return null;
        List<GameUnit> mobile = armed.Where(u => u.VelMax > 0.1 && !u.Boss).ToList();
        List<GameUnit> stationary = armed.Where(u => u.VelMax <= 0.1 && !u.Boss).ToList();
        switch (kind)
        {
            case "weak":
                return armed.FirstOrDefault(u => u.VelMax > 0.1 && !u.Boss && u.Name.Contains("tutorial", StringComparison.OrdinalIgnoreCase))
                       ?? mobile.FirstOrDefault() ?? stationary.FirstOrDefault();
            case "medium":
                return Percentile(mobile, 0.5f) ?? stationary.FirstOrDefault();
            case "strong":
                return Percentile(mobile, 0.85f) ?? stationary.LastOrDefault();
            case "turret":
                return stationary.FirstOrDefault();
            case "boss":
                return armed.FirstOrDefault(u => u.Boss) ?? armed.OrderByDescending(u => u.Guns.Count).ThenBy(u => u.MaxHealth).FirstOrDefault();
        }
        return null;
    }

    private static GameUnit? Percentile(List<GameUnit> sorted, float fraction)
    {
        if (sorted.Count == 0) return null;
        int index = Math.Clamp((int)MathF.Round((sorted.Count - 1) * fraction), 0, sorted.Count - 1);
        return sorted[index];
    }
}
