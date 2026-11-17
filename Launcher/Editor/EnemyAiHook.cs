using System.Text.Json.Nodes;

namespace SteambirdsLauncher.Editor;

// Place for the enemy AI editor that will come later (the "Enemy AI" tab and the menu item are grayed until then).
//
// What is already prepared for it:
//   - Every unit type of "types" may carry an "ai" object (AiField). The map editor never touches it and keeps it when
//     it saves, and the server ignores fields it does not know, so the AI editor can store its graph/script there
//     without a change in the map format.
//   - The unit types page shows an "Edit AI..." button that is enabled when a editor is registered in
//     EnemyAiEditors.Current.
//   - The save check does not look inside "ai"; an AI editor can add its own check through IEnemyAiEditor.Validate.
//
// Place of the enemy AI editor (greyed out until it exists): each type can have an "ai" object, which the map editor
// keeps and the server ignores.
internal interface IEnemyAiEditor
{
    string DisplayName { get; }

    // Opens the editor for one unit type. "beforeChange" saves the undo point, "afterChange" marks the map as changed.
    void Edit(IWin32Window owner, string typeName, JsonObject unitType, Action beforeChange, Action afterChange);

    // Problems of the AI of a unit type (shown with the others when the map is saved). May return an empty list.
    IEnumerable<string> Validate(string typeName, JsonObject unitType);
}

internal static class EnemyAiEditors
{
    public const string AiField = "ai";

    // null until the AI editor exists.
    public static IEnemyAiEditor? Current { get; set; }

    public static bool Available => Current != null;

    public static bool HasAi(JsonObject unitType) => unitType[AiField] != null;
}
