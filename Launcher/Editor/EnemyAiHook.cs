using System.Text.Json.Nodes;

namespace SteambirdsLauncher.Editor;

// The link between the map editor and the Enemy AI tab (Launcher\EnemyAi).
//
// A unit type of a map ("types") picks its AI with "ai": "<id>", the id (file name) of a file of the server's ai\brains
// folder. The AI itself is not in the map: it lives in its own file, so it can be edited, shared and loaded on its own,
// and the same AI can serve many maps. The server reads the field; without it (or with an id that does not exist) the unit
// uses the plain "behavior" of the type.
internal interface IEnemyAiEditor
{
    string DisplayName { get; }

    // Ids of the AIs that exist (for the list of the "ai" field).
    IEnumerable<string> BrainIds();

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

    // The unit type names an AI file.
    public static bool HasAi(JsonObject unitType) => Json.Str(unitType, AiField).Trim().Length > 0;
}
