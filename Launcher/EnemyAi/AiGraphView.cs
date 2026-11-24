using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// The state machine of one AI drawn as a graph: a node per state, an arrow per transition (with its conditions).
// Clicking a node selects that state and the box under the graph says what it takes to go from it to every other state
// and what it takes to get to it. Nodes can be dragged; their places are saved in the file ("graphX" / "graphY" of the
// state, which only the launcher reads). It edits nothing else: the transitions are edited in the States tab.
internal sealed class AiGraphView : Panel
{
    private const float NodeWidth = 176f;
    private const float NodeHeight = 56f;
    private const float ColumnGap = 150f;
    private const int ColumnsPerBand = 4;
    private const float RowGap = 44f;

    private sealed class Node
    {
        public string Id = "";
        public JsonObject State = null!;
        public PointF At;   // top-left corner, in graph coordinates
        public int Order;
    }

    private sealed class Edge
    {
        public Node From = null!;
        public Node To = null!;
        public JsonObject Transition = null!;
        public int Index;       // among the transitions of From
        public int Parallel;    // among the arrows between the same two nodes
    }

    private readonly GraphCanvas canvas;
    private readonly TextBox info;
    private readonly List<Node> nodes = new List<Node>();
    private readonly List<Edge> edges = new List<Edge>();
    private JsonObject? root;
    private string initial = "";
    private string selected = "";
    // The state the simulator is running now (drawn in its own color, apart from the selected one).
    private string running = "";

    public AiGraphView()
    {
        Dock = DockStyle.Fill;
        canvas = new GraphCanvas(this) { Dock = DockStyle.Fill };
        info = new ThemedTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font(FontFamily.GenericSansSerif, 9f) };

        Button fit = Ui.Button(L.T("Fit view"), (_, _) => canvas.FitView());
        Button arrange = Ui.Button(L.T("Arrange automatically"), (_, _) => ArrangeAgain());
        Label help = Ui.Wrapping(L.T("Click a state to see what it takes to leave it and to arrive at it. Drag a state to move it. Right drag: move the view. Wheel: zoom. Violet = the state the simulator is running."), 600, true);
        var bar = Ui.Flow(fit, arrange);
        bar.Dock = DockStyle.Top;

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        table.Controls.Add(bar, 0, 0);
        table.Controls.Add(help, 0, 1);
        table.Controls.Add(canvas, 0, 2);
        table.Controls.Add(info, 0, 3);
        Controls.Add(table);
    }

    // A state was clicked (its id): the editor selects it in the list of states too.
    public event Action<string>? StateChosen;

    // Places of nodes were saved into the file.
    public event Action? Edited;

    public void Bind(JsonObject? brain)
    {
        root = brain;
        selected = "";
        Rebuild(true);
    }

    // The file changed in the other tab (states, transitions, names): drawn again, keeping the places the user gave.
    public void RedrawFromFile()
    {
        Rebuild(false);
    }

    // The state the simulator is in right now ("" = nothing is running). Called often: it only draws again when it changed.
    public void SetRunningState(string id)
    {
        if (running == id) return;
        running = id;
        canvas.Invalidate();
    }

    public void SelectState(string id)
    {
        if (selected == id) return;
        selected = id;
        UpdateInfo();
        canvas.Invalidate();
    }

    // ------------------------------------------------------------------ model

    private void Rebuild(bool fit)
    {
        nodes.Clear();
        edges.Clear();
        if (root == null)
        {
            info.Text = "";
            canvas.Invalidate();
            return;
        }

        initial = Json.Str(root, "initial");
        int order = 0;
        foreach (JsonObject state in Json.Objects(Json.ArrOrNull(root, "states")))
        {
            string id = Json.Str(state, "id");
            if (id.Length == 0 || nodes.Any(n => n.Id == id)) continue;
            nodes.Add(new Node { Id = id, State = state, Order = order++ });
        }
        if (initial.Length == 0 && nodes.Count > 0) initial = nodes[0].Id;

        foreach (Node from in nodes)
        {
            int index = 0;
            foreach (JsonObject transition in Json.Objects(Json.ArrOrNull(from.State, "transitions")))
            {
                Node? to = nodes.FirstOrDefault(n => n.Id == Json.Str(transition, "to"));
                if (to != null)
                    edges.Add(new Edge { From = from, To = to, Transition = transition, Index = index });
                index++;
            }
        }
        foreach (var group in edges.GroupBy(e => (e.From, e.To)))
        {
            int n = 0;
            foreach (Edge e in group) e.Parallel = n++;
        }

        ArrangeNodes();
        if (selected.Length > 0 && nodes.All(n => n.Id != selected)) selected = "";
        UpdateInfo();
        if (fit) canvas.FitView();
        canvas.Invalidate();
    }

    // Columns by the number of steps from the first state; states nobody reaches go in a last column. A state that has
    // "graphX" / "graphY" in the file stays where the user put it.
    private void ArrangeNodes()
    {
        var depth = new Dictionary<string, int>();
        var queue = new Queue<Node>();
        Node? start = nodes.FirstOrDefault(n => n.Id == initial) ?? nodes.FirstOrDefault();
        if (start != null)
        {
            depth[start.Id] = 0;
            queue.Enqueue(start);
        }
        while (queue.Count > 0)
        {
            Node at = queue.Dequeue();
            foreach (Edge e in edges.Where(x => x.From == at))
            {
                if (depth.ContainsKey(e.To.Id)) continue;
                depth[e.To.Id] = depth[at.Id] + 1;
                queue.Enqueue(e.To);
            }
        }
        int last = depth.Count == 0 ? 0 : depth.Values.Max() + 1;

        // A long chain (a boss with many phases) wraps into bands of a few columns, so the graph stays readable.
        var perLevel = new Dictionary<int, int>();
        foreach (Node n in nodes)
        {
            int level = depth.TryGetValue(n.Id, out int d0) ? d0 : last;
            perLevel[level] = perLevel.TryGetValue(level, out int c0) ? c0 + 1 : 1;
        }
        int tallest = perLevel.Count == 0 ? 1 : perLevel.Values.Max();
        float bandHeight = tallest * (NodeHeight + RowGap) + 40f;

        var rows = new Dictionary<int, int>();
        foreach (Node n in nodes)
        {
            int level = depth.TryGetValue(n.Id, out int d) ? d : last;
            rows.TryGetValue(level, out int row);
            rows[level] = row + 1;
            int column = level % ColumnsPerBand;
            int band = level / ColumnsPerBand;
            n.At = new PointF(column * (NodeWidth + ColumnGap), band * bandHeight + row * (NodeHeight + RowGap));
            if (Json.Has(n.State, "graphX") && Json.Has(n.State, "graphY"))
                n.At = new PointF((float)Json.Num(n.State, "graphX"), (float)Json.Num(n.State, "graphY"));
        }
    }

    private void ArrangeAgain()
    {
        if (root == null) return;
        foreach (Node n in nodes)
        {
            n.State.Remove("graphX");
            n.State.Remove("graphY");
        }
        ArrangeNodes();
        canvas.FitView();
        canvas.Invalidate();
        Edited?.Invoke();
    }

    private static string StateKind(JsonObject state)
    {
        string behavior = Json.Str(state, "behavior");
        if (behavior.Length > 0) return behavior;
        JsonObject? move = Json.ObjOrNull(state, "move");
        string mode = Json.Str(move, "mode", "?");
        if (mode == "builtin")
        {
            string flight = Json.Str(move, "builtin");
            return "builtin" + (flight.Length > 0 ? ": " + flight : "");
        }
        return mode;
    }

    private static string StateExtras(JsonObject state)
    {
        var parts = new List<string>();
        string summon = Json.Str(state, "summon");
        if (summon.Length > 0) parts.Add(L.T("summons") + " " + Math.Max(1, (int)Json.Num(state, "summonCount", 1)) + "x " + summon);
        if (Json.Bool(state, "shield")) parts.Add(L.T("shield") + (Json.Num(state, "shieldSeconds") > 0 ? " " + Json.Num(state, "shieldSeconds").ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "s" : ""));
        if (Json.Str(state, "effect").Length > 0) parts.Add(L.T("effect") + " " + Json.Str(state, "effect"));
        JsonObject? fire = Json.ObjOrNull(state, "fire");
        if (fire != null && !Json.Bool(fire, "enabled", true)) parts.Add(L.T("does not shoot"));
        return string.Join(", ", parts);
    }

    // ------------------------------------------------------------------ what the clicked state needs

    private static string ConditionsText(JsonObject transition)
    {
        List<JsonObject> conditions = Json.Objects(Json.ArrOrNull(transition, "when")).ToList();
        return conditions.Count == 0 ? L.T("always") : string.Join(" " + L.T("and") + " ", conditions.Select(AiFields.DescribeCondition));
    }

    // A short form of a condition for the labels of the arrows.
    private static string ShortCondition(JsonObject c)
    {
        string type = Json.Str(c, "type", "always");
        double v = Json.Num(c, "value");
        string num = v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        string text = type switch
        {
            "always" => L.T("always"),
            "timeInState" => L.T("after") + " " + num + (Json.Has(c, "max") ? "–" + Json.Num(c, "max").ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "") + "s",
            "playerWithin" => L.T("player") + " < " + num,
            "playerBeyond" => L.T("player") + " > " + num,
            "hasTarget" => L.T("has a player"),
            "healthBelow" => L.T("health") + " ≤ " + Math.Round(v * 100) + "%",
            "healthAbove" => L.T("health") + " ≥ " + Math.Round(v * 100) + "%",
            "recentlyHit" => L.T("hit in the last") + " " + num + "s",
            "facingTarget" => L.T("facing") + " ≤ " + num + "°",
            "summonsAtMost" => L.T("summons alive") + " ≤ " + num,
            "chance" => L.T("chance") + " " + num + "/s",
            _ => type
        };
        return Json.Bool(c, "not") ? L.T("not") + " (" + text + ")" : text;
    }

    private static string ShortConditions(JsonObject transition)
    {
        List<JsonObject> conditions = Json.Objects(Json.ArrOrNull(transition, "when")).ToList();
        return conditions.Count == 0 ? L.T("always") : string.Join(" & ", conditions.Select(ShortCondition));
    }

    private static string EdgesText(IEnumerable<Edge> alternatives)
    {
        return string.Join("   " + L.T("or") + "   ", alternatives.Select(e => ConditionsText(e.Transition)));
    }

    // Shortest chain of transitions from one state to another (by the number of steps); null = no way.
    private List<Edge>? PathBetween(Node from, Node to)
    {
        var came = new Dictionary<Node, Edge?> { [from] = null };
        var queue = new Queue<Node>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            Node at = queue.Dequeue();
            if (at == to) break;
            foreach (Edge e in edges.Where(x => x.From == at))
            {
                if (came.ContainsKey(e.To)) continue;
                came[e.To] = e;
                queue.Enqueue(e.To);
            }
        }
        if (!came.ContainsKey(to) || from == to) return null;
        var path = new List<Edge>();
        for (Node at = to; at != from; at = came[at]!.From) path.Add(came[at]!);
        path.Reverse();
        return path;
    }

    private string Route(Node from, Node to)
    {
        List<Edge> direct = edges.Where(e => e.From == from && e.To == to).ToList();
        if (direct.Count > 0) return EdgesText(direct);
        List<Edge>? path = PathBetween(from, to);
        if (path == null) return L.T("there is no way");
        var sb = new StringBuilder();
        sb.Append(L.F("{0} steps", path.Count)).Append(":  ");
        sb.Append(string.Join("  >  ", path.Select(e => e.From.Id + " → " + e.To.Id + " [" + string.Join(" " + L.T("or") + " ", edges.Where(x => x.From == e.From && x.To == e.To).Select(x => ShortConditions(x.Transition))) + "]")));
        return sb.ToString();
    }

    private void UpdateInfo()
    {
        Node? node = nodes.FirstOrDefault(n => n.Id == selected);
        if (node == null)
        {
            info.Text = nodes.Count == 0 ? "" : L.T("Click a state of the graph.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine(L.F("State \"{0}\"", node.Id) + (node.Id == initial ? "   (" + L.T("first state") + ")" : "") + "   [" + StateKind(node.State) + "]");
        string extras = StateExtras(node.State);
        if (extras.Length > 0) sb.AppendLine(extras);
        sb.AppendLine();

        sb.AppendLine(L.F("To leave \"{0}\" (what it takes to go to each other state):", node.Id));
        foreach (Node other in nodes.Where(n => n != node))
            sb.AppendLine("  → " + other.Id + ":  " + Route(node, other));
        List<Edge> loops = edges.Where(e => e.From == node && e.To == node).ToList();
        if (loops.Count > 0) sb.AppendLine("  ↺ " + node.Id + ":  " + EdgesText(loops));
        sb.AppendLine();

        sb.AppendLine(L.F("To arrive at \"{0}\" (what it takes from each other state):", node.Id));
        foreach (Node other in nodes.Where(n => n != node))
            sb.AppendLine("  ← " + other.Id + ":  " + Route(other, node));
        sb.AppendLine();
        sb.Append(L.T("The transitions of a state are checked in order; the first whose conditions are all true wins."));
        info.Text = sb.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
        info.SelectionStart = 0;
    }

    // ------------------------------------------------------------------ canvas

    private sealed class GraphCanvas : Control
    {
        private readonly AiGraphView view;
        private float zoom = 1f;
        private float panX = 20f;
        private float panY = 20f;
        private Node? dragged;
        private PointF dragOffset;
        private bool moved;
        private bool panning;
        private Point lastMouse;
        private Point downAt;

        public GraphCanvas(AiGraphView view)
        {
            this.view = view;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            MinimumSize = new Size(160, 120);
        }

        public void FitView()
        {
            if (view.nodes.Count == 0 || Width < 40 || Height < 40) return;
            float minX = view.nodes.Min(n => n.At.X);
            float minY = view.nodes.Min(n => n.At.Y);
            float maxX = view.nodes.Max(n => n.At.X) + NodeWidth;
            float maxY = view.nodes.Max(n => n.At.Y) + NodeHeight;
            float w = Math.Max(1f, maxX - minX + 60f);
            float h = Math.Max(1f, maxY - minY + 60f);
            zoom = Math.Clamp(Math.Min(Width / w, Height / h), 0.3f, 1.4f);
            panX = (Width - (maxX - minX) * zoom) / 2f - minX * zoom;
            panY = (Height - (maxY - minY) * zoom) / 2f - minY * zoom;
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            FitView();
        }

        private PointF ToGraph(Point p) => new PointF((p.X - panX) / zoom, (p.Y - panY) / zoom);

        private Node? NodeAt(Point p)
        {
            PointF g = ToGraph(p);
            for (int i = view.nodes.Count - 1; i >= 0; i--)
            {
                Node n = view.nodes[i];
                if (g.X >= n.At.X && g.X <= n.At.X + NodeWidth && g.Y >= n.At.Y && g.Y <= n.At.Y + NodeHeight) return n;
            }
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            lastMouse = e.Location;
            downAt = e.Location;
            moved = false;
            if (e.Button == MouseButtons.Left)
            {
                Node? hit = NodeAt(e.Location);
                if (hit != null)
                {
                    dragged = hit;
                    PointF g = ToGraph(e.Location);
                    dragOffset = new PointF(g.X - hit.At.X, g.Y - hit.At.Y);
                    view.selected = hit.Id;
                    view.UpdateInfo();
                    view.StateChosen?.Invoke(hit.Id);
                }
                else
                {
                    panning = true;
                    if (view.selected.Length > 0)
                    {
                        view.selected = "";
                        view.UpdateInfo();
                    }
                }
            }
            else
            {
                panning = true;
            }
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (Math.Abs(e.X - downAt.X) + Math.Abs(e.Y - downAt.Y) > 4) moved = true;
            if (dragged != null && moved)
            {
                PointF g = ToGraph(e.Location);
                dragged.At = new PointF(g.X - dragOffset.X, g.Y - dragOffset.Y);
                Invalidate();
            }
            else if (panning)
            {
                panX += e.X - lastMouse.X;
                panY += e.Y - lastMouse.Y;
                Invalidate();
            }
            Cursor = NodeAt(e.Location) != null ? Cursors.Hand : Cursors.Default;
            lastMouse = e.Location;
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragged != null && moved)
            {
                dragged.State["graphX"] = Math.Round(dragged.At.X);
                dragged.State["graphY"] = Math.Round(dragged.At.Y);
                view.Edited?.Invoke();
            }
            dragged = null;
            panning = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float before = zoom;
            zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.12f : 1f / 1.12f), 0.25f, 2.5f);
            float k = zoom / before;
            panX = e.X - (e.X - panX) * k;
            panY = e.Y - (e.Y - panY) * k;
            Invalidate();
            base.OnMouseWheel(e);
        }

        // ---- drawing

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Palette p = Theme.P;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(p.Input);

            if (view.nodes.Count == 0)
            {
                TextRenderer.DrawText(g, L.T("This AI has no states yet."), Font, new Point(12, 12), p.Muted);
                return;
            }

            g.TranslateTransform(panX, panY);
            g.ScaleTransform(zoom, zoom);

            Node? sel = view.nodes.FirstOrDefault(n => n.Id == view.selected);
            foreach (Edge edge in view.edges) DrawEdge(g, p, edge, sel);
            foreach (Node node in view.nodes) DrawNode(g, p, node, sel);
        }

        private static PointF Center(Node n) => new PointF(n.At.X + NodeWidth / 2f, n.At.Y + NodeHeight / 2f);

        // Where the line from the center of the node toward "target" leaves its rectangle.
        private static PointF Border(Node n, PointF target)
        {
            PointF c = Center(n);
            float dx = target.X - c.X;
            float dy = target.Y - c.Y;
            if (Math.Abs(dx) < 0.001f && Math.Abs(dy) < 0.001f) return c;
            float sx = Math.Abs(dx) > 0.001f ? NodeWidth / 2f / Math.Abs(dx) : float.MaxValue;
            float sy = Math.Abs(dy) > 0.001f ? NodeHeight / 2f / Math.Abs(dy) : float.MaxValue;
            float s = Math.Min(sx, sy);
            return new PointF(c.X + dx * s, c.Y + dy * s);
        }

        private void DrawEdge(Graphics g, Theme.Palette p, Edge edge, Node? sel)
        {
            bool outgoing = sel != null && edge.From == sel;
            bool incoming = sel != null && edge.To == sel && !outgoing;
            Color color = outgoing ? p.Good : incoming ? Color.FromArgb(90, 150, 230) : sel != null ? Color.FromArgb(70, p.Muted) : Color.FromArgb(170, p.Muted);
            float width = outgoing || incoming ? 2.4f : 1.4f;

            PointF a;
            PointF b;
            PointF c1;
            PointF c2;
            PointF labelAt;
            if (edge.From == edge.To)
            {
                // A loop above the node.
                float x = edge.From.At.X + NodeWidth * 0.62f;
                float lift = 34f + edge.Parallel * 14f;
                a = new PointF(x, edge.From.At.Y);
                b = new PointF(x - NodeWidth * 0.3f, edge.From.At.Y);
                c1 = new PointF(x + 14f, edge.From.At.Y - lift);
                c2 = new PointF(b.X - 14f, edge.From.At.Y - lift);
                labelAt = new PointF((a.X + b.X) / 2f, edge.From.At.Y - lift * 0.75f - 8f);
            }
            else
            {
                PointF ca = Center(edge.From);
                PointF cb = Center(edge.To);
                float dx = cb.X - ca.X;
                float dy = cb.Y - ca.Y;
                float len = Math.Max(1f, MathF.Sqrt(dx * dx + dy * dy));
                // Perpendicular, always to the right of the direction of travel: the arrow back takes the other side.
                float px = -dy / len;
                float py = dx / len;
                float bend = 26f + edge.Parallel * 26f;
                PointF mid = new PointF((ca.X + cb.X) / 2f + px * bend, (ca.Y + cb.Y) / 2f + py * bend);
                a = Border(edge.From, mid);
                b = Border(edge.To, mid);
                c1 = new PointF(a.X + (mid.X - a.X) * 0.9f, a.Y + (mid.Y - a.Y) * 0.9f);
                c2 = new PointF(b.X + (mid.X - b.X) * 0.9f, b.Y + (mid.Y - b.Y) * 0.9f);
                labelAt = new PointF(mid.X, mid.Y);
            }

            using (var pen = new Pen(color, width))
            {
                g.DrawBezier(pen, a, c1, c2, b);
                // Arrow head along the last piece of the curve.
                float ax = b.X - c2.X;
                float ay = b.Y - c2.Y;
                float al = Math.Max(0.001f, MathF.Sqrt(ax * ax + ay * ay));
                ax /= al;
                ay /= al;
                var head = new[]
                {
                    b,
                    new PointF(b.X - ax * 11f - ay * 5.5f, b.Y - ay * 11f + ax * 5.5f),
                    new PointF(b.X - ax * 11f + ay * 5.5f, b.Y - ay * 11f - ax * 5.5f)
                };
                using var brush = new SolidBrush(color);
                g.FillPolygon(brush, head);
            }

            // The conditions, short, where there is room to read them.
            if (zoom >= 0.55f && (sel == null || outgoing || incoming))
            {
                string text = Short(ShortConditions(edge.Transition), 30);
                using var font = new Font(FontFamily.GenericSansSerif, 8f);
                SizeF size = g.MeasureString(text, font);
                var box = new RectangleF(labelAt.X - size.Width / 2f - 2f, labelAt.Y - size.Height / 2f, size.Width + 4f, size.Height);
                using (var back = new SolidBrush(Color.FromArgb(215, p.Input))) g.FillRectangle(back, box);
                using var ink = new SolidBrush(outgoing ? p.Good : incoming ? Color.FromArgb(120, 170, 240) : p.Muted);
                g.DrawString(text, font, ink, box.X + 2f, box.Y);
            }
        }

        private static string Short(string text, int max) => text.Length <= max ? text : text.Substring(0, max - 1) + "…";

        private void DrawNode(Graphics g, Theme.Palette p, Node node, Node? sel)
        {
            bool isSel = node == sel;
            bool isInitial = node.Id == view.initial;
            var rect = new RectangleF(node.At.X, node.At.Y, NodeWidth, NodeHeight);
            using GraphicsPath path = RoundedRect(rect, 9f);

            bool isRunning = node.Id == view.running;
            bool related = sel != null && !isSel && view.edges.Any(e => (e.From == sel && e.To == node) || (e.To == sel && e.From == node));
            // Selected = the theme's selection color; running in the simulator = violet. A state that is both keeps the violet
            // fill with the selection's border.
            Color runningFill = Color.FromArgb(118, 84, 200);
            Color fill = isRunning ? runningFill : isSel ? p.Selection : p.Surface;
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
            Color border = isSel ? p.AccentText : isRunning ? Color.FromArgb(70, 40, 150) : isInitial ? p.Accent : related ? p.Muted : p.Border;
            using (var pen = new Pen(border, isSel || isInitial || isRunning ? 2.4f : 1.4f)) g.DrawPath(pen, path);

            Color ink = isRunning ? Color.White : isSel ? p.SelectionText : p.Text;
            using var bold = new Font(FontFamily.GenericSansSerif, 9.5f, FontStyle.Bold);
            using var small = new Font(FontFamily.GenericSansSerif, 8f);
            using var inkBrush = new SolidBrush(ink);
            using var mutedBrush = new SolidBrush(isRunning ? Color.FromArgb(225, 215, 250) : isSel ? p.SelectionText : p.Muted);
            var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString((isInitial ? "▶ " : "") + node.Id, bold, inkBrush, new RectangleF(rect.X + 8f, rect.Y + 5f, rect.Width - 14f, 18f), format);
            if (isRunning)
                g.DrawString("● " + L.T("running"), small, inkBrush, new RectangleF(rect.Right - 70f, rect.Y + 6f, 64f, 14f), new StringFormat { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap });
            g.DrawString(StateKind(node.State), small, mutedBrush, new RectangleF(rect.X + 8f, rect.Y + 23f, rect.Width - 14f, 14f), format);
            string extras = StateExtras(node.State);
            if (extras.Length > 0)
                g.DrawString(extras, small, mutedBrush, new RectangleF(rect.X + 8f, rect.Y + 37f, rect.Width - 14f, 14f), format);
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = radius * 2f;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
