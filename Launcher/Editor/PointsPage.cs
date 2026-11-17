using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.Editor;

// "Points" page: everything that has a place on the map and a name the script or the server looks up: points, groups of
// points, areas, lines, values, routes, checkpoints, signs, decoration, buildings and single floor pieces.
internal sealed class PointsPage : EditorPage
{
    private sealed class Section
    {
        public string Title = "";
        public CollectionPanel Panel = null!;
    }

    private readonly List<Section> sections = new List<Section>();
    private readonly ComboBox picker;
    private readonly Panel host;
    private readonly CollectionPanel pointsPanel;
    private readonly CollectionPanel groupsPanel;
    private readonly CollectionPanel areasPanel;
    private readonly CollectionPanel linesPanel;
    private readonly CollectionPanel valuesPanel;
    private readonly CollectionPanel routesPanel;
    private readonly CollectionPanel checkpointsPanel;
    private readonly CollectionPanel signsPanel;
    private readonly CollectionPanel decorPanel;
    private readonly CollectionPanel buildingsPanel;
    private readonly CollectionPanel piecesPanel;

    public PointsPage(EditorContext context) : base(context)
    {
        JsonObject Root() => Document.Root;

        pointsPanel = new CollectionPanel(Session,
            new ObjectDictionarySource(
                () => Json.ObjOrNull(Root(), "points"),
                () => Json.Obj(Root(), "points"),
                name => Json.NewPoint(0, 0, MapCoords.FaceMinusV),
                (oldName, newName) => MapRefactor.Rename(Document, NameKind.Point, oldName, newName)),
            PointFields(), "point", listHeight: 150);

        groupsPanel = new CollectionPanel(Session,
            new PointListDictionarySource(() => Json.ObjOrNull(Root(), "groups"), () => Json.Obj(Root(), "groups")),
            new[] { PositionsField("pos", "Positions (u, v, rotation)", false) }, "group", listHeight: 150);

        areasPanel = new CollectionPanel(Session,
            new ObjectDictionarySource(
                () => Json.ObjOrNull(Root(), "areas"),
                () => Json.Obj(Root(), "areas"),
                name => new JsonObject { ["minU"] = -6, ["maxU"] = 6, ["minV"] = -6, ["maxV"] = 6 },
                (oldName, newName) => MapRefactor.Rename(Document, NameKind.Area, oldName, newName)),
            AreaFields(), "area", listHeight: 150);

        linesPanel = new CollectionPanel(Session,
            new NumberDictionarySource(
                () => Json.ObjOrNull(Root(), "lines"),
                () => Json.Obj(Root(), "lines"),
                (oldName, newName) => MapRefactor.Rename(Document, NameKind.Line, oldName, newName)),
            new[] { Coordinate("value", "v of the line") }, "line", listHeight: 150);

        valuesPanel = new CollectionPanel(Session,
            new NumberDictionarySource(() => Json.ObjOrNull(Root(), "values"), () => Json.Obj(Root(), "values")),
            new[] { Coordinate("value", "Value") }, "value", listHeight: 150);

        routesPanel = new CollectionPanel(Session,
            new PointListDictionarySource(() => Json.ObjOrNull(Root(), "routes"), () => Json.Obj(Root(), "routes")),
            new[] { PositionsField("pos", "Points (u, v)", true) }, "route", listHeight: 150);

        checkpointsPanel = new CollectionPanel(Session,
            new ObjectArraySource(
                () => Json.ArrOrNull(Root(), "checkpoints"),
                () => Json.Arr(Root(), "checkpoints"),
                "name",
                name => new JsonObject { ["name"] = name, ["activeAtV"] = 0, ["u"] = 0, ["v"] = 0, ["rot"] = MapCoords.FacePlusV }),
            CheckpointFields(), "checkpoint", listHeight: 150);

        signsPanel = new CollectionPanel(Session,
            new ObjectArraySource(
                () => Json.ArrOrNull(Root(), "signs"),
                () => Json.Arr(Root(), "signs"),
                "key",
                _ => new JsonObject { ["key"] = "", ["u"] = 0, ["v"] = 0, ["radius"] = 14, ["rot"] = 0 },
                uniqueNames: false),
            SignFields(), "sign", listHeight: 150);

        decorPanel = new CollectionPanel(Session,
            new ObjectArraySource(
                () => Json.ArrOrNull(Root(), "decor"),
                () => Json.Arr(Root(), "decor"),
                "name",
                name => new JsonObject { ["id"] = 0, ["name"] = "", ["u"] = 0, ["v"] = 0, ["z"] = 0.5, ["rot"] = 45 },
                uniqueNames: false),
            DecorFields(), "", listHeight: 150);

        buildingsPanel = new CollectionPanel(Session,
            new ObjectArraySource(
                () => Json.ArrOrNull(Root(), "buildings"),
                () => Json.Arr(Root(), "buildings"),
                "name",
                name => new JsonObject { ["prefab"] = 24, ["name"] = "", ["u"] = 0, ["v"] = 0, ["rot"] = MapCoords.FacePlusV },
                uniqueNames: false),
            BuildingFields(), "", listHeight: 150);

        piecesPanel = new CollectionPanel(Session,
            new ObjectArraySource(
                () => Json.ArrOrNull(Document.FloorJson, "pieces"),
                () => Json.Arr(Document.FloorJson, "pieces"),
                "name",
                name => new JsonObject { ["p"] = 0, ["q"] = 0, ["id"] = 0, ["name"] = "" },
                uniqueNames: false),
            PieceFields(), "", listHeight: 150);

        Add("Points", pointsPanel);
        Add("Groups of points", groupsPanel);
        Add("Areas", areasPanel);
        Add("Lines (v)", linesPanel);
        Add("Values", valuesPanel);
        Add("Routes", routesPanel);
        Add("Checkpoints", checkpointsPanel);
        Add("Signs", signsPanel);
        Add("Decoration", decorPanel);
        Add("Buildings", buildingsPanel);
        Add("Floor pieces", piecesPanel);

        picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Margin = new Padding(3, 3, 3, 6) };
        foreach (Section section in sections) picker.Items.Add(section.Title);
        picker.SelectedIndexChanged += (_, _) => ShowSection(picker.SelectedIndex);

        host = new Panel { Dock = DockStyle.Fill };
        foreach (Section section in sections)
        {
            section.Panel.Visible = false;
            host.Controls.Add(section.Panel);
        }

        Controls.Add(Stack((Ui.Flow(Ui.Text(L.T("Show")), picker), false), (host, true)));
        WireSelection();
        picker.SelectedIndex = 0;
        Reload();
    }

    private void Add(string title, CollectionPanel panel)
    {
        sections.Add(new Section { Title = L.T(title), Panel = panel });
        panel.ListChanged += () => Canvas.Invalidate();
    }

    private void ShowSection(int index)
    {
        for (int i = 0; i < sections.Count; i++) sections[i].Panel.Visible = i == index;
    }

    public override void Reload()
    {
        foreach (Section section in sections) section.Panel.Reload();
    }

    // ------------------------------------------------------------------ list <-> canvas

    private void WireSelection()
    {
        pointsPanel.UserSelected += item => ToCanvas(item, item?.Key);
        areasPanel.UserSelected += item => ToCanvas(item, item?.Key);
        checkpointsPanel.UserSelected += item => ToCanvas(item, null);
        signsPanel.UserSelected += item => ToCanvas(item, null);
        decorPanel.UserSelected += item => ToCanvas(item, null);
        buildingsPanel.UserSelected += item => ToCanvas(item, null);
        linesPanel.UserSelected += item =>
        {
            if (item != null) Canvas.SelectData(Json.ObjOrNull(Document.Root, "lines"), item.Key);
        };

        decorPanel.FieldChanged += (spec, item) => KeepIdAndNameTogether(decorPanel, spec, item);
        piecesPanel.FieldChanged += (spec, item) => KeepIdAndNameTogether(piecesPanel, spec, item);
    }

    private void ToCanvas(ListItem? item, string? name)
    {
        if (item != null) Canvas.SelectData(item.Data, name);
    }

    // A ground asset has an id and a name: picking one fills the other.
    private void KeepIdAndNameTogether(CollectionPanel panel, FieldSpec spec, ListItem item)
    {
        EditorAssets assets = EditorAssets.Current;
        if (spec.Key == "name")
        {
            string name = Json.Str(item.Data, "name");
            foreach (KeyValuePair<int, string> pair in assets.GroundNames)
            {
                if (pair.Value != name) continue;
                item.Data["id"] = pair.Key;
                break;
            }
        }
        else if (spec.Key == "id")
        {
            string name = assets.GroundName(Json.Int(item.Data, "id"));
            if (name.Length > 0) item.Data["name"] = name;
        }
        else
        {
            return;
        }
        panel.PropertyForm.RefreshValues();
        Session.Touch();
    }

    public override bool ShowObject(MapObject item)
    {
        switch (item.Kind)
        {
            case ObjectKind.Point:
                return Show(pointsPanel, p => p.SelectKey(item.Name));
            case ObjectKind.Area:
                return Show(areasPanel, p => p.SelectKey(item.Name));
            case ObjectKind.Line:
                return Show(linesPanel, p => p.SelectKey(item.Name));
            case ObjectKind.GroupPoint:
                return Show(groupsPanel, p => p.SelectKey(item.Owner));
            case ObjectKind.Route:
                return Show(routesPanel, p => p.SelectKey(item.Owner));
            case ObjectKind.Checkpoint:
                return Show(checkpointsPanel, p => p.SelectData(item.Data));
            case ObjectKind.Sign:
                return Show(signsPanel, p => p.SelectData(item.Data));
            case ObjectKind.Decor:
                return Show(Json.Has(item.Data, "prefab") ? buildingsPanel : decorPanel, p => p.SelectData(item.Data));
            default:
                return false;
        }
    }

    private bool Show(CollectionPanel panel, Func<CollectionPanel, bool> select)
    {
        int index = sections.FindIndex(s => ReferenceEquals(s.Panel, panel));
        if (index < 0) return false;
        if (picker.SelectedIndex != index) picker.SelectedIndex = index;
        select(panel);
        return true;
    }

    // ------------------------------------------------------------------ fields

    private static FieldSpec Coordinate(string key, string label)
    {
        return new FieldSpec(key, label, FieldKind.Number) { Min = -3000, Max = 3000, Increment = 0.5 };
    }

    private static FieldSpec Rotation(string key = "rot", double fallback = 0)
    {
        return new FieldSpec(key, "Rotation", FieldKind.Number)
        {
            Min = 0,
            Max = 360,
            Increment = 15,
            Default = fallback,
            Help = "Degrees clockwise from +y. 315 faces +v (the back of the dungeon), 135 faces -v (the entrance)."
        };
    }

    private static FieldSpec PositionsField(string key, string label, bool noRotation)
    {
        return new FieldSpec(key, label, FieldKind.Positions)
        {
            NoRotation = noRotation,
            Help = "One point per line: u, v" + (noRotation ? "" : ", rotation")
        };
    }

    private static IEnumerable<FieldSpec> PointFields()
    {
        yield return Coordinate("u", "u");
        yield return Coordinate("v", "v");
        yield return Rotation("rot", MapCoords.FaceMinusV);
        yield return new FieldSpec("note", "Note", FieldKind.Text);
    }

    private static IEnumerable<FieldSpec> AreaFields()
    {
        yield return Coordinate("minU", "Min u");
        yield return Coordinate("maxU", "Max u");
        yield return Coordinate("minV", "Min v");
        yield return Coordinate("maxV", "Max v");
    }

    private static IEnumerable<FieldSpec> CheckpointFields()
    {
        yield return new FieldSpec("name", "Checkpoint", FieldKind.Text);
        yield return Coordinate("activeAtV", "Active after v");
        yield return Coordinate("u", "u");
        yield return Coordinate("v", "v");
        yield return Rotation("rot", MapCoords.FacePlusV);
    }

    private static IEnumerable<FieldSpec> SignFields()
    {
        yield return new FieldSpec("key", "Text key", FieldKind.Text) { Help = "Key of the client's lore table. Required." };
        yield return Coordinate("u", "u");
        yield return Coordinate("v", "v");
        yield return new FieldSpec("radius", "Radius", FieldKind.Number) { Min = 1, Max = 200, Default = 14, Help = "The text appears when the plane is closer than this." };
        yield return Rotation();
    }

    private static IEnumerable<FieldSpec> GroundNameFields(string idKey)
    {
        yield return new FieldSpec(idKey, "Ground asset id", FieldKind.Int) { Min = 0, Max = 99999999 };
        yield return new FieldSpec("name", "Ground asset name", FieldKind.EditableChoice)
        {
            Choices = () => EditorAssets.Current.GroundNames.Values.Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
            Help = "Names come from ground_assets.txt. Picking a name fills the id."
        };
    }

    private static IEnumerable<FieldSpec> DecorFields()
    {
        foreach (FieldSpec field in GroundNameFields("id")) yield return field;
        yield return Coordinate("u", "u");
        yield return Coordinate("v", "v");
        yield return new FieldSpec("z", "Height (z)", FieldKind.Number)
        {
            Min = -100,
            Max = 100,
            Default = 0.5,
            Help = "0.5 is the floor level; a smaller number is higher."
        };
        yield return Rotation("rot", 45);
    }

    private static IEnumerable<FieldSpec> BuildingFields()
    {
        yield return new FieldSpec("prefab", "Prefab index", FieldKind.Int)
        {
            Min = 0,
            Max = 999,
            Help = "Index in prefab_table.txt: 12 Hangar, 14 Storage, 23 Tavern, 24 Store, 26 Crafter, 27 Pilot School, 28 Flock Hall, 29 Combat Sim, 30 Mega Carrier."
        };
        yield return new FieldSpec("name", "Name", FieldKind.Text);
        yield return Coordinate("u", "u");
        yield return Coordinate("v", "v");
        yield return Rotation("rot", MapCoords.FacePlusV);
        yield return new FieldSpec("store", "Store id", FieldKind.Text) { Help = "Only for the store building." };
    }

    private static IEnumerable<FieldSpec> PieceFields()
    {
        yield return new FieldSpec("p", "Slot p", FieldKind.Int) { Min = -400, Max = 400, Help = "Slot of 6 u." };
        yield return new FieldSpec("q", "Slot q", FieldKind.Int) { Min = -400, Max = 400 };
        foreach (FieldSpec field in GroundNameFields("id")) yield return field;
        yield return new FieldSpec("rot", "Rotation (counter-clockwise, relative to the tile)", FieldKind.OptionalNumber)
        {
            Help = "Empty = the client picks a random turn. Track pieces need a rotation."
        };
    }
}
