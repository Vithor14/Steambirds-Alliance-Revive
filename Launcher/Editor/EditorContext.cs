using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.Editor;

// What the property pages need from the editor: the map being edited, the canvas, the tooltips and the lists of
// names a page offers in its drop-downs (unit types, rooms, areas...).
internal sealed class EditorContext
{
    public EditorContext(MapSession session, MapCanvas canvas, ToolTip tip)
    {
        Session = session;
        Canvas = canvas;
        Tip = tip;
    }

    public MapSession Session { get; }
    public MapCanvas Canvas { get; }
    public ToolTip Tip { get; }
    public MapDocument Document => Session.Document;
    public JsonObject Root => Session.Document.Root;

    public IEnumerable<string> TypeNames() => Json.ObjectProperties(Json.ObjOrNull(Root, "types")).Select(p => p.Key);

    // Picture of the game unit a unit type of this map is made from (its "template"; a type with no template is
    // looked up by its own name). Null when the type, the unit or the pictures do not exist.
    public AssetPicture? PictureOfType(string typeName)
    {
        if (typeName.Length == 0 || !EditorAssets.Current.HasPictures) return null;
        JsonObject? type = Json.ObjOrNull(Json.ObjOrNull(Root, "types"), typeName);
        string template = Json.Str(type, "template");
        return EditorAssets.Current.PictureOfUnit(template.Length > 0 ? template : typeName);
    }

    public IEnumerable<string> RoomNames() =>
        Json.Objects(Json.ArrOrNull(Root, "encounters")).Select(r => Json.Str(r, "room")).Where(n => n.Length > 0);

    public IEnumerable<string> AreaNames() => Json.ObjectProperties(Json.ObjOrNull(Root, "areas")).Select(p => p.Key);

    public IEnumerable<string> PointNames() => Json.ObjectProperties(Json.ObjOrNull(Root, "points")).Select(p => p.Key);

    public IEnumerable<string> LineNames()
    {
        JsonObject? lines = Json.ObjOrNull(Root, "lines");
        if (lines == null) return Enumerable.Empty<string>();
        return lines.Where(p => Json.IsNumber(p.Value)).Select(p => p.Key).ToList();
    }

    public IEnumerable<string> RouteNames()
    {
        JsonObject? routes = Json.ObjOrNull(Root, "routes");
        if (routes == null) return Enumerable.Empty<string>();
        return routes.Where(p => p.Value is JsonArray).Select(p => p.Key).ToList();
    }

    public IEnumerable<string> MapIds() => DungeonCatalogFile.AvailableMaps();

    // Name of the first unit type, or the type picked in the palette (what a new group of enemies uses).
    public string DefaultUnitType()
    {
        if (Canvas.PlaceType.Length > 0) return Canvas.PlaceType;
        return TypeNames().FirstOrDefault() ?? "";
    }

    // Terrains the editor knows (dungeons of the game and the other maps).
    public static IEnumerable<string> Terrains()
    {
        foreach (TerritoryInfo info in Territories.Dungeons) yield return info.Terrain;
        yield return "Nexus";
        yield return "TutorialWalker";
    }

    // Behaviors the server understands for an enemy.
    public static readonly string[] Behaviors =
    {
        "chase", "circle", "turret", "kamikaze", "charge", "patrol", "escort", "idle"
    };

    // Ways a room wakes up (more can be written by hand: "death:<type>").
    public static readonly string[] WakeModes = { "onEnter", "script", "drill" };
}

// A page of the right-hand side of the editor. Pages are built again when the whole map is replaced (new, open, undo).
internal abstract class EditorPage : Panel
{
    protected EditorPage(EditorContext context)
    {
        Context = context;
        Dock = DockStyle.Fill;
    }

    protected EditorContext Context { get; }
    protected MapSession Session => Context.Session;
    protected MapDocument Document => Context.Session.Document;
    protected MapCanvas Canvas => Context.Canvas;

    // Reads everything again from the map (called when the map was replaced or a list of names may have changed).
    public abstract void Reload();

    // Shows the object selected on the canvas, when this page has a place for it. True when it did.
    public virtual bool ShowObject(MapObject item) => false;

    // Rows of controls, one under the other: the control marked "fill" takes the free space, the others their height.
    protected static TableLayoutPanel Stack(params (Control Control, bool Fill)[] rows)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach ((Control control, bool fill) in rows)
        {
            int row = table.RowCount;
            table.RowCount = row + 1;
            table.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            control.Dock = fill ? DockStyle.Fill : DockStyle.None;
            table.Controls.Add(control, 0, row);
        }
        return table;
    }

    // Short text under a form or a list.
    protected static Label Hint(string english, int width = 420)
    {
        return Ui.Wrapping(L.T(english), width, true);
    }
}
