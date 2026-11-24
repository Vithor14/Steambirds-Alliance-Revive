using System.Text.Json.Nodes;

namespace SteambirdsLauncher.Editor;

internal enum NameKind { UnitType, Room, Area, Line, Point }

// When something is renamed (a unit type, a room, an area...), everything in the map that refers to it by name follows.
internal static class MapRefactor
{
    public static void Rename(MapDocument document, NameKind kind, string oldName, string newName)
    {
        if (oldName == newName || oldName.Length == 0) return;
        JsonObject root = document.Root;
        JsonArray? rooms = Json.ArrOrNull(root, "encounters");

        switch (kind)
        {
            case NameKind.UnitType:
                RenameTypeReferences(root, oldName, newName);
                break;

            case NameKind.Room:
                foreach (JsonObject gate in Gates(root)) ReplaceIfEqual(gate, "room", oldName, newName);
                RewriteConditions(root, "cleared", oldName, newName);
                break;

            case NameKind.Area:
                foreach (JsonObject room in Json.Objects(rooms)) ReplaceIfEqual(room, "area", oldName, newName);
                break;

            case NameKind.Line:
                foreach (JsonObject room in Json.Objects(rooms)) ReplaceIfEqual(room, "line", oldName, newName);
                break;

            case NameKind.Point:
                if (root["exit"] is JsonObject exit) ReplaceIfEqual(exit, "point", oldName, newName);
                break;
        }
    }

    private static IEnumerable<JsonObject> Gates(JsonObject root)
    {
        return Json.Objects(Json.ArrOrNull(root, "gates")).Concat(Json.Objects(Json.ArrOrNull(root, "sideGates")));
    }

    private static void RenameTypeReferences(JsonObject root, string oldName, string newName)
    {
        JsonArray? rooms = Json.ArrOrNull(root, "encounters");
        foreach (JsonObject room in Json.Objects(rooms))
        {
            foreach (JsonObject group in Json.Objects(Json.ArrOrNull(room, "enemies"))) ReplaceIfEqual(group, "type", oldName, newName);
            foreach (JsonObject wave in Json.Objects(Json.ArrOrNull(room, "waves")))
            {
                foreach (JsonObject group in Json.Objects(Json.ArrOrNull(wave, "groups"))) ReplaceIfEqual(group, "type", oldName, newName);
            }
        }
        foreach (JsonObject gate in Gates(root)) ReplaceIfEqual(gate, "type", oldName, newName);

        foreach (KeyValuePair<string, JsonObject> pair in Json.ObjectProperties(Json.ObjOrNull(root, "types")))
        {
            JsonObject type = pair.Value;
            ReplaceIfEqual(type, "onDeath", oldName, newName);
            if (type["sequence"] is JsonArray sequence)
            {
                for (int i = 0; i < sequence.Count; i++)
                {
                    if (Json.AsString(sequence[i]) == oldName) sequence[i] = newName;
                }
            }
            if (type["periodicSummon"] is JsonObject summon) ReplaceIfEqual(summon, "type", oldName, newName);
            if (type["dpsChest"] is JsonObject chest)
            {
                ReplaceIfEqual(chest, "reward", oldName, newName);
                ReplaceIfEqual(chest, "fail", oldName, newName);
            }
        }

        RewriteConditions(root, "death", oldName, newName);
    }

    // "cleared:<room>", "death:<type>" in the exit rule, the "opensWhen" of gates and the "wakes" of rooms and groups.
    private static void RewriteConditions(JsonObject root, string prefix, string oldName, string newName)
    {
        if (root["exit"] is JsonObject exit) RewriteCondition(exit, "when", prefix, oldName, newName);
        foreach (JsonObject gate in Gates(root)) RewriteCondition(gate, "opensWhen", prefix, oldName, newName);
        foreach (JsonObject room in Json.Objects(Json.ArrOrNull(root, "encounters")))
        {
            RewriteCondition(room, "wakes", prefix, oldName, newName);
            foreach (JsonObject group in Json.Objects(Json.ArrOrNull(room, "enemies"))) RewriteCondition(group, "wakes", prefix, oldName, newName);
            foreach (JsonObject wave in Json.Objects(Json.ArrOrNull(room, "waves")))
            {
                foreach (JsonObject group in Json.Objects(Json.ArrOrNull(wave, "groups"))) RewriteCondition(group, "wakes", prefix, oldName, newName);
            }
        }
    }

    private static void RewriteCondition(JsonObject obj, string key, string prefix, string oldName, string newName)
    {
        string text = Json.Str(obj, key);
        if (text.Length == 0) return;
        string[] parts = text.Split('+');
        bool changed = false;
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (part.Equals(prefix + ":" + oldName, StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = prefix + ":" + newName;
                changed = true;
            }
        }
        if (changed) obj[key] = string.Join("+", parts);
    }

    private static void ReplaceIfEqual(JsonObject obj, string key, string oldName, string newName)
    {
        if (Json.Str(obj, key) == oldName) obj[key] = newName;
    }
}
