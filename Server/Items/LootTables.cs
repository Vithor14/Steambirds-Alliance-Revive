using System.Globalization;
using System.Text.Json;

// Loot tables: one JSON file per table in the "loot_tables" folder (Server\loot_tables).
// Adding a table is adding a file: nothing else in the server changes. See loot_tables\README.md.
//
// The format is the game's own LootTableDef (the one the unit configs and quests use), so tables can be copied
// between the game data and these files:
//   { "id": "normal", "description": "...",
//     "replaces": [ "tier7minion", "*leader*" ],      lootTableIds of the game units this table stands in for
//     "units": [ "tutorialwalkerdungeontreasure*" ],  units (templateName) that always use this table
//     "rolls": [ LootTableDef, ... ] }                 each roll draws one item set
//   LootTableDef: { "usableChance": 50, "ensureUsableSlots": ["MainGun"], "conditions": [...],
//                   "lootTable": [ { "weight": 80 }, { "weight": 20, "items": [ { "tier": 1 } ] } ] }
//   item: { "id": "armor_3" } | { "slot": "Armor", "tier": 2 } | { "tier": 1 } (random armor, engine, main gun
//         or secondary) | { "instant": { "currency_id": "newscrap", "currency_amount": 10 } }
//
// The draw is SteambirdsLootTable.DrawLoot from the client: one weighted item set per roll; "tier" is relative to
// the enemy (enemy tier - 1 is added, then clamped to 1..10); "usableChance" (0-100) makes random items come only
// from what the player's class can equip.

// What the draw needs to know about the player (LootDrawDetails).
public sealed class LootContext
{
    public string PlaneClass = "";
    public int PlaneLevel = 1;
    public int PilotLevel = 1;
    public bool IsAdmin;
    public string Difficulty = "normal";
    public HashSet<string> Buffs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> FinishedQuests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

// InstantLoot: rewards delivered right away, without a bag.
public sealed class InstantLootDef
{
    public int PlaneXp;
    public int PilotXp;
    public List<string> Unlocks = new List<string>();
    public string CurrencyId = "";
    public int CurrencyAmount;
    public string IngredientId = "";
    public int IngredientAmount;
    public string Buff = "";

    public bool IsEmpty => PlaneXp == 0 && PilotXp == 0 && Unlocks.Count == 0 && CurrencyAmount == 0 && IngredientAmount == 0 && Buff.Length == 0;

    public override string ToString()
    {
        var parts = new List<string>();
        if (PlaneXp != 0) parts.Add(PlaneXp + " plane XP");
        if (PilotXp != 0) parts.Add(PilotXp + " pilot XP");
        if (Unlocks.Count > 0) parts.Add("unlocks " + string.Join(", ", Unlocks));
        if (CurrencyAmount != 0) parts.Add(CurrencyAmount + " " + CurrencyId);
        if (IngredientAmount != 0) parts.Add(IngredientAmount + " " + IngredientId);
        if (Buff.Length > 0) parts.Add("buff " + Buff);
        return string.Join(", ", parts);
    }
}

public sealed class LootItemDef
{
    public string Id = "";
    public int? Slot;
    public int Tier;
    public InstantLootDef? Instant;
}

public sealed class LootSetDef
{
    public float Weight;
    public List<LootItemDef>? Items;
    public float UsableChance = -1f;
    public List<int>? EnsureUsableSlots;
}

public sealed class LootConditionDef
{
    public int? PilotLevelAboveOrEq;
    public string PlaneClass = "";
    public int? PlaneLevelAboveOrEq;
    public string Quest = "";
    public string Buff = "";
    public int BuffCount = 1;
    public bool? IsAdmin;
    public bool? IsTestEnv;
    public bool? IsAlways;
    public string Difficulty = "";        // server extension: "normal", "difficult" or "lethal"
    public bool UnsupportedField;         // combat sim and flock conditions (always false here)
    public List<LootConditionDef>? Or;
    public List<LootConditionDef>? Not;
    public List<LootConditionDef>? And;
}

public sealed class LootDef
{
    public string LootTableId = "";
    public float UsableChance = -1f;
    public List<int>? EnsureUsableSlots;
    public List<LootConditionDef>? Conditions;
    public List<LootSetDef> Sets = new List<LootSetDef>();
}

public sealed class LootTableFile
{
    public string Id = "";
    public string Description = "";
    public string FilePath = "";
    public List<string> Replaces = new List<string>();
    public List<string> Units = new List<string>();
    public List<LootDef> Rolls = new List<LootDef>();
}

// Result of a draw: item ids and instant rewards.
public sealed class DrawnLoot
{
    public List<string> Items = new List<string>();
    public List<InstantLootDef> Instants = new List<InstantLootDef>();
    public List<string> Sources = new List<string>();

    public bool IsEmpty => Items.Count == 0 && Instants.Count == 0;
}

public static class LootTables
{
    public const string FolderName = "loot_tables";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly JsonDocumentOptions JsonOptions = new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly object gate = new object();
    private static Dictionary<string, LootTableFile> tables = new Dictionary<string, LootTableFile>(StringComparer.OrdinalIgnoreCase);
    private static string folderStamp = "";
    private static DateTime nextCheck = DateTime.MinValue;
    private static Action<string, string> log = (_, _) => { };

    public static string FolderPath => Path.Combine(Directory.GetCurrentDirectory(), FolderName);

    public static void Start(Action<string, string> log)
    {
        LootTables.log = log;
        lock (gate) ReloadIfChanged(true);
    }

    // Current tables. The folder is checked again at most every 2 seconds (new, changed or removed files).
    public static IReadOnlyDictionary<string, LootTableFile> All
    {
        get
        {
            lock (gate)
            {
                if (DateTime.UtcNow >= nextCheck)
                {
                    nextCheck = DateTime.UtcNow.AddSeconds(2);
                    ReloadIfChanged(false);
                }
                return tables;
            }
        }
    }

    public static LootTableFile? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return All.TryGetValue(id, out LootTableFile? table) ? table : null;
    }

    private static void ReloadIfChanged(bool force)
    {
        try
        {
            string folder = FolderPath;
            if (!Directory.Exists(folder))
            {
                if (force) log("LOOT", "folder " + folder + " does not exist: no loot tables (enemies only drop the game's built-in loot)");
                tables = new Dictionary<string, LootTableFile>(StringComparer.OrdinalIgnoreCase);
                folderStamp = "";
                return;
            }
            string[] files = Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
            string stamp = string.Join("|", files.Select(f => f + "@" + File.GetLastWriteTimeUtc(f).Ticks.ToString(Inv)));
            if (!force && stamp == folderStamp) return;
            folderStamp = stamp;

            var result = new Dictionary<string, LootTableFile>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<string>();
            foreach (string file in files)
            {
                try
                {
                    LootTableFile table = ParseFile(file);
                    if (result.ContainsKey(table.Id)) errors.Add(Path.GetFileName(file) + ": id '" + table.Id + "' repeated (the first one stays)");
                    else result[table.Id] = table;
                }
                catch (Exception e)
                {
                    errors.Add(Path.GetFileName(file) + ": " + e.Message);
                }
            }
            // A file with an error keeps the last good version of that table.
            foreach (KeyValuePair<string, LootTableFile> old in tables)
            {
                if (!result.ContainsKey(old.Key) && files.Contains(old.Value.FilePath, StringComparer.OrdinalIgnoreCase))
                    result[old.Key] = old.Value;
            }
            tables = result;
            log("LOOT", folder + ": " + result.Count + " loot table(s) (" + string.Join(", ", result.Values.Select(t => t.Id + " " + t.Rolls.Count + " roll(s)")) + ")" +
                (errors.Count > 0 ? " | ERROS: " + string.Join("; ", errors) : ""));
        }
        catch (Exception e)
        {
            log("LOOT", "error reading the loot folder: " + e.Message);
        }
    }

    // ------------------------------------------------------------------ parsing (JSON files)

    private static LootTableFile ParseFile(string file)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file), JsonOptions);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("the file must be an object { }");
        var table = new LootTableFile
        {
            Id = Str(root, "id"),
            Description = Str(root, "description"),
            FilePath = file
        };
        if (table.Id.Length == 0) table.Id = Path.GetFileNameWithoutExtension(file);
        table.Replaces = StrList(root, "replaces");
        table.Units = StrList(root, "units");
        if (Prop(root, "rolls") is JsonElement rolls && rolls.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement roll in rolls.EnumerateArray()) table.Rolls.Add(ParseDef(roll));
        }
        else if (Prop(root, "lootTable") is JsonElement)
        {
            // A file that is a single LootTableDef.
            table.Rolls.Add(ParseDef(root));
        }
        if (table.Rolls.Count == 0) throw new InvalidDataException("no \"rolls\"");
        foreach (LootDef def in table.Rolls) ValidateDef(def, table.Id);
        return table;
    }

    private static void ValidateDef(LootDef def, string table)
    {
        foreach (LootSetDef set in def.Sets)
        {
            if (set.Items == null) continue;
            foreach (LootItemDef item in set.Items)
            {
                if (item.Id.Length > 0 && ItemCatalog.Loaded && ItemCatalog.Get(item.Id) == null)
                    log("LOOT", "table '" + table + "': item '" + item.Id + "' does not exist in this game version (it comes out as an unknown item)");
            }
        }
    }

    private static JsonElement? Prop(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (JsonProperty p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        }
        return null;
    }

    private static string Str(JsonElement obj, string name)
    {
        JsonElement? v = Prop(obj, name);
        if (v == null) return "";
        return v.Value.ValueKind == JsonValueKind.String ? v.Value.GetString() ?? "" :
            v.Value.ValueKind == JsonValueKind.Number ? v.Value.GetRawText() : "";
    }

    private static float? Num(JsonElement obj, string name)
    {
        JsonElement? v = Prop(obj, name);
        if (v == null) return null;
        if (v.Value.ValueKind == JsonValueKind.Number) return (float)v.Value.GetDouble();
        if (v.Value.ValueKind == JsonValueKind.String && float.TryParse(v.Value.GetString(), NumberStyles.Float, Inv, out float f)) return f;
        return null;
    }

    private static bool? Bool(JsonElement obj, string name)
    {
        JsonElement? v = Prop(obj, name);
        if (v == null) return null;
        if (v.Value.ValueKind == JsonValueKind.True) return true;
        if (v.Value.ValueKind == JsonValueKind.False) return false;
        if (v.Value.ValueKind == JsonValueKind.String && bool.TryParse(v.Value.GetString(), out bool b)) return b;
        return null;
    }

    private static List<string> StrList(JsonElement obj, string name)
    {
        var list = new List<string>();
        JsonElement? v = Prop(obj, name);
        if (v == null) return list;
        if (v.Value.ValueKind == JsonValueKind.String) list.Add(v.Value.GetString() ?? "");
        else if (v.Value.ValueKind == JsonValueKind.Array)
            foreach (JsonElement e in v.Value.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String) list.Add(e.GetString() ?? "");
        list.RemoveAll(s => s.Length == 0);
        return list;
    }

    private static List<int>? SlotList(JsonElement obj, string name)
    {
        List<string> names = StrList(obj, name);
        if (names.Count == 0) return null;
        List<int> slots = names.Select(ItemCatalog.ParseSlot).Where(s => s >= 0).ToList();
        return slots.Count > 0 ? slots : null;
    }

    private static LootDef ParseDef(JsonElement e)
    {
        var def = new LootDef
        {
            LootTableId = Str(e, "lootTableId"),
            UsableChance = Num(e, "usableChance") ?? -1f,
            EnsureUsableSlots = SlotList(e, "ensureUsableSlots"),
            Conditions = ParseConditions(Prop(e, "conditions"))
        };
        if (Prop(e, "lootTable") is JsonElement sets && sets.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement s in sets.EnumerateArray())
            {
                var set = new LootSetDef
                {
                    Weight = Num(s, "weight") ?? 1f,
                    UsableChance = Num(s, "usableChance") ?? -1f,
                    EnsureUsableSlots = SlotList(s, "ensureUsableSlots")
                };
                if (Prop(s, "items") is JsonElement items && items.ValueKind == JsonValueKind.Array)
                {
                    set.Items = new List<LootItemDef>();
                    foreach (JsonElement it in items.EnumerateArray()) set.Items.Add(ParseItem(it));
                }
                def.Sets.Add(set);
            }
        }
        return def;
    }

    private static LootItemDef ParseItem(JsonElement e)
    {
        var item = new LootItemDef { Id = Str(e, "id"), Tier = (int)(Num(e, "tier") ?? 1f) };
        string slot = Str(e, "slot");
        if (slot.Length > 0 && ItemCatalog.ParseSlot(slot) >= 0) item.Slot = ItemCatalog.ParseSlot(slot);
        if (Prop(e, "instant") is JsonElement inst && inst.ValueKind == JsonValueKind.Object)
        {
            item.Instant = new InstantLootDef
            {
                PlaneXp = (int)(Num(inst, "planeXp") ?? 0f),
                PilotXp = (int)(Num(inst, "pilotXp") ?? 0f),
                Unlocks = StrList(inst, "unlocks"),
                CurrencyId = Str(inst, "currency_id"),
                CurrencyAmount = (int)(Num(inst, "currency_amount") ?? 0f),
                IngredientId = Str(inst, "ingredient_id"),
                IngredientAmount = (int)(Num(inst, "ingredient_amount") ?? 0f),
                Buff = Str(inst, "buff")
            };
        }
        return item;
    }

    private static List<LootConditionDef>? ParseConditions(JsonElement? e)
    {
        if (e == null) return null;
        var list = new List<LootConditionDef>();
        var items = new List<JsonElement>();
        if (e.Value.ValueKind == JsonValueKind.Array) items.AddRange(e.Value.EnumerateArray());
        else if (e.Value.ValueKind == JsonValueKind.Object) items.Add(e.Value);
        foreach (JsonElement c in items)
        {
            var cond = new LootConditionDef
            {
                PilotLevelAboveOrEq = (int?)Num(c, "pilotLevelAboveOrEq"),
                PlaneClass = Str(c, "planeClass"),
                PlaneLevelAboveOrEq = (int?)Num(c, "planeLevelAboveOrEq"),
                Quest = Str(c, "quest"),
                Buff = Str(c, "buff"),
                BuffCount = (int)(Num(c, "buffCount") ?? 1f),
                IsAdmin = Bool(c, "isAdmin"),
                IsTestEnv = Bool(c, "isTestEnv"),
                IsAlways = Bool(c, "isAlways"),
                Difficulty = Str(c, "difficulty"),
                Or = ParseConditions(Prop(c, "or")),
                Not = ParseConditions(Prop(c, "not")),
                And = ParseConditions(Prop(c, "and"))
            };
            foreach (JsonProperty p in c.EnumerateObject())
            {
                if (p.Name.StartsWith("combatSim", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Equals("isNewFlocksNecessary", StringComparison.OrdinalIgnoreCase))
                    cond.UnsupportedField = true;
            }
            list.Add(cond);
        }
        return list;
    }

    // ------------------------------------------------------------------ parsing (game config: unit loot)

    public static LootDef ParseDef(ConfigNode n)
    {
        var def = new LootDef
        {
            LootTableId = n["lootTableId"].AsString(""),
            UsableChance = n["usableChance"].AsFloat(-1f),
            EnsureUsableSlots = SlotList(n["ensureUsableSlots"]),
            Conditions = ParseConditions(n["conditions"])
        };
        foreach (ConfigNode s in n["lootTable"].Items)
        {
            var set = new LootSetDef
            {
                Weight = s["weight"].AsFloat(1f),
                UsableChance = s["usableChance"].AsFloat(-1f),
                EnsureUsableSlots = SlotList(s["ensureUsableSlots"])
            };
            if (s.Has("items"))
            {
                set.Items = new List<LootItemDef>();
                foreach (ConfigNode it in s["items"].Items)
                {
                    var item = new LootItemDef { Id = it["id"].AsString(""), Tier = it["tier"].AsInt(1) };
                    int slot = ItemCatalog.ParseSlot(it["slot"].AsString(""));
                    if (slot >= 0) item.Slot = slot;
                    ConfigNode inst = it["instant"];
                    if (!inst.IsMissing)
                    {
                        item.Instant = new InstantLootDef
                        {
                            PlaneXp = inst["planeXp"].AsInt(0),
                            PilotXp = inst["pilotXp"].AsInt(0),
                            Unlocks = inst["unlocks"].AsStringList(),
                            CurrencyId = inst["currency_id"].AsString(""),
                            CurrencyAmount = inst["currency_amount"].AsInt(0),
                            IngredientId = inst["ingredient_id"].AsString(""),
                            IngredientAmount = inst["ingredient_amount"].AsInt(0),
                            Buff = inst["buff"].AsString("")
                        };
                    }
                    set.Items.Add(item);
                }
            }
            def.Sets.Add(set);
        }
        return def;
    }

    private static List<int>? SlotList(ConfigNode n)
    {
        List<int> slots = n.AsStringList().Select(ItemCatalog.ParseSlot).Where(s => s >= 0).ToList();
        return slots.Count > 0 ? slots : null;
    }

    private static List<LootConditionDef>? ParseConditions(ConfigNode n)
    {
        if (n.IsMissing) return null;
        var list = new List<LootConditionDef>();
        var items = new List<ConfigNode>();
        if (n.IsList) items.AddRange(n.Items);
        else items.Add(n);
        foreach (ConfigNode c in items)
        {
            var cond = new LootConditionDef
            {
                PilotLevelAboveOrEq = c["pilotLevelAboveOrEq"].AsNullableInt(),
                PlaneClass = c["planeClass"].AsString(""),
                PlaneLevelAboveOrEq = c["planeLevelAboveOrEq"].AsNullableInt(),
                Quest = c["quest"].AsString(""),
                Buff = c["buff"].AsString(""),
                BuffCount = c["buffCount"].AsInt(1),
                IsAdmin = c.Has("isAdmin") ? c["isAdmin"].AsBool(false) : null,
                IsTestEnv = c.Has("isTestEnv") ? c["isTestEnv"].AsBool(false) : null,
                IsAlways = c.Has("isAlways") ? c["isAlways"].AsBool(false) : null,
                Or = c.Has("or") ? ParseConditions(c["or"]) : null,
                Not = c.Has("not") ? ParseConditions(c["not"]) : null,
                And = c.Has("and") ? ParseConditions(c["and"]) : null
            };
            foreach (KeyValuePair<string, ConfigNode> p in c.Pairs)
            {
                if (p.Key.StartsWith("combatSim", StringComparison.OrdinalIgnoreCase) ||
                    p.Key.Equals("isNewFlocksNecessary", StringComparison.OrdinalIgnoreCase))
                    cond.UnsupportedField = true;
            }
            list.Add(cond);
        }
        return list;
    }

    // ------------------------------------------------------------------ resolution

    // "*" matches any text, case does not matter ("tier*boss", "*leader*", "miniboss").
    public static bool Matches(string pattern, string text)
    {
        if (pattern == "*") return true;
        string[] parts = pattern.ToLowerInvariant().Split('*');
        string value = text.ToLowerInvariant();
        if (parts.Length == 1) return value == parts[0];
        int pos = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part.Length == 0) continue;
            if (i == 0)
            {
                if (!value.StartsWith(part, StringComparison.Ordinal)) return false;
                pos = part.Length;
                continue;
            }
            if (i == parts.Length - 1)
                return value.Length - part.Length >= pos && value.EndsWith(part, StringComparison.Ordinal);
            int found = value.IndexOf(part, pos, StringComparison.Ordinal);
            if (found < 0) return false;
            pos = found + part.Length;
        }
        return true;
    }

    // Table that stands in for a game lootTableId: same id, or the table with the most specific matching
    // "replaces" pattern (more letters outside the "*"; ties go to the first id in alphabetical order).
    public static LootTableFile? ForLootTableId(string lootTableId)
    {
        IReadOnlyDictionary<string, LootTableFile> all = All;
        if (all.TryGetValue(lootTableId, out LootTableFile? same)) return same;
        return BestMatch(all.Values, t => t.Replaces, lootTableId);
    }

    // Table that a unit always uses ("units" patterns), if any.
    public static LootTableFile? ForUnit(string templateName)
    {
        return BestMatch(All.Values, t => t.Units, templateName);
    }

    private static LootTableFile? BestMatch(IEnumerable<LootTableFile> tables, Func<LootTableFile, List<string>> patterns, string text)
    {
        LootTableFile? best = null;
        int bestScore = -1;
        foreach (LootTableFile table in tables.OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase))
        {
            foreach (string pattern in patterns(table))
            {
                if (!Matches(pattern, text)) continue;
                int score = pattern.Replace("*", "").Length;
                if (score > bestScore)
                {
                    best = table;
                    bestScore = score;
                }
            }
        }
        return best;
    }

    // Loot of a kill: the unit's own table (units), else the game's loot entries of the unit (inline tables and
    // the files standing in for its lootTableIds), else the normal or boss table of items.json.
    public static DrawnLoot RollForKill(string templateName, int tier, bool boss, LootContext ctx, ItemSettings settings)
    {
        var drawn = new DrawnLoot();
        int rolls = Math.Max(0, settings.LootRolls);
        for (int r = 0; r < rolls; r++) RollOnce(templateName, tier, boss, ctx, settings, drawn);
        return drawn;
    }

    private static void RollOnce(string templateName, int tier, bool boss, LootContext ctx, ItemSettings settings, DrawnLoot drawn)
    {
        LootTableFile? own = ForUnit(templateName);
        if (own != null)
        {
            DrawTable(own, tier, ctx, drawn, "table '" + own.Id + "' (unit)");
            return;
        }

        bool resolved = false;
        UnitLootInfo? game = settings.UseGameUnitLoot ? ItemCatalog.GetUnitLoot(templateName) : null;
        if (game != null)
        {
            foreach (UnitLootEntry entry in game.Entries)
            {
                if (!DifficultyMatches(entry.Key, ctx.Difficulty) || !DifficultyMatches(entry.LootTableId, ctx.Difficulty)) continue;
                LootDef def = ParseDef(entry.Node);
                if (def.Sets.Count > 0)
                {
                    // Inline table of the game data (for example the dungeon key some enemies carry).
                    DrawDef(def, tier, ctx, drawn, "game '" + entry.Key + "'");
                    if (def.LootTableId.Length == 0) continue;
                }
                if (def.LootTableId.Length > 0)
                {
                    LootTableFile? table = ForLootTableId(def.LootTableId);
                    if (table == null) continue;
                    // The game's entry keeps its own conditions and usability on top of the table.
                    DrawTable(table, tier, ctx, drawn, "table '" + table.Id + "' in place of '" + def.LootTableId + "'", def);
                    resolved = true;
                }
            }
        }

        if (!resolved)
        {
            string fallback = boss ? settings.BossLootTable : settings.NormalLootTable;
            LootTableFile? table = Get(fallback);
            if (table != null) DrawTable(table, tier, ctx, drawn, "table '" + table.Id + "' (" + (boss ? "boss" : "regular enemy") + ")");
        }
    }

    // Entries named "..._normal", "..._difficult" or "..._lethal" only drop on that dungeon difficulty.
    private static bool DifficultyMatches(string name, string difficulty)
    {
        string n = name.ToLowerInvariant();
        foreach (string d in new[] { "normal", "difficult", "lethal" })
        {
            if (n.EndsWith("_" + d, StringComparison.Ordinal) || n.Contains("_" + d + "_", StringComparison.Ordinal))
                return d == difficulty;
        }
        return true;
    }

    public static void DrawTable(LootTableFile table, int tier, LootContext ctx, DrawnLoot drawn, string source, LootDef? outer = null)
    {
        foreach (LootDef roll in table.Rolls)
        {
            if (outer != null && outer.Conditions != null && !Satisfied(outer.Conditions, ctx, false)) return;
            LootDef def = roll;
            if (outer != null && (outer.UsableChance >= 0f || outer.EnsureUsableSlots != null))
            {
                def = new LootDef
                {
                    LootTableId = roll.LootTableId,
                    UsableChance = Math.Max(roll.UsableChance, outer.UsableChance),
                    EnsureUsableSlots = Union(roll.EnsureUsableSlots, outer.EnsureUsableSlots),
                    Conditions = roll.Conditions,
                    Sets = roll.Sets
                };
            }
            DrawDef(def, tier, ctx, drawn, source);
        }
    }

    private static List<int>? Union(List<int>? a, List<int>? b)
    {
        if (a == null) return b;
        if (b == null) return a;
        return a.Union(b).ToList();
    }

    // SteambirdsLootTable.DrawLoot(LootTableDef, baseTier, ...).
    public static void DrawDef(LootDef def, int baseTier, LootContext ctx, DrawnLoot drawn, string source)
    {
        if (def.Sets.Count == 0) return;
        if (def.Conditions != null && !Satisfied(def.Conditions, ctx, false)) return;

        LootSetDef? set = PickWeighted(def.Sets);
        if (set == null || set.Items == null) return;

        bool usable = false;
        if (def.UsableChance >= 0f && Random.Shared.NextDouble() * 100.0 < def.UsableChance) usable = true;
        if (set.UsableChance >= 0f && Random.Shared.NextDouble() * 100.0 < set.UsableChance) usable = true;
        HashSet<int>? ensure = null;
        if (def.EnsureUsableSlots != null) ensure = new HashSet<int>(def.EnsureUsableSlots);
        if (set.EnsureUsableSlots != null)
        {
            ensure ??= new HashSet<int>();
            foreach (int s in set.EnsureUsableSlots) ensure.Add(s);
        }

        bool any = false;
        foreach (LootItemDef item in set.Items)
        {
            if (item.Instant != null)
            {
                if (!item.Instant.IsEmpty) { drawn.Instants.Add(item.Instant); any = true; }
                continue;
            }
            string? id = PickItem(item, baseTier - 1, usable, ensure, ctx);
            if (!string.IsNullOrEmpty(id))
            {
                drawn.Items.Add(id);
                any = true;
            }
        }
        if (any) drawn.Sources.Add(source);
    }

    // RandomPlus.PickRandomWeighted.
    private static LootSetDef? PickWeighted(List<LootSetDef> sets)
    {
        double total = sets.Sum(s => Math.Max(0f, s.Weight));
        if (total <= 0) return null;
        double roll = Random.Shared.NextDouble() * total;
        foreach (LootSetDef s in sets)
        {
            double w = Math.Max(0f, s.Weight);
            if (roll < w) return s;
            roll -= w;
        }
        return sets[sets.Count - 1];
    }

    // SteambirdsLootTable.LootElement.GetItem.
    private static string? PickItem(LootItemDef item, int addTiers, bool usable, HashSet<int>? usableSlots, LootContext ctx)
    {
        if (item.Id.Length > 0) return item.Id;
        int tier = item.Tier;
        if (addTiers > 0) tier += addTiers;
        tier = Math.Clamp(tier, 1, 10);

        List<int> slots = item.Slot.HasValue ? new List<int> { item.Slot.Value } : ItemCatalog.RandomItemSlots.OrderBy(_ => Random.Shared.Next()).ToList();
        PlaneClassInfo? cls = ItemCatalog.GetClass(ctx.PlaneClass);
        foreach (int slot in slots)
        {
            IReadOnlyList<ItemDef> bucket = ItemCatalog.Bucket(slot, tier);
            if (bucket.Count == 0) return null;
            List<ItemDef> list = FilterByUsability(bucket, usable, usableSlots, cls);
            if (list.Count > 0) return list[Random.Shared.Next(list.Count)].Id;
        }
        return null;
    }

    // SteambirdsLootTable.LootElement.FilterByUsability (Usable = only what the class can equip; ensureUsableSlots
    // = items of those slots must be usable, other slots are free).
    private static List<ItemDef> FilterByUsability(IReadOnlyList<ItemDef> raw, bool usable, HashSet<int>? usableSlots, PlaneClassInfo? cls)
    {
        if (cls == null || (!usable && usableSlots == null)) return raw.ToList();
        if (usable) return raw.Where(i => cls.AllowsTag(i.Tag)).ToList();
        return raw.Where(i => cls.AllowsTag(i.Tag) || !usableSlots!.Contains(i.SlotValue)).ToList();
    }

    // LootCondition.IsSatisfied: every condition must hold (or, with booleanOr, any of them).
    public static bool Satisfied(List<LootConditionDef> conditions, LootContext ctx, bool booleanOr)
    {
        foreach (LootConditionDef c in conditions)
        {
            var checks = new List<bool>();
            if (c.Or != null) checks.Add(Satisfied(c.Or, ctx, true));
            if (c.Not != null) checks.Add(!Satisfied(c.Not, ctx, false));
            if (c.And != null) checks.Add(Satisfied(c.And, ctx, false));
            if (c.PilotLevelAboveOrEq.HasValue) checks.Add(ctx.PilotLevel >= c.PilotLevelAboveOrEq.Value);
            if (c.PlaneClass.Length > 0) checks.Add(string.Equals(ctx.PlaneClass, c.PlaneClass, StringComparison.OrdinalIgnoreCase));
            if (c.PlaneLevelAboveOrEq.HasValue) checks.Add(ctx.PlaneLevel >= c.PlaneLevelAboveOrEq.Value);
            if (c.Quest.Length > 0) checks.Add(ctx.FinishedQuests.Contains(c.Quest));
            if (c.Buff.Length > 0) checks.Add(ctx.Buffs.Contains(c.Buff) && c.BuffCount <= 1);
            if (c.IsAdmin.HasValue) checks.Add(ctx.IsAdmin == c.IsAdmin.Value);
            if (c.IsTestEnv.HasValue) checks.Add(!c.IsTestEnv.Value);
            if (c.IsAlways.HasValue) checks.Add(c.IsAlways.Value);
            if (c.Difficulty.Length > 0) checks.Add(string.Equals(ctx.Difficulty, c.Difficulty, StringComparison.OrdinalIgnoreCase));
            if (c.UnsupportedField) checks.Add(false);

            foreach (bool ok in checks)
            {
                if (ok && booleanOr) return true;
                if (!ok && !booleanOr) return false;
            }
        }
        return !booleanOr;
    }
}
