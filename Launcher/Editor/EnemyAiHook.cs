using System.Text.Json.Nodes;

namespace SteambirdsLauncher.Editor;

// The link between the map editor and the Enemy AI tab (Launcher\EnemyAi).
//
// A unit type of a map ("types") picks its AI with "ai": "<id>", the id (file name) of a file of the server's ai\brains
// folder (inside generic, world or dungeons/<name>). The AI itself is not in the map: it lives in its own file, so it can be edited, shared and loaded on its own,
// and the same AI can serve many maps. The server reads the field; without it (or with an id that does not exist) the unit
// gets the generic AI of the type's old "behavior" (chase when it has none).
internal interface IEnemyAiEditor
{
    string DisplayName { get; }

    // Ids of the AIs that exist.
    IEnumerable<string> BrainIds();

    // Ids of the AIs a unit type can use (for the list of the "ai" field): first the ones made for its game unit, then the
    // generic ones. unitNames: the game unit(s) of the type (its "template" split at "|").
    IEnumerable<string> ApplicableBrainIds(IEnumerable<string> unitNames);

    // Goes to the AI tab with this unit type of the map loaded in the simulator and its AI open in the editor
    // (an empty type name just goes to the tab).
    void Edit(IWin32Window owner, string typeName, JsonObject unitType, Action beforeChange, Action afterChange);

    // Problems of the AI of a unit type (shown with the others when the map is saved). May return an empty list.
    IEnumerable<string> Validate(string typeName, JsonObject unitType);
}

internal static class EnemyAiEditors
{
    public const string AiField = "ai";

    // Set by the launcher when the Enemy AI tab exists.
    public static IEnemyAiEditor? Current { get; set; }

    public static bool Available => Current != null;

    // The game units a unit type is made from: its "template" without the "auto:" choices.
    public static List<string> UnitNamesOf(JsonObject? unitType)
    {
        if (unitType == null) return new List<string>();
        return Json.Str(unitType, "template").Split('|').Select(o => o.Trim())
            .Where(o => o.Length > 0 && !o.StartsWith("auto:", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    // The unit type names an AI file.
    public static bool HasAi(JsonObject unitType) => Json.Str(unitType, AiField).Trim().Length > 0;

    // The AI every new unit type gets: it chases the player (the generic AI of the old "chase" behavior).
    public const string DefaultAi = "generic_chase";

    // The built-in flight a unit type stands for: its old "behavior" field, or the one of a generic AI (generic_idle = idle).
    // "" = none (an AI of its own).
    public static string FlightOf(JsonObject? unitType)
    {
        if (unitType == null) return "";
        string behavior = Json.Str(unitType, "behavior").Trim();
        if (behavior.Length > 0) return behavior;
        string ai = Json.Str(unitType, AiField).Trim();
        return ai.StartsWith("generic_", StringComparison.OrdinalIgnoreCase) ? ai.Substring("generic_".Length).ToLowerInvariant() : "";
    }
}
