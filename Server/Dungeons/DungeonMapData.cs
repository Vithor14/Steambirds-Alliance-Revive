using System.Text.Json;

// Dungeon part of a map file (maps\dungeon_*.json). Maps reads the geometry of the same file (floor, walls,
// points, areas, gates, checkpoints, signs); this class reads what only the dungeon server uses:
//   - "types": unit types of the map, with the fields of enemies.json plus the dungeon extras (onDeath, sequence,
//     barks, durationSeconds, periodicSummon, dpsChest, miniboss);
//   - "encounters": one per room (units with positions, or waves);
//   - "gates" again (for "opensWhen" and "room", which Maps does not keep) and "sideGates";
//   - "routes", "exit", "nextDungeon" and the metadata (key, portalItem, quest).
// The format is described in maps\README.md.
//
// Unit types are registered in Enemies with the key "<map id>/<type>", so two maps can use the same name
// for different types and nothing collides with enemies.json. "summon" of the boss phases is renamed the same way.
public static class DungeonMapData
{
    // ================================================================== file (JSON)

    public sealed class DungeonFile
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Terrain { get; set; } = "";
        public string Source { get; set; } = "";
        public string Key { get; set; } = "";
        public string PortalItem { get; set; } = "";
        public string Quest { get; set; } = "";
        public ExitRule? Exit { get; set; }
        public NextDungeon? NextDungeon { get; set; }
        public Dictionary<string, float>? Values { get; set; }
        public List<GateDef>? Gates { get; set; }
        public List<GateDef>? SideGates { get; set; }
        public Dictionary<string, List<RoutePoint>>? Routes { get; set; }
        public Dictionary<string, JsonElement>? Types { get; set; }
        public List<EncounterDef>? Encounters { get; set; }
    }

    public sealed class ExitRule
    {
        public string Point { get; set; } = "portal";
        public string When { get; set; } = "";
    }

    public sealed class NextDungeon
    {
        public string Map { get; set; } = "";
        public string Point { get; set; } = "";
        public string When { get; set; } = "";
    }

    // Gate: a row (sj, siDe..siAte) in "gates" or a column (si, sjDe..sjAte) in "sideGates".
    public sealed class GateDef
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public int Sj { get; set; }
        public int SiFrom { get; set; }
        public int SiTo { get; set; }
        public int Si { get; set; }
        public int SjFrom { get; set; }
        public int SjTo { get; set; }
        public bool Damageable { get; set; }
        public string OpensWhen { get; set; } = "";
        // Room the gate leads into (used by "closesOnEnter").
        public string Room { get; set; } = "";
    }

    public sealed class RoutePoint
    {
        public float U { get; set; }
        public float V { get; set; }
    }

    public sealed class PosDef
    {
        public float U { get; set; }
        public float V { get; set; }
        public float Rot { get; set; }
    }

    public sealed class PlacedUnits
    {
        public string Type { get; set; } = "";
        public List<PosDef>? Pos { get; set; }
        public string Note { get; set; } = "";
        public string Route { get; set; } = "";
        public string Behavior { get; set; } = "";
        public bool Miniboss { get; set; }
        // Empty = with the room. "death:<type>" = only appears when a unit of that type dies (rewards, portals).
        public string Wakes { get; set; } = "";
    }

    public sealed class WaveDef
    {
        public string Name { get; set; } = "";
        public float DelayBeforeSeconds { get; set; } = 4f;
        public string Message { get; set; } = "";
        public List<PlacedUnits>? Groups { get; set; }
    }

    public sealed class EncounterDef
    {
        public string Room { get; set; } = "";
        public string Area { get; set; } = "";
        public string Line { get; set; } = "";
        // onEnter (the player enters the room area), script / drill (scripted; for now also when entering),
        //         // death:<type> (when a unit of that type dies).
        public string Wakes { get; set; } = "onEnter";
        public bool Boss { get; set; }
        public bool Miniboss { get; set; }
        public bool ClosesOnEnter { get; set; }
        public bool AltarRoom { get; set; }
        public string Note { get; set; } = "";
        public List<string>? Controllers { get; set; }
        public List<PlacedUnits>? Enemies { get; set; }
        public List<WaveDef>? Waves { get; set; }
    }

    // Dungeon fields of a unit type (the rest goes to Enemies.TipoDeInimigo).
    public sealed class UnitExtras
    {
        public bool Miniboss { get; set; }
        // Type that appears where the unit died (wrecks "*DEATH", reward chests).
        public string OnDeath { get; set; } = "";
        // Types that appear one after the other where the previous one died (Phoenix eggs, Giant Egg -> Poison God).
        public List<string>? Sequence { get; set; }
        // Bark keys (Texts/bark of the client) by moment: onWake, phase, onDeath...
        public Dictionary<string, string>? Barks { get; set; }
        // > 0: the unit ends by itself after this time, as if destroyed (eggs of a sequence).
        public float DurationSeconds { get; set; }
        // true: the unit never counts to clear its room, even if it is an armed enemy (the invisible ring-firers
        // that appear when a barrel explodes live a second and must not hold the room).
        public bool ExcludeFromRoomClear { get; set; }
        public PeriodicSummon? PeriodicSummon { get; set; }
        public DpsChest? DpsChest { get; set; }
        public string Route { get; set; } = "";
        public string Target { get; set; } = "";
        public string Note { get; set; } = "";
    }

    // The unit calls more units while awake: "count" every "seconds", at most "max" alive at a time,
    // only below "belowHealth" (1 = from the start).
    public sealed class PeriodicSummon
    {
        public string Type { get; set; } = "";
        public float Seconds { get; set; } = 5f;
        public int Count { get; set; } = 1;
        public int Max { get; set; } = 4;
        public float BelowHealth { get; set; } = 1f;
        // false: the summoned units do not count to clear the room (mines dropped by a blimp, bombs laid by a carrier).
        public bool CountsForRoomClear { get; set; } = true;
    }

    // Big treasure (DPS chest of the low tier dungeons, patch of 2019): break it within "seconds" from the first hit
    // and "reward" appears; otherwise it disappears and "fail" appears.
    public sealed class DpsChest
    {
        public float Seconds { get; set; } = 20f;
        public string Reward { get; set; } = "";
        public string Fail { get; set; } = "";
    }

    // ================================================================== loaded data

    public sealed class Loaded
    {
        public string MapId = "";
        public string Path = "";
        public DungeonFile File = new DungeonFile();
        public readonly Dictionary<string, Enemies.EnemyType> Types =
            new Dictionary<string, Enemies.EnemyType>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, UnitExtras> Extras = new Dictionary<string, UnitExtras>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Warnings = new List<string>();
        public float HealthMultiplier = 1f;

        public string Prefix => MapId + "/";
        public string Key(string localType) => Prefix + localType;

        public UnitExtras ExtrasOf(string localType)
        {
            return Extras.TryGetValue(localType, out UnitExtras? e) ? e : Empty;
        }

        public bool HasType(string localType) => Types.ContainsKey(localType);

        public List<EncounterDef> Encounters => File.Encounters ?? new List<EncounterDef>();
    }

    private static readonly UnitExtras Empty = new UnitExtras();

    private sealed class CacheEntry
    {
        public Loaded? Data;
        public DateTime Date;
        public float HealthMultiplier;
    }

    private static readonly Dictionary<string, CacheEntry> cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
    private static readonly object padlock = new object();

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // Dungeon data of a map, read again when the file changes. healthMultiplier (dungeons.json) multiplies the health
    // of every type. Throws InvalidOperationException when the file cannot be read and there is no older version.
    public static Loaded Get(string mapId, float healthMultiplier, Action<string, string> log)
    {
        lock (padlock)
        {
            string path = Maps.FilePath(mapId);
            DateTime date = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            cache.TryGetValue(mapId, out CacheEntry? current);
            if (current?.Data != null && current.Date == date && Math.Abs(current.HealthMultiplier - healthMultiplier) < 0.0001f)
                return current.Data;

            try
            {
                if (!File.Exists(path)) throw new FileNotFoundException("file not found", path);
                Loaded data = Read(mapId, path, healthMultiplier);
                cache[mapId] = new CacheEntry { Data = data, Date = date, HealthMultiplier = healthMultiplier };
                log("DUNGEON", "dungeon data of '" + mapId + "' " + (current?.Data == null ? "loaded" : "reloaded") + ": " +
                    data.Types.Count + " types, " + data.Encounters.Count + " rooms with an encounter, " +
                    (data.File.Gates?.Count ?? 0) + " gates + " + (data.File.SideGates?.Count ?? 0) + " side" +
                    (Math.Abs(healthMultiplier - 1f) > 0.0001f ? ", health x" + Maps.Num(healthMultiplier) : ""));
                foreach (string w in data.Warnings) log("DUNGEON", "warning in " + mapId + ": " + w);
                return data;
            }
            catch (Exception e)
            {
                string reason = e is JsonException j ? "invalid JSON: " + j.Message : e.Message;
                if (current?.Data != null)
                {
                    current.Date = date;
                    log("DUNGEON", "ERROR reloading the dungeon data of " + path + ": " + reason + " (the previous version stays)");
                    return current.Data;
                }
                throw new InvalidOperationException("dungeon data of '" + mapId + "' did not load (" + path + "): " + reason, e);
            }
        }
    }

    private static Loaded Read(string mapId, string path, float healthMultiplier)
    {
        string text = File.ReadAllText(path);
        DungeonFile? file = LegacyNames.Deserialize<DungeonFile>(text, Options);
        if (file == null) throw new InvalidDataException("empty file");

        var data = new Loaded { MapId = mapId, Path = path, File = file, HealthMultiplier = healthMultiplier };
        Dictionary<string, JsonElement> types = file.Types ?? new Dictionary<string, JsonElement>();

        foreach (KeyValuePair<string, JsonElement> pair in types)
        {
            string raw = pair.Value.GetRawText();
            Enemies.EnemyType? type;
            UnitExtras? extras;
            try
            {
                type = JsonSerializer.Deserialize<Enemies.EnemyType>(raw, Options);
                extras = JsonSerializer.Deserialize<UnitExtras>(raw, Options);
            }
            catch (JsonException e)
            {
                data.Warnings.Add("type '" + pair.Key + "' unreadable: " + e.Message);
                continue;
            }
            if (type == null) continue;
            if (string.IsNullOrWhiteSpace(type.Name)) type.Name = pair.Key;
            if (healthMultiplier > 0f) type.HealthMultiplier = (type.HealthMultiplier > 0f ? type.HealthMultiplier : 1f) * healthMultiplier;
            data.Types[pair.Key] = type;
            data.Extras[pair.Key] = extras ?? new UnitExtras();
        }

        // Boss phases call types of the same map: same prefix.
        foreach (Enemies.EnemyType type in data.Types.Values)
        {
            foreach (Enemies.BossPhase phase in type.Phases ?? new List<Enemies.BossPhase>())
            {
                if (string.IsNullOrWhiteSpace(phase.Summon)) continue;
                if (data.Types.ContainsKey(phase.Summon)) phase.Summon = data.Key(phase.Summon);
                else data.Warnings.Add("a phase of '" + type.Name + "' summons '" + phase.Summon + "', which is not in \"types\"");
            }
        }

        // References to types that do not exist: warned once, ignored when the room is built.
        foreach (KeyValuePair<string, UnitExtras> pair in data.Extras)
        {
            UnitExtras e = pair.Value;
            if (e.OnDeath.Length > 0 && !data.HasType(e.OnDeath)) data.Warnings.Add("'" + pair.Key + "' aoMorrer '" + e.OnDeath + "' has no type");
            foreach (string s in e.Sequence ?? new List<string>())
            {
                if (!data.HasType(s)) data.Warnings.Add("'" + pair.Key + "' sequence '" + s + "' has no type");
            }
            if (e.PeriodicSummon != null && !data.HasType(e.PeriodicSummon.Type))
                data.Warnings.Add("'" + pair.Key + "' invocacaoPeriodica '" + e.PeriodicSummon.Type + "' has no type");
            if (e.DpsChest != null)
            {
                if (e.DpsChest.Reward.Length > 0 && !data.HasType(e.DpsChest.Reward))
                    data.Warnings.Add("'" + pair.Key + "' dpsChest.reward '" + e.DpsChest.Reward + "' has no type");
                if (e.DpsChest.Fail.Length > 0 && !data.HasType(e.DpsChest.Fail))
                    data.Warnings.Add("'" + pair.Key + "' dpsChest.fail '" + e.DpsChest.Fail + "' has no type");
            }
        }
        foreach (EncounterDef enc in data.Encounters)
        {
            foreach (PlacedUnits p in AllUnits(enc))
            {
                if (!data.HasType(p.Type)) data.Warnings.Add("room '" + enc.Room + "': type '" + p.Type + "' is not in \"types\"");
            }
        }
        foreach (GateDef g in (file.Gates ?? new List<GateDef>()).Concat(file.SideGates ?? new List<GateDef>()))
        {
            if (!data.HasType(g.Type)) data.Warnings.Add("gate '" + g.Name + "': type '" + g.Type + "' is not in \"types\"");
        }

        Register(data);
        return data;
    }

    // Every unit entry of an encounter (fixed units and waves).
    public static IEnumerable<PlacedUnits> AllUnits(EncounterDef enc)
    {
        foreach (PlacedUnits p in enc.Enemies ?? new List<PlacedUnits>()) yield return p;
        foreach (WaveDef w in enc.Waves ?? new List<WaveDef>())
        {
            foreach (PlacedUnits p in w.Groups ?? new List<PlacedUnits>()) yield return p;
        }
    }

    // Enemies gets each type with the map prefix. A file read again replaces the old types.
    private static void Register(Loaded data)
    {
        foreach (KeyValuePair<string, Enemies.EnemyType> pair in data.Types)
            Enemies.RegisterDefaultType(data.Key(pair.Key), pair.Value);
    }

    // ================================================================== conditions

    // "cleared:room", "death:type", "quest:id"; several joined with "+" (all must be true).
    public static bool Check(string condition, Func<string, bool> roomCleared, Func<string, bool> typeDead)
    {
        if (string.IsNullOrWhiteSpace(condition)) return false;
        foreach (string part in condition.Split('+'))
        {
            string p = part.Trim();
            int colon = p.IndexOf(':');
            if (colon < 0) return false;
            string kind = p.Substring(0, colon).Trim().ToLowerInvariant();
            string value = p.Substring(colon + 1).Trim();
            switch (kind)
            {
                case "cleared":
                    if (!roomCleared(value)) return false;
                    break;
                case "death":
                    if (!typeDead(value)) return false;
                    break;
                case "quest":
                    // Quests are not on the server yet: the gate opens when the dungeon starts.
                    break;
                default:
                    return false;
            }
        }
        return true;
    }
}
