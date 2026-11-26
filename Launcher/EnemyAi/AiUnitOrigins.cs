using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;

namespace SteambirdsLauncher.EnemyAi;

// A folder of the unit picker: "Dungeons", then one per dungeon; "World", then one per biome; and so on.
internal sealed class AiUnitGroup
{
    public AiUnitGroup(string title)
    {
        Title = title;
    }

    public string Title { get; }
    public List<AiUnitGroup> Children { get; } = new List<AiUnitGroup>();
    public List<AiUnitChoice> Units { get; } = new List<AiUnitChoice>();

    public IEnumerable<AiUnitChoice> AllUnits() => Units.Concat(Children.SelectMany(c => c.AllUnits()));
}

// Where the units of the Enemy AI tab come from, the same folders the AI files are sorted in (server\ai\brains):
//   Dungeons > <dungeon>   the unit types of each dungeon map (and the tutorial, which is a dungeon)
//   World > <biome>        the enemies of each biome of the open world, with the AI the host picked for each one
//   Generic                the unit types of enemies.json that are not part of a dungeon (tests and commands)
//   Without origin         the game's units that no dungeon, biome or type uses
// A unit that lives in several places is listed in each of them, and each entry runs the AI that place gives it.
internal static class AiUnitOrigins
{
    public static List<AiUnitGroup> Build(string serverFolder, bool armedOnly, AiUnitChoice? mapChoice)
    {
        var groups = new List<AiUnitGroup>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (mapChoice != null)
        {
            var open = new AiUnitGroup(L.T("Map being edited"));
            open.Units.Add(mapChoice);
            groups.Add(open);
        }

        List<AiUnitChoice> enemyTypes = AiUnits.EnemyTypes(Path.Combine(serverFolder, "enemies.json")).ToList();

        var dungeons = new AiUnitGroup(L.T("Dungeons"));
        AiUnitGroup tutorial = new AiUnitGroup("tutorial");
        foreach (AiUnitChoice type in enemyTypes.Where(t => t.TypeId.StartsWith("tutorial_", StringComparison.OrdinalIgnoreCase)))
            tutorial.Units.Add(type);
        foreach (string path in Directory.Exists(Path.Combine(serverFolder, "maps"))
                     ? Directory.EnumerateFiles(Path.Combine(serverFolder, "maps"), "dungeon_*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                     : Enumerable.Empty<string>())
        {
            string name = Path.GetFileNameWithoutExtension(path).Substring("dungeon_".Length);
            var group = new AiUnitGroup(name);
            foreach (AiUnitChoice type in ReadMapTypes(path, L.T("dungeon") + " " + name, armedOnly)) group.Units.Add(type);
            foreach (AiUnitChoice type in group.Units) type.AiFolder = "dungeons/" + SteambirdsAi.AiCatalog.CleanId(name);
            if (group.Units.Count > 0) dungeons.Children.Add(group);
        }
        foreach (AiUnitChoice type in tutorial.Units) type.AiFolder = "dungeons/tutorial";
        if (tutorial.Units.Count > 0) dungeons.Children.Insert(0, tutorial);
        if (dungeons.Children.Count > 0) groups.Add(dungeons);

        AiUnitGroup? world = BuildWorld(armedOnly);
        if (world != null) groups.Add(world);

        var generic = new AiUnitGroup(L.T("Generic"));
        foreach (AiUnitChoice type in enemyTypes.Where(t => !t.TypeId.StartsWith("tutorial_", StringComparison.OrdinalIgnoreCase)))
            generic.Units.Add(type);
        if (generic.Units.Count > 0) groups.Add(generic);

        foreach (AiUnitChoice c in groups.SelectMany(g => g.AllUnits()))
        {
            if (c.UnitName.Length > 0) used.Add(c.UnitName);
        }

        // The game's own units that nothing above uses.
        var none = new AiUnitGroup(L.T("Without origin"));
        IEnumerable<GameUnit> units = EditorAssets.Current.Units.Values.Where(u => !u.Wall);
        if (armedOnly) units = units.Where(u => u.Armed);
        foreach (GameUnit unit in units.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!used.Contains(unit.Name)) none.Units.Add(new AiUnitChoice { Label = unit.Name, UnitName = unit.Name });
        }
        if (groups.SelectMany(g => g.AllUnits()).Count(c => c.UnitName.Length > 0) == 0 && none.Units.Count == 0)
            none.Units.Add(new AiUnitChoice { Label = L.T("(test plane)") });
        if (none.Units.Count > 0) groups.Add(none);
        return groups;
    }

    // The "types" of one map file.
    private static IEnumerable<AiUnitChoice> ReadMapTypes(string path, string kind, bool armedOnly)
    {
        var found = new List<AiUnitChoice>();
        try
        {
            JsonNode? root = JsonNode.Parse(TextFiles.ReadShared(path), null, JsoncFile.ParseOptions);
            if (root is not JsonObject top || top["types"] is not JsonObject types) return found;
            foreach (KeyValuePair<string, JsonNode?> entry in types)
            {
                if (entry.Value is not JsonObject type) continue;
                if (armedOnly)
                {
                    // Walls, chests and wrecks have nothing to fly or shoot with.
                    GameUnit? unit = AiUnits.Resolve(Json.Str(type, "template"));
                    if (unit != null && !unit.Armed) continue;
                    if (EnemyAiEditors.FlightOf(type) == "idle") continue;
                }
                found.Add(AiUnits.FromType(entry.Key, type, kind));
            }
        }
        catch (Exception e) when (e is System.Text.Json.JsonException || e is IOException || e is UnauthorizedAccessException)
        {
            // A map that cannot be read now simply has no units here.
        }
        return found.OrderBy(c => c.OwnAi.StartsWith("generic_", StringComparison.OrdinalIgnoreCase) ? 1 : 0).ThenBy(c => c.TypeId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // One enemy of a biome as the simulator runs it: the AI the host picked for it there, or the default of the open world.
    private static AiUnitChoice WorldChoice(string unit, WorldEnemyRegion region, WorldEnemyRegion? edit)
    {
        string ai = WorldEnemyFiles.DefaultAiOf(unit);
        if (edit?.Ai != null)
        {
            foreach (KeyValuePair<string, string> pair in edit.Ai)
            {
                if (pair.Key.Equals(unit, StringComparison.OrdinalIgnoreCase) && pair.Value.Length > 0) ai = pair.Value;
            }
        }
        var type = new JsonObject { ["name"] = unit, ["template"] = unit, [EnemyAiEditors.AiField] = ai };
        AiUnitChoice choice = AiUnits.FromType(unit, type, L.T("world") + " " + region.Key);
        choice.AiFolder = SteambirdsAi.AiCatalog.WorldFolder;
        return choice;
    }

    // The leader of the first group of the biome where the unit is a follower ("" when it is nobody's follower).
    private static string LeaderOf(List<WorldEnemyGroup> groups, string unit)
    {
        foreach (WorldEnemyGroup g in groups)
        {
            if (g.Solo || g.Leaders.Count == 0) continue;
            if (g.Leaders.Contains(unit, StringComparer.OrdinalIgnoreCase)) continue;
            if (g.Members.Contains(unit, StringComparer.OrdinalIgnoreCase)) return g.Leaders[0];
        }
        return "";
    }

    // The enemies of each biome: the lists of world-enemies.json, or the game's own (world-enemy-defaults.json).
    private static AiUnitGroup? BuildWorld(bool armedOnly)
    {
        List<WorldEnemyRegion>? defaults = WorldEnemyFiles.LoadDefaults(out _);
        if (defaults == null || defaults.Count == 0) return null;
        Dictionary<string, WorldEnemyRegion> edits;
        try
        {
            edits = WorldEnemyFiles.LoadEdits();
        }
        catch (Exception)
        {
            edits = new Dictionary<string, WorldEnemyRegion>(StringComparer.OrdinalIgnoreCase);
        }

        var world = new AiUnitGroup(L.T("World"));
        foreach (WorldEnemyRegion region in defaults)
        {
            edits.TryGetValue(region.Key, out WorldEnemyRegion? edit);
            List<WorldEnemyGroup> lists = edit?.Groups ?? region.Groups ?? new List<WorldEnemyGroup>();
            var names = new List<string>();
            foreach (WorldEnemyGroup g in lists) names.AddRange(g.Leaders.Concat(g.Members));
            foreach (WorldBossGroup boss in region.Bosses) names.AddRange(new[] { boss.Name }.Concat(boss.Parts));
            var group = new AiUnitGroup((region.Zone.Length > 0 ? region.Zone : region.Key) + " (" + region.Key + ")");
            foreach (string unit in names.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                if (armedOnly && EditorAssets.Current.Units.TryGetValue(unit, out GameUnit? known) && !known.Armed) continue;
                AiUnitChoice choice = WorldChoice(unit, region, edit);
                string leader = LeaderOf(lists, unit);
                if (leader.Length > 0) choice.LeaderChoice = WorldChoice(leader, region, edit);
                group.Units.Add(choice);
            }
            if (group.Units.Count > 0) world.Children.Add(group);
        }
        return world.Children.Count > 0 ? world : null;
    }
}
