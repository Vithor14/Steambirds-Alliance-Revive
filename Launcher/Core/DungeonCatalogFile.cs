using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SteambirdsLauncher.Core;

// server\dungeons.json (read by Dungeons\DungeonCatalog.cs): which dungeons the host offers, the map file of each one,
// when each one opens and how much health its enemies have. The server reads it again when it changes.
// Saving only touches the fields the launcher edits; every other field (notes, portal positions...) is kept.
public static class DungeonCatalogFile
{
    public const string FileName = "dungeons.json";
    public static string FilePath => LauncherPaths.ServerFile(FileName);

    // Format of "opensAt" accepted by the server (DateTime.TryParse with the invariant culture, local time).
    public const string OpensAtFormat = "yyyy-MM-dd HH:mm";

    // The original maps of the Nexus and of the tutorial (the server uses them when the fields are empty).
    public const string DefaultNexusMap = "nexus";
    public const string DefaultTutorialMap = "tutorial_walker";

    public sealed class Dungeon
    {
        public string Type { get; set; } = "";       // "type": terrain of the client, e.g. DungeonBomb (not editable)
        public string Map { get; set; } = "";        // "map": file in server\maps without .json
        public string Name { get; set; } = "";       // "name"
        public bool Active { get; set; } = true;     // "enabled"
        public bool InNexus { get; set; } = true;    // "inNexus"
        public double HealthMultiplier { get; set; } = 1.0;  // "healthMultiplier"
        public string OpensAt { get; set; } = "";    // "opensAt"
        public string Note { get; set; } = "";       // "note"
    }

    public sealed class Catalog
    {
        public List<Dungeon> Dungeons { get; } = new List<Dungeon>();
        public int MaxPlayers { get; set; } = 10;               // "maxPlayers"
        public double AutoExitSeconds { get; set; } = 180;      // "autoExitSeconds"
        public bool NexusPortalsOn { get; set; } = true;        // "nexusPortals.enabled"
        public string EntryMode { get; set; } = "both";         // "entryMode": both, portals or missionTransport
        public bool LockBossRooms { get; set; }                 // "lockBossRooms"
        public double FireRateScale { get; set; } = 1.0;        // "fireRateScale"
        public double MaxBulletsPerSecond { get; set; } = 10;   // "maxBulletsPerSecond"
        public string NexusMap { get; set; } = DefaultNexusMap;         // "nexusMap": map file of the Nexus
        public string TutorialMap { get; set; } = DefaultTutorialMap;   // "tutorialMap": map file of the tutorial
    }

    private static readonly JsonNodeOptions NodeOptions = new JsonNodeOptions { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static bool Exists => File.Exists(FilePath);

    public static Catalog Load()
    {
        JsonObject root = ReadRoot(out _);
        var catalog = new Catalog
        {
            MaxPlayers = (int)GetNumber(root, "maxPlayers", 10),
            AutoExitSeconds = GetNumber(root, "autoExitSeconds", 180),
            EntryMode = GetString(root, "entryMode", "both"),
            LockBossRooms = GetBool(root, "lockBossRooms", false),
            FireRateScale = GetNumber(root, "fireRateScale", 1.0),
            MaxBulletsPerSecond = GetNumber(root, "maxBulletsPerSecond", 10),
            NexusMap = GetString(root, "nexusMap", DefaultNexusMap),
            TutorialMap = GetString(root, "tutorialMap", DefaultTutorialMap)
        };
        if (catalog.NexusMap.Trim().Length == 0) catalog.NexusMap = DefaultNexusMap;
        if (catalog.TutorialMap.Trim().Length == 0) catalog.TutorialMap = DefaultTutorialMap;
        if (root["nexusPortals"] is JsonObject portals) catalog.NexusPortalsOn = GetBool(portals, "enabled", true);

        if (root["dungeons"] is JsonArray list)
        {
            foreach (JsonNode? node in list)
            {
                if (node is not JsonObject entry) continue;
                string type = GetString(entry, "type", "");
                if (type.Length == 0) continue;
                catalog.Dungeons.Add(new Dungeon
                {
                    Type = type,
                    Map = GetString(entry, "map", ""),
                    Name = GetString(entry, "name", ""),
                    Active = GetBool(entry, "enabled", true),
                    InNexus = GetBool(entry, "inNexus", true),
                    HealthMultiplier = GetNumber(entry, "healthMultiplier", 1.0),
                    OpensAt = GetString(entry, "opensAt", ""),
                    Note = GetString(entry, "note", "")
                });
            }
        }
        return catalog;
    }

    // Writes the edited values into the current file (read again now, so changes made meanwhile by hand are kept).
    public static void Save(Catalog catalog)
    {
        JsonObject root = ReadRoot(out bool hasBom);
        root["maxPlayers"] = catalog.MaxPlayers;
        root["autoExitSeconds"] = catalog.AutoExitSeconds;
        root["entryMode"] = catalog.EntryMode;
        root["lockBossRooms"] = catalog.LockBossRooms;
        root["fireRateScale"] = catalog.FireRateScale;
        root["maxBulletsPerSecond"] = catalog.MaxBulletsPerSecond;
        root["nexusMap"] = catalog.NexusMap;
        root["tutorialMap"] = catalog.TutorialMap;
        if (root["nexusPortals"] is JsonObject portals) portals["enabled"] = catalog.NexusPortalsOn;
        else root["nexusPortals"] = new JsonObject { ["enabled"] = catalog.NexusPortalsOn };

        if (root["dungeons"] is not JsonArray list)
        {
            list = new JsonArray();
            root["dungeons"] = list;
        }

        foreach (Dungeon dungeon in catalog.Dungeons)
        {
            JsonObject? entry = list.OfType<JsonObject>()
                .FirstOrDefault(o => string.Equals(GetString(o, "type", ""), dungeon.Type, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                entry = new JsonObject { ["type"] = dungeon.Type };
                list.Add(entry);
            }
            entry["map"] = dungeon.Map;
            entry["name"] = dungeon.Name;
            entry["enabled"] = dungeon.Active;
            entry["inNexus"] = dungeon.InNexus;
            entry["healthMultiplier"] = dungeon.HealthMultiplier;
            entry["opensAt"] = dungeon.OpensAt;
            entry["note"] = dungeon.Note;
        }

        TextFiles.WriteAtomic(FilePath, root.ToJsonString(WriteOptions), hasBom);
    }

    // Empty = open now. Otherwise it must be a date the server understands.
    public static bool IsValidOpensAt(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _);
    }

    // Map files available for the dungeons: server\maps\*.json, without the extension.
    public static List<string> AvailableMaps()
    {
        try
        {
            if (!Directory.Exists(LauncherPaths.MapsFolder)) return new List<string>();
            return Directory.GetFiles(LauncherPaths.MapsFolder, "*.json", SearchOption.TopDirectoryOnly)
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .OrderBy(n => n.StartsWith("dungeon", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static JsonObject ReadRoot(out bool hasBom)
    {
        string text = TextFiles.ReadKeepingBom(FilePath, out hasBom);
        JsonNode? node = JsonNode.Parse(text, NodeOptions, JsoncFile.ParseOptions);
        return node as JsonObject ?? throw new FormatException(L.F("{0} is not a JSON object", FileName));
    }

    private static string GetString(JsonObject obj, string key, string fallback)
    {
        if (obj[key] is JsonValue value && value.TryGetValue(out string? text) && text != null) return text;
        return fallback;
    }

    private static bool GetBool(JsonObject obj, string key, bool fallback)
    {
        if (obj[key] is JsonValue value && value.TryGetValue(out bool flag)) return flag;
        return fallback;
    }

    private static double GetNumber(JsonObject obj, string key, double fallback)
    {
        if (obj[key] is JsonValue value)
        {
            if (value.TryGetValue(out double number)) return number;
            if (value.TryGetValue(out string? text) &&
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) return parsed;
        }
        return fallback;
    }
}
