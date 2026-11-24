using System.Text.Json.Nodes;

namespace SteambirdsLauncher.Editor;

// Changes that touch the whole map.
internal static class MapTransforms
{
    // Moves the floor, and every object that has a position, by whole tiles so the floor is centered on the grid.
    // The client grid has 54 tiles on each side of the origin; a map that is far from it would not fit.
    // True when something moved.
    public static bool CenterOnGrid(MapDocument document)
    {
        if (!document.Floor.GetBounds(out int minA, out int maxA, out int minB, out int maxB)) return false;
        int da = -(int)Math.Floor((minA + maxA) / 2.0);
        int db = -(int)Math.Floor((minB + maxB) / 2.0);
        if (da == 0 && db == 0) return false;
        Shift(document, da, db);
        return true;
    }

    // Moves the whole map by whole tiles (da along u, db along v).
    public static void Shift(MapDocument document, int da, int db)
    {
        document.Floor.Shift(da, db);
        double du = da * MapCoords.Tile;
        double dv = db * MapCoords.Tile;
        int dsi = da * MapCoords.SubsPerTile;
        int dsj = db * MapCoords.SubsPerTile;
        JsonObject root = document.Root;

        MovePoint(root["spawn"] as JsonObject, du, dv);
        foreach (JsonObject point in Json.ObjectProperties(root["points"] as JsonObject).Select(p => p.Value)) MovePoint(point, du, dv);

        if (root["groups"] is JsonObject groups)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in groups)
            {
                foreach (JsonObject point in Json.Objects(pair.Value as JsonArray)) MovePoint(point, du, dv);
            }
        }

        foreach (JsonObject area in Json.ObjectProperties(root["areas"] as JsonObject).Select(p => p.Value))
        {
            AddNumber(area, "minU", du);
            AddNumber(area, "maxU", du);
            AddNumber(area, "minV", dv);
            AddNumber(area, "maxV", dv);
        }

        if (root["lines"] is JsonObject lines)
        {
            foreach (string key in lines.Select(p => p.Key).ToList()) lines[key] = Json.Round(Json.AsNumber(lines[key]) + dv);
        }

        foreach (JsonObject checkpoint in Json.Objects(root["checkpoints"] as JsonArray))
        {
            MovePoint(checkpoint, du, dv);
            AddNumber(checkpoint, "activeAtV", dv);
            AddNumber(checkpoint, "activeAtU", du);
        }

        foreach (JsonObject sign in Json.Objects(root["signs"] as JsonArray)) MovePoint(sign, du, dv);
        foreach (JsonObject ornament in Json.Objects(root["decor"] as JsonArray)) MovePoint(ornament, du, dv);
        foreach (JsonObject building in Json.Objects(root["buildings"] as JsonArray)) MovePoint(building, du, dv);

        if (root["routes"] is JsonObject routes)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in routes)
            {
                foreach (JsonObject point in Json.Objects(pair.Value as JsonArray)) MovePoint(point, du, dv);
            }
        }

        foreach (JsonObject gate in Json.Objects(root["gates"] as JsonArray))
        {
            AddInt(gate, "sj", dsj);
            AddInt(gate, "siFrom", dsi);
            AddInt(gate, "siTo", dsi);
        }
        foreach (JsonObject gate in Json.Objects(root["sideGates"] as JsonArray))
        {
            AddInt(gate, "si", dsi);
            AddInt(gate, "sjFrom", dsj);
            AddInt(gate, "sjTo", dsj);
        }

        if (root["floor"] is JsonObject floor)
        {
            foreach (JsonObject piece in Json.Objects(floor["pieces"] as JsonArray))
            {
                AddInt(piece, "p", da * 2);
                AddInt(piece, "q", db * 2);
            }
        }

        foreach (JsonObject room in Json.Objects(root["encounters"] as JsonArray))
        {
            foreach (JsonObject group in Json.Objects(room["enemies"] as JsonArray)) MoveGroup(group, du, dv);
            foreach (JsonObject wave in Json.Objects(room["waves"] as JsonArray))
            {
                foreach (JsonObject group in Json.Objects(wave["groups"] as JsonArray)) MoveGroup(group, du, dv);
            }
        }
    }

    private static void MoveGroup(JsonObject group, double du, double dv)
    {
        foreach (JsonObject point in Json.Objects(group["pos"] as JsonArray)) MovePoint(point, du, dv);
    }

    private static void MovePoint(JsonObject? point, double du, double dv)
    {
        if (point == null) return;
        AddNumber(point, "u", du);
        AddNumber(point, "v", dv);
    }

    private static void AddNumber(JsonObject obj, string key, double delta)
    {
        if (obj[key] == null) return;
        obj[key] = Json.Round(Json.AsNumber(obj[key]) + delta);
    }

    private static void AddInt(JsonObject obj, string key, int delta)
    {
        if (obj[key] == null) return;
        obj[key] = (int)Math.Round(Json.AsNumber(obj[key])) + delta;
    }
}
