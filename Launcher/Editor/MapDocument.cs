using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.Editor;

// A map file open in the editor: the JSON tree plus the floor (kept apart as a grid while editing).
internal sealed class MapDocument
{
    public const string FormatName = "steambirds-map";
    public const int FormatVersion = 1;

    public MapDocument(JsonObject root)
    {
        Root = root;
        Floor.Load(FloorJson);
    }

    public JsonObject Root { get; private set; }
    public FloorGrid Floor { get; } = new FloorGrid();

    // File this map was opened from or saved to (null for a new map).
    public string? FilePath { get; set; }

    public JsonObject FloorJson => Json.Obj(Root, "floor");

    public string Id
    {
        get => Json.Str(Root, "id");
        set => Root["id"] = value;
    }

    public string Name
    {
        get => Json.Str(Root, "name");
        set => Root["name"] = value;
    }

    public string Terrain
    {
        get => Json.Str(Root, "terrain");
        set => Root["terrain"] = value;
    }

    public int Territory
    {
        get => Json.Int(Root, "territory");
        set => Root["territory"] = value;
    }

    // Dungeon maps need an entrance, an exit and a way to finish; the Nexus and the tutorial do not.
    public bool IsDungeon =>
        Terrain.StartsWith("Dungeon", StringComparison.OrdinalIgnoreCase) ||
        Id.StartsWith("dungeon", StringComparison.OrdinalIgnoreCase) ||
        Root["types"] != null || Root["encounters"] != null;

    // Name of the point where the exit carrier appears (exit.point, "portal" when not set).
    public string ExitPointName
    {
        get
        {
            string name = Json.Str(Json.ObjOrNull(Root, "exit"), "point");
            return name.Length > 0 ? name : "portal";
        }
    }

    // ------------------------------------------------------------------ file

    public static MapDocument Parse(string text)
    {
        JsonNode? node = JsonNode.Parse(text, null, JsoncFile.ParseOptions);
        if (node is not JsonObject root) throw new FormatException(L.T("The file is not a JSON object."));
        LegacyNames.Translate(root);   // maps made with the old Portuguese field names open too
        string format = Json.Str(root, "format");
        if (!string.Equals(format, FormatName, StringComparison.OrdinalIgnoreCase))
            throw new FormatException(L.F("This is not a map file: \"format\" should be \"{0}\".", FormatName));
        int version = Json.Int(root, "version");
        if (version < 1 || version > FormatVersion)
            throw new FormatException(L.F("Map format version {0} is not supported (the editor writes version {1}).", version, FormatVersion));
        return new MapDocument(root);
    }

    public static MapDocument Load(string path)
    {
        string text = TextFiles.ReadKeepingBom(path, out _);
        MapDocument document = Parse(text);
        document.FilePath = path;
        return document;
    }

    // The text of the file: the floor goes back into "chao.desenho" first.
    public string Serialize()
    {
        Floor.Save(FloorJson);
        return CompactJson.Write(Root);
    }

    // Used by undo and redo: replaces the whole content with an earlier text.
    public void Replace(string json)
    {
        MapDocument other = Parse(json);
        Root = other.Root;
        Floor.Load(FloorJson);
    }

    public void Save(string path)
    {
        TextFiles.WriteAtomic(path, Serialize(), withBom: false);
        FilePath = path;
    }
}

// The document being edited and its history. Everything that changes the map calls Checkpoint() first (saves the
// state before the change for Undo) and Touch() after (marks it as changed and redraws).
internal sealed class MapSession
{
    private const int MaxHistory = 150;

    private readonly List<string> undo = new List<string>();
    private readonly List<string> redo = new List<string>();
    private string lastCheckpoint = "";

    public MapSession(MapDocument document)
    {
        Document = document;
    }

    public MapDocument Document { get; private set; }
    public bool Dirty { get; private set; }

    // The content changed: redraw.
    public event Action? Changed;

    // The whole document was replaced (new, open, undo, redo): rebuild the pages.
    public event Action? Replaced;

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public void SetDocument(MapDocument document, bool dirty)
    {
        Document = document;
        undo.Clear();
        redo.Clear();
        lastCheckpoint = "";
        Dirty = dirty;
        Replaced?.Invoke();
        Changed?.Invoke();
    }

    // Saves the current state as a point to go back to. Call it before changing the map.
    public void Checkpoint()
    {
        string state = Document.Serialize();
        if (undo.Count > 0 && state == lastCheckpoint) return;
        undo.Add(state);
        lastCheckpoint = state;
        if (undo.Count > MaxHistory) undo.RemoveAt(0);
        redo.Clear();
    }

    // The map was changed: marks it as not saved and redraws.
    public void Touch()
    {
        Dirty = true;
        Changed?.Invoke();
    }

    public void MarkSaved()
    {
        Dirty = false;
        Changed?.Invoke();
    }

    // Replaces the whole content with this text (the "edit as text" dialog). Throws, and changes nothing, when the
    // text is not a valid map.
    public void ReplaceText(string json)
    {
        MapDocument.Parse(json);
        Checkpoint();
        Document.Replace(json);
        Dirty = true;
        Replaced?.Invoke();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (undo.Count == 0) return;
        redo.Add(Document.Serialize());
        string state = undo[undo.Count - 1];
        undo.RemoveAt(undo.Count - 1);
        lastCheckpoint = "";
        Document.Replace(state);
        Dirty = true;
        Replaced?.Invoke();
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (redo.Count == 0) return;
        undo.Add(Document.Serialize());
        string state = redo[redo.Count - 1];
        redo.RemoveAt(redo.Count - 1);
        lastCheckpoint = "";
        Document.Replace(state);
        Dirty = true;
        Replaced?.Invoke();
        Changed?.Invoke();
    }
}
