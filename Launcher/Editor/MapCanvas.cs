using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.Editor;

internal enum EditorTool
{
    Select,
    Floor,
    Walls,
    Entrance,
    Exit,
    Point,
    GroupPoint,
    Checkpoint,
    Sign,
    Area,
    Line,
    Gate,
    Unit,
    Route
}

// The map as the editor draws it: u to the right, v up, one tile = 12 u. This file has the view, the tools and the
// mouse and keyboard; MapCanvas.Paint.cs draws.
//
// Mouse: left button uses the tool; middle button (or right button with the Select tool, or Space + left) moves the
// view; the wheel zooms. Floor and Walls tools: right button erases.
// Keys: Delete removes, Q / E rotate (Shift = 45 degrees), arrows nudge, Ctrl+D duplicates, F fits the map, Esc = Select tool.
internal sealed partial class MapCanvas : Control
{
    private enum DragMode { None, Pan, Move, Resize, Paint, Rect, Area, GateLine }

    private readonly MapSession session;
    private List<MapObject>? objects;
    private MapObject? selected;
    private bool spaceDown;

    private double centerU;
    private double centerV;
    private double scale = 3.0;

    private EditorTool tool = EditorTool.Select;
    private DragMode drag = DragMode.None;
    private Point lastMouse;
    private double dragStartU;
    private double dragStartV;
    private double dragLastU;
    private double dragLastV;
    private bool dragChecked;
    private int resizeCorner;
    private bool paintOn;
    private (int X, int Y)? lastCell;
    private (int X, int Y) rectStart;
    private (int X, int Y) rectEnd;
    private double hoverU;
    private double hoverV;
    private bool hoverValid;
    private string? routeName;
    private string statusText = "";

    public MapCanvas(MapSession session)
    {
        this.session = session;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Cross;
        session.Changed += OnSessionChanged;
        session.Replaced += OnSessionReplaced;
    }

    // ------------------------------------------------------------------ options (set by the editor tab)

    public bool ShowFloor { get; set; } = true;
    public bool ShowWalls { get; set; } = true;
    public bool ShowGrid { get; set; } = true;
    public bool ShowAreas { get; set; } = true;
    public bool ShowGates { get; set; } = true;
    public bool ShowPoints { get; set; } = true;
    public bool ShowUnits { get; set; } = true;
    public bool ShowDecor { get; set; } = true;
    public bool ShowLabels { get; set; } = true;
    public bool UsePictures { get; set; } = true;

    // Step objects snap to when they are placed or moved (0 = free).
    public double SnapStep { get; set; } = 0.5;

    // Floor and Walls tools: drag a rectangle instead of painting.
    public bool RectangleMode { get; set; }

    // Walls tool: paint a whole tile (4 x 4 sub-cells) at a time.
    public bool WallTileSized { get; set; }

    public string PlaceType { get; set; } = "";
    public string PlaceRoom { get; set; } = "";
    public string PlaceGroup { get; set; } = "";
    public string GateType { get; set; } = "";

    public EditorTool Tool
    {
        get => tool;
        set
        {
            if (tool == value) return;
            tool = value;
            routeName = null;
            Cursor = value == EditorTool.Select ? Cursors.Default : Cursors.Cross;
            ToolChanged?.Invoke();
            Invalidate();
        }
    }

    public MapObject? Selected => selected;

    // The selection changed (the property pages show it).
    public event Action? SelectionChanged;

    // Objects were added, removed or moved by the canvas (the pages refresh their lists).
    public event Action? StructureChanged;

    // The canvas changed the tool by itself (after placing the entrance, for example).
    public event Action? ToolChanged;

    // Text for the status bar: the cursor position and the last message.
    public event Action<string>? StatusChanged;

    // The user pressed a command key (Ctrl+Z, Ctrl+Y, Ctrl+S): "undo", "redo", "save".
    public event Action<string>? CommandRequested;

    private MapDocument Doc => session.Document;

    // ------------------------------------------------------------------ session events

    private void OnSessionChanged()
    {
        objects = null;
        Invalidate();
    }

    private void OnSessionReplaced()
    {
        objects = null;
        if (selected != null) SetSelected(null);
        routeName = null;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            session.Changed -= OnSessionChanged;
            session.Replaced -= OnSessionReplaced;
            labelFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    private List<MapObject> Objects()
    {
        return objects ??= MapObjects.All(Doc);
    }

    // ------------------------------------------------------------------ view

    private float ScreenX(double u) => (float)(Width / 2.0 + (u - centerU) * scale);
    private float ScreenY(double v) => (float)(Height / 2.0 - (v - centerV) * scale);
    private double MapU(double x) => centerU + (x - Width / 2.0) / scale;
    private double MapV(double y) => centerV - (y - Height / 2.0) / scale;

    public void CenterOn(double u, double v)
    {
        centerU = u;
        centerV = v;
        Invalidate();
    }

    // Shows the whole map (floor and objects), or the area around the origin when the map is empty.
    public void FitToMap()
    {
        double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
        if (Doc.Floor.GetBounds(out int minA, out int maxA, out int minB, out int maxB))
        {
            minU = minA * MapCoords.Tile;
            maxU = (maxA + 1) * MapCoords.Tile;
            minV = minB * MapCoords.Tile;
            maxV = (maxB + 1) * MapCoords.Tile;
        }
        foreach (MapObject item in Objects())
        {
            if (item.Kind == ObjectKind.Line) continue;
            item.GetPosition(out double u, out double v);
            minU = Math.Min(minU, u);
            maxU = Math.Max(maxU, u);
            minV = Math.Min(minV, v);
            maxV = Math.Max(maxV, v);
        }
        if (minU > maxU)
        {
            minU = -60;
            maxU = 60;
            minV = -60;
            maxV = 60;
        }

        double width = Math.Max(maxU - minU, 24) + 24;
        double height = Math.Max(maxV - minV, 24) + 24;
        scale = Math.Clamp(Math.Min(Math.Max(1, Width) / width, Math.Max(1, Height) / height), 0.3, 12);
        centerU = (minU + maxU) / 2;
        centerV = (minV + maxV) / 2;
        Invalidate();
    }

    private void ZoomAt(Point at, double factor)
    {
        double u = MapU(at.X);
        double v = MapV(at.Y);
        scale = Math.Clamp(scale * factor, 0.3, 40);
        centerU = u - (at.X - Width / 2.0) / scale;
        centerV = v + (at.Y - Height / 2.0) / scale;
        Invalidate();
    }

    public void ZoomBy(double factor) => ZoomAt(new Point(Width / 2, Height / 2), factor);

    private double Snap(double value)
    {
        if (SnapStep <= 0) return value;
        return Math.Round(value / SnapStep, MidpointRounding.AwayFromZero) * SnapStep;
    }

    // ------------------------------------------------------------------ selection

    public void Select(MapObject? item)
    {
        SetSelected(item);
        Invalidate();
    }

    // Selects the object that holds this JSON object (used by the pages).
    public void SelectData(JsonObject? data, string? name = null)
    {
        if (data == null)
        {
            Select(null);
            return;
        }
        foreach (MapObject item in Objects())
        {
            if (!ReferenceEquals(item.Data, data)) continue;
            if (name != null && item.Name != name) continue;
            Select(item);
            return;
        }
    }

    private void SetSelected(MapObject? item)
    {
        if (selected == null && item == null) return;
        selected = item;
        SelectionChanged?.Invoke();
    }

    public void DeleteSelected()
    {
        if (selected == null) return;
        session.Checkpoint();
        selected.Delete();
        SetSelected(null);
        session.Touch();
        StructureChanged?.Invoke();
    }

    public void DuplicateSelected()
    {
        MapObject? item = selected;
        if (item == null) return;
        session.Checkpoint();
        JsonNode? copy = item.Data.DeepClone();
        if (copy is not JsonObject clone) return;

        MapObject? created = null;
        switch (item.Kind)
        {
            case ObjectKind.Point:
            case ObjectKind.Area:
                if (item.Dictionary == null) return;
                string name = Json.UniqueName(item.Dictionary.Select(p => p.Key), item.Name);
                item.Dictionary[name] = clone;
                break;
            case ObjectKind.Line:
            case ObjectKind.Entrance:
                return;
            default:
                if (item.Container == null) return;
                if (item.Kind == ObjectKind.Gate)
                {
                    ShiftGate(clone, item.Vertical, 1);
                    string gateName = Json.UniqueName(Json.Objects(item.Container).Select(g => Json.Str(g, "name")), Json.Str(clone, "name"));
                    clone["name"] = gateName;
                }
                else
                {
                    if (clone["u"] != null) clone["u"] = Json.Round(Json.Num(clone, "u") + MapCoords.Slot);
                    if (item.Kind == ObjectKind.Checkpoint)
                        clone["name"] = Json.UniqueName(Json.Objects(item.Container).Select(g => Json.Str(g, "name")), Json.Str(clone, "name"));
                }
                item.Container.Add(clone);
                break;
        }

        session.Touch();
        objects = null;
        created = FindData(clone);
        SetSelected(created);
        StructureChanged?.Invoke();
        Invalidate();
    }

    private static void ShiftGate(JsonObject gate, bool vertical, int tiles)
    {
        int d = tiles * MapCoords.SubsPerTile;
        if (vertical) gate["si"] = Json.Int(gate, "si") + d;
        else gate["siFrom"] = Json.Int(gate, "siFrom") + d;
        if (!vertical) gate["siTo"] = Json.Int(gate, "siTo") + d;
    }

    private MapObject? FindData(JsonObject data)
    {
        foreach (MapObject item in Objects())
        {
            if (ReferenceEquals(item.Data, data)) return item;
        }
        return null;
    }

    // ------------------------------------------------------------------ messages

    private void Say(string text)
    {
        statusText = text;
        PublishStatus();
    }

    private void PublishStatus()
    {
        string where = "";
        if (hoverValid)
        {
            int a = MapCoords.TileOf(hoverU);
            int b = MapCoords.TileOf(hoverV);
            where = L.F("u {0:0.0}, v {1:0.0}   tile ({2}, {3})   sub-cell ({4}, {5})", hoverU, hoverV, a, b,
                MapCoords.SubOf(hoverU), MapCoords.SubOf(hoverV));
        }
        StatusChanged?.Invoke(statusText.Length > 0 ? where + "   |   " + statusText : where);
    }

    // ------------------------------------------------------------------ keyboard

    protected override bool IsInputKey(Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        if (key is Keys.Left or Keys.Right or Keys.Up or Keys.Down) return true;
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space)
        {
            spaceDown = true;
            Cursor = Cursors.SizeAll;
            e.Handled = true;
            return;
        }

        if (e.Control)
        {
            switch (e.KeyCode)
            {
                case Keys.Z:
                    CommandRequested?.Invoke(e.Shift ? "redo" : "undo");
                    e.Handled = true;
                    return;
                case Keys.Y:
                    CommandRequested?.Invoke("redo");
                    e.Handled = true;
                    return;
                case Keys.S:
                    CommandRequested?.Invoke("save");
                    e.Handled = true;
                    return;
                case Keys.D:
                    DuplicateSelected();
                    e.Handled = true;
                    return;
            }
        }

        switch (e.KeyCode)
        {
            case Keys.Delete:
            case Keys.Back:
                DeleteSelected();
                e.Handled = true;
                break;
            case Keys.Escape:
                if (drag != DragMode.None) CancelDrag();
                routeName = null;
                Tool = EditorTool.Select;
                e.Handled = true;
                break;
            case Keys.F:
            case Keys.Home:
                FitToMap();
                e.Handled = true;
                break;
            case Keys.Q:
                RotateSelected(e.Shift ? -45 : -15);
                e.Handled = true;
                break;
            case Keys.E:
                RotateSelected(e.Shift ? 45 : 15);
                e.Handled = true;
                break;
            case Keys.Left:
                Nudge(-1, 0, e.Shift);
                e.Handled = true;
                break;
            case Keys.Right:
                Nudge(1, 0, e.Shift);
                e.Handled = true;
                break;
            case Keys.Up:
                Nudge(0, 1, e.Shift);
                e.Handled = true;
                break;
            case Keys.Down:
                Nudge(0, -1, e.Shift);
                e.Handled = true;
                break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space)
        {
            spaceDown = false;
            Cursor = tool == EditorTool.Select ? Cursors.Default : Cursors.Cross;
        }
    }

    private void RotateSelected(double degrees)
    {
        MapObject? item = selected;
        if (item == null || !item.HasRotation) return;
        session.Checkpoint();
        item.Rotation = item.Rotation + degrees;
        session.Touch();
        StructureChanged?.Invoke();
    }

    private void Nudge(int du, int dv, bool big)
    {
        MapObject? item = selected;
        if (item == null) return;
        double step = item.Kind == ObjectKind.Gate ? MapCoords.Sub : (SnapStep > 0 ? SnapStep : 1.0);
        if (big) step *= 4;
        session.Checkpoint();
        item.MoveBy(du * step, dv * step);
        session.Touch();
        StructureChanged?.Invoke();
    }

    // ------------------------------------------------------------------ mouse

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomAt(e.Location, e.Delta > 0 ? 1.15 : 1 / 1.15);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hoverValid = false;
        PublishStatus();
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        lastMouse = e.Location;
        double u = MapU(e.X);
        double v = MapV(e.Y);
        dragStartU = dragLastU = u;
        dragStartV = dragLastV = v;
        dragChecked = false;

        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && spaceDown))
        {
            drag = DragMode.Pan;
            return;
        }

        bool paintTool = tool == EditorTool.Floor || tool == EditorTool.Walls;
        if (e.Button == MouseButtons.Right && !paintTool)
        {
            if (tool == EditorTool.Unit)
            {
                MapObject? hit = HitTest(e.Location, ObjectKind.Unit);
                if (hit != null)
                {
                    SetSelected(hit);
                    DeleteSelected();
                    return;
                }
            }
            drag = DragMode.Pan;
            return;
        }
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;

        switch (tool)
        {
            case EditorTool.Select:
                BeginSelect(e.Location, u, v);
                break;
            case EditorTool.Floor:
            case EditorTool.Walls:
                paintOn = e.Button == MouseButtons.Left;
                int cell = tool == EditorTool.Floor || WallTileSized ? 12 : 3;
                var start = (MapCoords.Floor(u / cell), MapCoords.Floor(v / cell));
                if (RectangleMode)
                {
                    drag = DragMode.Rect;
                    rectStart = rectEnd = start;
                }
                else
                {
                    drag = DragMode.Paint;
                    session.Checkpoint();
                    lastCell = null;
                    PaintCells(u, v, u, v);
                }
                break;
            case EditorTool.Area:
                drag = DragMode.Area;
                break;
            case EditorTool.Gate:
                drag = DragMode.GateLine;
                break;
            default:
                PlaceWithTool(u, v);
                break;
        }
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        double u = MapU(e.X);
        double v = MapV(e.Y);
        hoverU = u;
        hoverV = v;
        hoverValid = true;
        PublishStatus();

        switch (drag)
        {
            case DragMode.Pan:
                centerU -= (e.X - lastMouse.X) / scale;
                centerV += (e.Y - lastMouse.Y) / scale;
                break;
            case DragMode.Move:
                MoveSelected(u, v);
                break;
            case DragMode.Resize:
                ResizeSelected(u, v);
                break;
            case DragMode.Paint:
                PaintCells(dragLastU, dragLastV, u, v);
                break;
            case DragMode.Rect:
            {
                int cell = tool == EditorTool.Floor || WallTileSized ? 12 : 3;
                rectEnd = (MapCoords.Floor(u / cell), MapCoords.Floor(v / cell));
                break;
            }
        }
        dragLastU = u;
        dragLastV = v;
        lastMouse = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        DragMode finished = drag;
        drag = DragMode.None;
        double u = MapU(e.X);
        double v = MapV(e.Y);

        switch (finished)
        {
            case DragMode.Rect:
                ApplyRectangle();
                break;
            case DragMode.Area:
                CreateArea(dragStartU, dragStartV, u, v);
                break;
            case DragMode.GateLine:
                CreateGate(dragStartU, dragStartV, u, v);
                break;
            case DragMode.Resize:
                NormalizeSelectedArea();
                break;
            case DragMode.Move:
            case DragMode.Paint:
                if (dragChecked) StructureChanged?.Invoke();
                break;
        }
        Invalidate();
    }

    private void CancelDrag()
    {
        drag = DragMode.None;
        Invalidate();
    }

    // ------------------------------------------------------------------ select tool

    private void BeginSelect(Point at, double u, double v)
    {
        if (selected is { Kind: ObjectKind.Area })
        {
            int corner = HitAreaCorner(selected, at);
            if (corner >= 0)
            {
                resizeCorner = corner;
                drag = DragMode.Resize;
                return;
            }
        }

        MapObject? hit = HitTest(at, null);
        SetSelected(hit);
        if (hit != null && hit.CanMove) drag = DragMode.Move;
    }

    private void MoveSelected(double u, double v)
    {
        MapObject? item = selected;
        if (item == null) return;

        double du = u - dragLastU;
        double dv = v - dragLastV;
        if (SnapStep > 0 && item.Kind != ObjectKind.Gate && item.Kind != ObjectKind.Area)
        {
            // Snap the position, not the movement: the object follows the cursor and lands on the step.
            item.GetPosition(out double cu, out double cv);
            double tu = Snap(cu + du);
            double tv = Snap(cv + dv);
            du = tu - cu;
            dv = tv - cv;
            if (Math.Abs(du) < 1e-9 && Math.Abs(dv) < 1e-9) return;
            dragLastU += du;
            dragLastV += dv;
        }
        else if (item.Kind == ObjectKind.Gate)
        {
            // Gates move by whole sub-cells: wait until the cursor went a whole sub-cell from where we last moved.
            du = Math.Round((u - dragLastU) / MapCoords.Sub) * MapCoords.Sub;
            dv = Math.Round((v - dragLastV) / MapCoords.Sub) * MapCoords.Sub;
            if (du == 0 && dv == 0) return;
            dragLastU += du;
            dragLastV += dv;
        }
        else
        {
            dragLastU = u;
            dragLastV = v;
        }

        if (!dragChecked)
        {
            session.Checkpoint();
            dragChecked = true;
        }
        item.MoveBy(du, dv);
        session.Touch();
    }

    private void ResizeSelected(double u, double v)
    {
        MapObject? item = selected;
        if (item == null || item.Kind != ObjectKind.Area) return;
        if (!dragChecked)
        {
            session.Checkpoint();
            dragChecked = true;
        }
        double su = Snap(u);
        double sv = Snap(v);
        // Corners: 0 = min u / min v, 1 = max u / min v, 2 = max u / max v, 3 = min u / max v.
        if (resizeCorner == 0 || resizeCorner == 3) item.Data["minU"] = Json.Round(su);
        else item.Data["maxU"] = Json.Round(su);
        if (resizeCorner == 0 || resizeCorner == 1) item.Data["minV"] = Json.Round(sv);
        else item.Data["maxV"] = Json.Round(sv);
        session.Touch();
    }

    // After a resize the minimum may be bigger than the maximum: swap them.
    private void NormalizeSelectedArea()
    {
        MapObject? item = selected;
        if (item == null || item.Kind != ObjectKind.Area) return;
        double minU = Json.Num(item.Data, "minU"), maxU = Json.Num(item.Data, "maxU");
        double minV = Json.Num(item.Data, "minV"), maxV = Json.Num(item.Data, "maxV");
        item.Data["minU"] = Math.Min(minU, maxU);
        item.Data["maxU"] = Math.Max(minU, maxU);
        item.Data["minV"] = Math.Min(minV, maxV);
        item.Data["maxV"] = Math.Max(minV, maxV);
        session.Touch();
        if (dragChecked) StructureChanged?.Invoke();
    }

    // ------------------------------------------------------------------ hit test

    private bool IsKindVisible(ObjectKind kind)
    {
        return kind switch
        {
            ObjectKind.Area => ShowAreas,
            ObjectKind.Line => ShowAreas,
            ObjectKind.Gate => ShowGates,
            ObjectKind.Route => ShowPoints,
            ObjectKind.Decor => ShowDecor,
            ObjectKind.Unit => ShowUnits,
            _ => ShowPoints
        };
    }

    private MapObject? HitTest(Point at, ObjectKind? only)
    {
        List<MapObject> list = Objects();
        for (int i = list.Count - 1; i >= 0; i--)
        {
            MapObject item = list[i];
            if (only.HasValue && item.Kind != only.Value) continue;
            if (!IsKindVisible(item.Kind)) continue;
            if (item.Kind == ObjectKind.Area) continue;
            if (Hits(item, at)) return item;
        }
        if (only.HasValue && only.Value != ObjectKind.Area) return null;

        // Areas last, by their border only (an area usually covers a whole room).
        for (int i = list.Count - 1; i >= 0; i--)
        {
            MapObject item = list[i];
            if (item.Kind != ObjectKind.Area || !ShowAreas) continue;
            float x0 = ScreenX(Json.Num(item.Data, "minU"));
            float x1 = ScreenX(Json.Num(item.Data, "maxU"));
            float y0 = ScreenY(Json.Num(item.Data, "maxV"));
            float y1 = ScreenY(Json.Num(item.Data, "minV"));
            const float tolerance = 6;
            bool insideOuter = at.X >= x0 - tolerance && at.X <= x1 + tolerance && at.Y >= y0 - tolerance && at.Y <= y1 + tolerance;
            bool insideInner = at.X > x0 + tolerance && at.X < x1 - tolerance && at.Y > y0 + tolerance && at.Y < y1 - tolerance;
            if (insideOuter && !insideInner) return item;
        }
        return null;
    }

    private bool Hits(MapObject item, Point at)
    {
        item.GetPosition(out double u, out double v);
        double dx = ScreenX(u) - at.X;
        double dy = ScreenY(v) - at.Y;
        switch (item.Kind)
        {
            case ObjectKind.Line:
                return Math.Abs(dy) <= 5;
            case ObjectKind.Gate:
            {
                item.GateBounds(out double minU, out double maxU, out double minV, out double maxV);
                float x0 = ScreenX(minU) - 4, x1 = ScreenX(maxU) + 4, y0 = ScreenY(maxV) - 4, y1 = ScreenY(minV) + 4;
                return at.X >= x0 && at.X <= x1 && at.Y >= y0 && at.Y <= y1;
            }
            case ObjectKind.Unit:
            {
                double radius = Math.Max(9, UnitRadius(item.Name) * scale);
                return dx * dx + dy * dy <= radius * radius;
            }
            case ObjectKind.Entrance:
                return dx * dx + dy * dy <= 13 * 13;
            default:
                return dx * dx + dy * dy <= 10 * 10;
        }
    }

    private int HitAreaCorner(MapObject area, Point at)
    {
        float x0 = ScreenX(Json.Num(area.Data, "minU"));
        float x1 = ScreenX(Json.Num(area.Data, "maxU"));
        float y0 = ScreenY(Json.Num(area.Data, "maxV"));
        float y1 = ScreenY(Json.Num(area.Data, "minV"));
        (float X, float Y)[] corners = { (x0, y1), (x1, y1), (x1, y0), (x0, y0) };
        for (int i = 0; i < corners.Length; i++)
        {
            if (Math.Abs(corners[i].X - at.X) <= 7 && Math.Abs(corners[i].Y - at.Y) <= 7) return i;
        }
        return -1;
    }

    // ------------------------------------------------------------------ painting floor and walls

    private void PaintCells(double u0, double v0, double u1, double v1)
    {
        double cellSize = tool == EditorTool.Floor || WallTileSized ? 12 : 3;
        double steps = Math.Max(Math.Abs(u1 - u0), Math.Abs(v1 - v0)) / (cellSize / 2);
        int count = Math.Max(1, (int)Math.Ceiling(steps));
        bool changed = false;
        for (int i = 0; i <= count; i++)
        {
            double t = count == 0 ? 1 : i / (double)count;
            double u = u0 + (u1 - u0) * t;
            double v = v0 + (v1 - v0) * t;
            var cell = (MapCoords.Floor(u / cellSize), MapCoords.Floor(v / cellSize));
            if (lastCell.HasValue && lastCell.Value == cell) continue;
            lastCell = cell;
            changed |= ApplyCell(cell.Item1, cell.Item2, paintOn);
        }
        if (changed)
        {
            dragChecked = true;
            session.Touch();
        }
    }

    // Floor tool: the cell is a tile. Walls tool: a sub-cell, or a whole tile when WallTileSized.
    private bool ApplyCell(int x, int y, bool on)
    {
        FloorGrid floor = Doc.Floor;
        if (tool == EditorTool.Floor)
        {
            if (on && !MapCoords.InsideTileRange(x, y))
            {
                Say(L.F("Tiles must stay inside {0} to {1}: that is the size of the game grid.", MapCoords.MinTile, MapCoords.MaxTile));
                return false;
            }
            return floor.SetTile(x, y, on);
        }

        bool changed = false;
        if (WallTileSized)
        {
            int per = MapCoords.SubsPerTile;
            for (int i = 0; i < per; i++)
            {
                for (int j = 0; j < per; j++) changed |= floor.SetWall(x * per + i, y * per + j, on);
            }
            return changed;
        }
        return floor.SetWall(x, y, on);
    }

    private void ApplyRectangle()
    {
        int x0 = Math.Min(rectStart.X, rectEnd.X), x1 = Math.Max(rectStart.X, rectEnd.X);
        int y0 = Math.Min(rectStart.Y, rectEnd.Y), y1 = Math.Max(rectStart.Y, rectEnd.Y);
        session.Checkpoint();
        bool changed = false;
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++) changed |= ApplyCell(x, y, paintOn);
        }
        if (changed)
        {
            session.Touch();
            StructureChanged?.Invoke();
        }
    }

    // ------------------------------------------------------------------ placing objects

    private void PlaceWithTool(double rawU, double rawV)
    {
        double u = Snap(rawU);
        double v = Snap(rawV);
        JsonObject root = Doc.Root;
        MapObject? created = null;
        bool backToSelect = false;

        switch (tool)
        {
            case EditorTool.Entrance:
            {
                session.Checkpoint();
                JsonObject spawn = root["spawn"] as JsonObject ?? new JsonObject();
                Json.SetPoint(spawn, u, v, Json.Has(spawn, "rot") ? Json.Num(spawn, "rot") : MapCoords.FacePlusV);
                root["spawn"] = spawn;
                backToSelect = true;
                objects = null;
                created = FindData(spawn);
                break;
            }
            case EditorTool.Exit:
            {
                session.Checkpoint();
                JsonObject points = Json.Obj(root, "points");
                string name = Doc.ExitPointName;
                JsonObject point = points[name] as JsonObject ?? new JsonObject();
                Json.SetPoint(point, u, v, Json.Has(point, "rot") ? Json.Num(point, "rot") : 0);
                points[name] = point;
                backToSelect = true;
                objects = null;
                created = FindData(point);
                break;
            }
            case EditorTool.Point:
            {
                session.Checkpoint();
                JsonObject points = Json.Obj(root, "points");
                string name = Json.UniqueName(points.Select(p => p.Key), "point");
                JsonObject point = Json.NewPoint(u, v, MapCoords.FacePlusV);
                points[name] = point;
                objects = null;
                created = FindData(point);
                break;
            }
            case EditorTool.GroupPoint:
            {
                session.Checkpoint();
                string groupName = PlaceGroup.Trim().Length > 0 ? PlaceGroup.Trim() : "group";
                JsonArray group = Json.Arr(Json.Obj(root, "groups"), groupName);
                JsonObject point = Json.NewPoint(u, v, MapCoords.FacePlusV);
                group.Add(point);
                objects = null;
                created = FindData(point);
                break;
            }
            case EditorTool.Checkpoint:
            {
                session.Checkpoint();
                JsonArray list = Json.Arr(root, "checkpoints");
                string name = Json.UniqueName(Json.Objects(list).Select(c => Json.Str(c, "name")), "checkpoint");
                var checkpoint = new JsonObject { ["name"] = name, ["activeAtV"] = Json.Round(v - 4) };
                Json.SetPoint(checkpoint, u, v, MapCoords.FacePlusV);
                list.Add(checkpoint);
                objects = null;
                created = FindData(checkpoint);
                break;
            }
            case EditorTool.Sign:
            {
                session.Checkpoint();
                JsonArray list = Json.Arr(root, "signs");
                var sign = new JsonObject { ["key"] = "" };
                Json.SetPoint(sign, u, v, 0);
                sign["radius"] = 14;
                sign["rot"] = 0;
                list.Add(sign);
                objects = null;
                created = FindData(sign);
                break;
            }
            case EditorTool.Line:
            {
                session.Checkpoint();
                JsonObject lines = Json.Obj(root, "lines");
                string name = Json.UniqueName(lines.Select(p => p.Key), "line");
                lines[name] = Json.Round(v);
                objects = null;
                created = Objects().FirstOrDefault(o => o.Kind == ObjectKind.Line && o.Name == name);
                break;
            }
            case EditorTool.Unit:
                created = PlaceUnit(u, v);
                break;
            case EditorTool.Route:
            {
                session.Checkpoint();
                JsonObject routes = Json.Obj(root, "routes");
                routeName ??= Json.UniqueName(routes.Select(p => p.Key), "route");
                JsonArray route = Json.Arr(routes, routeName);
                var vertex = new JsonObject();
                Json.SetPoint(vertex, u, v);
                route.Add(vertex);
                objects = null;
                created = FindData(vertex);
                break;
            }
        }

        if (created == null) return;
        SetSelected(created);
        session.Touch();
        StructureChanged?.Invoke();
        if (backToSelect) Tool = EditorTool.Select;
    }

    // Adds a unit of the chosen type to the chosen room. Units of the same type with nothing special go in the same group.
    private MapObject? PlaceUnit(double u, double v)
    {
        if (PlaceType.Length == 0)
        {
            Say(L.T("Choose a unit type in the toolbar first."));
            return null;
        }
        if (PlaceRoom.Length == 0)
        {
            Say(L.T("Choose a room in the toolbar first (create one in the Rooms tab)."));
            return null;
        }
        JsonObject? room = Json.Objects(Json.ArrOrNull(Doc.Root, "encounters")).FirstOrDefault(r => Json.Str(r, "room") == PlaceRoom);
        if (room == null)
        {
            Say(L.F("The room \"{0}\" does not exist.", PlaceRoom));
            return null;
        }

        session.Checkpoint();
        JsonArray groups = Json.Arr(room, "enemies");
        JsonObject? group = Json.Objects(groups).FirstOrDefault(g =>
            Json.Str(g, "type") == PlaceType && !Json.Has(g, "route") && !Json.Has(g, "wakes") &&
            !Json.Has(g, "behavior") && !Json.Has(g, "note") && !Json.Has(g, "miniboss"));
        if (group == null)
        {
            group = new JsonObject { ["type"] = PlaceType, ["pos"] = new JsonArray() };
            groups.Add(group);
        }
        JsonArray positions = Json.Arr(group, "pos");
        JsonObject position = Json.NewPoint(u, v, MapCoords.FaceMinusV);
        positions.Add(position);
        objects = null;
        MapObject? created = FindData(position);
        if (created != null) Say(L.F("Added a {0} to room {1}.", PlaceType, PlaceRoom));
        return created;
    }

    private void CreateArea(double u0, double v0, double u1, double v1)
    {
        double minU = Snap(Math.Min(u0, u1)), maxU = Snap(Math.Max(u0, u1));
        double minV = Snap(Math.Min(v0, v1)), maxV = Snap(Math.Max(v0, v1));
        if (maxU - minU < 3 || maxV - minV < 3)
        {
            // A click: an area of 24 x 24 u around it.
            minU = Snap(u0 - 12);
            maxU = Snap(u0 + 12);
            minV = Snap(v0 - 12);
            maxV = Snap(v0 + 12);
        }
        session.Checkpoint();
        JsonObject areas = Json.Obj(Doc.Root, "areas");
        string name = Json.UniqueName(areas.Select(p => p.Key), "area");
        var area = new JsonObject
        {
            ["minU"] = Json.Round(minU),
            ["maxU"] = Json.Round(maxU),
            ["minV"] = Json.Round(minV),
            ["maxV"] = Json.Round(maxV)
        };
        areas[name] = area;
        objects = null;
        SetSelected(FindData(area));
        session.Touch();
        StructureChanged?.Invoke();
        Tool = EditorTool.Select;
    }

    // A drag along u makes a gate in a row of sub-cells ("gates"), a drag along v a gate in a column ("sideGates").
    // A click makes a gate one tile wide in a row.
    private void CreateGate(double u0, double v0, double u1, double v1)
    {
        double du = Math.Abs(u1 - u0);
        double dv = Math.Abs(v1 - v0);
        bool column = dv > du && dv >= MapCoords.Sub;
        string type = GateType.Length > 0 ? GateType : FirstGateType();

        var gate = new JsonObject();
        JsonArray list;
        if (column)
        {
            int si = MapCoords.SubOf(u0);
            int first = MapCoords.SubOf(Math.Min(v0, v1));
            int last = MapCoords.SubOf(Math.Max(v0, v1));
            list = Json.Arr(Doc.Root, "sideGates");
            gate["name"] = Json.UniqueName(GateNames(), "gate");
            gate["type"] = type;
            gate["si"] = si;
            gate["sjFrom"] = first;
            gate["sjTo"] = last;
        }
        else
        {
            int sj = MapCoords.SubOf(v0);
            int first, last;
            if (du >= MapCoords.Sub)
            {
                first = MapCoords.SubOf(Math.Min(u0, u1));
                last = MapCoords.SubOf(Math.Max(u0, u1));
            }
            else
            {
                first = MapCoords.TileOf(u0) * MapCoords.SubsPerTile;
                last = first + MapCoords.SubsPerTile - 1;
            }
            list = Json.Arr(Doc.Root, "gates");
            gate["name"] = Json.UniqueName(GateNames(), "gate");
            gate["type"] = type;
            gate["sj"] = sj;
            gate["siFrom"] = first;
            gate["siTo"] = last;
        }
        gate["damageable"] = true;

        session.Checkpoint();
        list.Add(gate);
        objects = null;
        SetSelected(FindData(gate));
        session.Touch();
        StructureChanged?.Invoke();
        Tool = EditorTool.Select;
    }

    private IEnumerable<string> GateNames()
    {
        return Json.Objects(Json.ArrOrNull(Doc.Root, "gates")).Concat(Json.Objects(Json.ArrOrNull(Doc.Root, "sideGates")))
            .Select(g => Json.Str(g, "name"));
    }

    // The type for a new gate when none is chosen: a unit type that looks like a wall, else the first one.
    private string FirstGateType()
    {
        JsonObject? types = Json.ObjOrNull(Doc.Root, "types");
        if (types == null) return "";
        foreach (KeyValuePair<string, JsonObject> pair in Json.ObjectProperties(types))
        {
            string text = (pair.Key + " " + Json.Str(pair.Value, "template")).ToLowerInvariant();
            if (text.Contains("wall") || text.Contains("gate")) return pair.Key;
        }
        return types.Select(p => p.Key).FirstOrDefault() ?? "";
    }
}
