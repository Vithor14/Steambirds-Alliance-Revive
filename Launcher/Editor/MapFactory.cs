using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.Editor;

// Makes new maps: empty ones or copies of the dungeons of the game (the maps of server\maps).
internal static class MapFactory
{
    private static readonly Regex IdPattern = new Regex("^[a-z0-9_]+$", RegexOptions.Compiled);

    // Map ids are file names: lower case letters, digits and underscore.
    public static bool IsValidId(string id) => id.Length > 0 && id.Length <= 60 && IdPattern.IsMatch(id);

    public static string SuggestId(string name)
    {
        string lower = name.Trim().ToLowerInvariant();
        var text = new System.Text.StringBuilder();
        foreach (char c in lower)
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) text.Append(c);
            else if (text.Length > 0 && text[text.Length - 1] != '_') text.Append('_');
        }
        string id = text.ToString().Trim('_');
        return id.Length == 0 ? "new_map" : id;
    }

    // An id that has no file yet in server\maps.
    public static string FreeId(string wanted)
    {
        string id = wanted;
        for (int n = 2; File.Exists(Path.Combine(LauncherPaths.MapsFolder, id + ".json")); n++) id = wanted + "_" + n;
        return id;
    }

    // Empty map: no floor, no objects. The floor pieces are the ones the game uses for that terrain.
    public static MapDocument Blank(string id, string name, string terrain, int territory)
    {
        TerritoryInfo? info = Territories.ForTerrain(terrain);
        bool dungeon = terrain.StartsWith("Dungeon", StringComparison.OrdinalIgnoreCase);

        var root = new JsonObject
        {
            ["format"] = MapDocument.FormatName,
            ["version"] = MapDocument.FormatVersion,
            ["id"] = id,
            ["name"] = name,
            ["terrain"] = terrain,
            ["note"] = "",
            ["source"] = "Made with the map editor of the launcher."
        };
        if (dungeon)
        {
            root["key"] = "";
            root["portalItem"] = "";
            root["exit"] = new JsonObject { ["point"] = "portal", ["when"] = "" };
        }
        root["territory"] = territory;
        root["floor"] = new JsonObject
        {
            ["wallsOnEdges"] = true,
            ["voidMargin"] = 4,
            ["voidTerritory"] = 74,
            ["drawingPieces"] = Json.IntArray(info?.DefaultPieces ?? Array.Empty<int>()),
            ["pieces"] = new JsonArray()
        };
        root["decor"] = new JsonArray();
        root["buildings"] = new JsonArray();
        root["points"] = new JsonObject();
        root["groups"] = new JsonObject();
        root["areas"] = new JsonObject();
        root["lines"] = new JsonObject();
        root["values"] = new JsonObject();
        if (dungeon) root["values"] = new JsonObject { ["tier"] = 5 };
        root["checkpoints"] = new JsonArray();
        root["gates"] = new JsonArray();
        if (dungeon)
        {
            root["sideGates"] = new JsonArray();
            root["signs"] = new JsonArray();
            root["types"] = new JsonObject();
            root["encounters"] = new JsonArray();
        }
        else
        {
            root["signs"] = new JsonArray();
        }
        return new MapDocument(root);
    }

    // A copy of the map file of a dungeon of dungeons.json. When this copy of the game has no map file for it yet,
    // the map starts empty with the terrain of that dungeon. "copied" tells which one happened.
    public static MapDocument FromGameDungeon(DungeonCatalogFile.Dungeon dungeon, string id, string name, out bool copied)
    {
        copied = false;
        string path = Path.Combine(LauncherPaths.MapsFolder, dungeon.Map + ".json");
        if (dungeon.Map.Length > 0 && File.Exists(path))
        {
            MapDocument source = MapDocument.Load(path);
            source.FilePath = null;
            source.Id = id;
            source.Name = name;
            source.Root.Remove("draft");
            copied = true;
            return source;
        }

        TerritoryInfo? info = Territories.ForTerrain(dungeon.Type);
        return Blank(id, name, dungeon.Type, info?.Territory ?? 0);
    }
}
