using System.Text.Json.Nodes;
using SteambirdsAi;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;

namespace SteambirdsLauncher.EnemyAi;

// Where a formation file lives: generic, world, or dungeons/<name>, the same folders as the AI brains.
// The list also groups the world files by the biome of the leader unit.
internal static class FormationOrigins
{
    public static string ForUnit(string unit)
    {
        unit = (unit ?? "").Trim();
        if (unit.Length == 0) return AiCatalog.GenericFolder;
        string dungeon = DungeonOf(unit);
        if (dungeon.Length > 0) return AiCatalog.DungeonsFolder + "/" + dungeon;
        if (BiomeOf(unit).Length > 0 || ListedInWorldAi(unit)) return AiCatalog.WorldFolder;
        return AiCatalog.GenericFolder;
    }

    // The biome label for the tree ("" when this unit is not in a biome list).
    public static string BiomeOf(string unit)
    {
        unit = (unit ?? "").Trim();
        if (unit.Length == 0) return "";
        List<WorldEnemyRegion>? defaults = WorldEnemyFiles.LoadDefaults(out _);
        if (defaults == null) return "";
        foreach (WorldEnemyRegion region in defaults)
        {
            if (!LivesIn(region, unit)) continue;
            return region.Zone.Length > 0 ? region.Zone + " (" + region.Key + ")" : region.Key;
        }
        return "";
    }

    private static bool LivesIn(WorldEnemyRegion region, string unit)
    {
        foreach (WorldEnemyGroup group in region.Groups ?? new List<WorldEnemyGroup>())
        {
            if (group.Leaders.Contains(unit, StringComparer.OrdinalIgnoreCase)) return true;
            if (group.Members.Contains(unit, StringComparer.OrdinalIgnoreCase)) return true;
        }
        if (region.Boss.Equals(unit, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (string choice in region.BossChoices)
            if (choice.Equals(unit, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (WorldBossGroup boss in region.Bosses)
        {
            if (boss.Name.Equals(unit, StringComparison.OrdinalIgnoreCase)) return true;
            if (boss.Parts.Contains(unit, StringComparer.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string DungeonOf(string unit)
    {
        string maps = Path.Combine(LauncherPaths.ServerFolder, "maps");
        if (!Directory.Exists(maps)) return "";
        foreach (string path in Directory.EnumerateFiles(maps, "dungeon_*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(path).StartsWith("dungeon_map_maker", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (JsonNode.Parse(TextFiles.ReadShared(path)) is not JsonObject root || root["types"] is not JsonObject types) continue;
                foreach (KeyValuePair<string, JsonNode?> entry in types)
                {
                    if (entry.Value is not JsonObject type) continue;
                    if (!Uses(Json.Str(type, "template"), unit) && !entry.Key.Equals(unit, StringComparison.OrdinalIgnoreCase)) continue;
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (name.StartsWith("dungeon_", StringComparison.OrdinalIgnoreCase)) name = name.Substring("dungeon_".Length);
                    return AiCatalog.CleanId(name);
                }
            }
            catch (Exception e) when (e is System.Text.Json.JsonException || e is IOException)
            {
                // A map that cannot be read does not claim the unit.
            }
        }
        return "";
    }

    private static bool ListedInWorldAi(string unit)
    {
        string folder = Path.Combine(LauncherPaths.AiFolder, "brains", AiCatalog.WorldFolder);
        if (!Directory.Exists(folder)) return false;
        foreach (string path in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                if (JsonNode.Parse(TextFiles.ReadShared(path)) is not JsonObject root) continue;
                if (Json.Strings(root["units"]).Any(name => name.Equals(unit, StringComparison.OrdinalIgnoreCase))) return true;
            }
            catch (Exception e) when (e is System.Text.Json.JsonException || e is IOException)
            {
                // Skip an unreadable AI.
            }
        }
        return false;
    }

    private static bool Uses(string template, string unit)
    {
        foreach (string option in template.Split('|'))
        {
            string name = option.Trim();
            if (name.Length == 0 || name.StartsWith("auto:", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Equals(unit, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
