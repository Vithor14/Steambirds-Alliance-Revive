using System.Drawing.Drawing2D;
using SteambirdsAi;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// The formation preview: a grid like the AI simulator, the points, and the pieces on the same path the server uses.
internal sealed class FormationCanvas : Control
{
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 40 };
    private readonly List<(float X, float Y)> trail = new List<(float X, float Y)>();
    private readonly Dictionary<string, float> health = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> fired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> suppressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (float X, float Y, float Rot, float Vel)> body = new Dictionary<string, (float, float, float, float)>(StringComparer.OrdinalIgnoreCase);
    private float time;
    private FormationFile? file;
    private int dragPoint = -1;
    private int motion;
    private float anchorX, anchorY, anchorRot, anchorSpeed;
    private bool anchorReady;
    private float goalX, goalY;
    private readonly Dictionary<string, float> deadFor = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

    public FormationCanvas()
    {
        DoubleBuffered = true;
        timer.Tick += (_, _) =>
        {
            if (Playing && file != null)
            {
                float dt = 0.04f * Math.Clamp(SpeedScale, 0.25f, 4f);
                time += dt;
                StepHealth(dt);
                StepLeader(dt);
                StepBodies(dt);
            }
            Invalidate();
        };
        timer.Start();
        VisibleChanged += (_, _) =>
        {
            if (Visible) timer.Start();
            else timer.Stop();
        };
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += (_, _) => dragPoint = -1;
    }

    public bool Playing { get; set; } = true;
    public bool EditPoints { get; set; }
    public float SpeedScale { get; set; } = 1f;
    public float ClickDamage { get; set; } = 0.25f;
    public float DrainPerSecond { get; set; }
    public bool AutoRespawn { get; set; } = true;

    // 0 = circle, 1 = stands still, 2 = flies to the last click.
    public int Motion
    {
        get => motion;
        set
        {
            if (motion == value) return;
            if (motion == 0)
            {
                (anchorX, anchorY, anchorRot, _) = CirclePose();
                anchorSpeed = 0;
                anchorReady = true;
            }
            motion = value;
        }
    }

    public event Action? Changed;

    public FormationFile? File
    {
        get => file;
        set
        {
            if (ReferenceEquals(file, value)) return;
            file = value;
            trail.Clear();
            body.Clear();
            health.Clear();
            seen.Clear();
            fired.Clear();
            suppressed.Clear();
            deadFor.Clear();
            time = 0f;
            dragPoint = -1;
            anchorReady = false;
        }
    }

    public void ResetView()
    {
        trail.Clear();
        body.Clear();
        health.Clear();
        seen.Clear();
        fired.Clear();
        suppressed.Clear();
        deadFor.Clear();
        time = 0f;
        anchorReady = false;
        Invalidate();
    }

    // Puts every piece back to full health and lets the links run again.
    public void Heal()
    {
        foreach (string key in health.Keys.ToList()) health[key] = 1f;
        suppressed.Clear();
        fired.Clear();
        seen.Clear();
        deadFor.Clear();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }

    private float PixelsPerUnit => Math.Max(8f, Math.Min(Width, Height) / 48f);

    private Point ToScreen(float x, float y) =>
        new Point((int)(Width / 2f + x * PixelsPerUnit), (int)(Height / 2f - y * PixelsPerUnit));

    private void ToWorld(int sx, int sy, out float x, out float y)
    {
        x = (sx - Width / 2f) / PixelsPerUnit;
        y = (Height / 2f - sy) / PixelsPerUnit;
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (file == null) return;
        (float lx, float ly, float rot, float speed) = Leader();
        if (!EditPoints && Motion == 2 && e.Button == MouseButtons.Left)
        {
            ToWorld(e.X, e.Y, out goalX, out goalY);
            return;
        }
        if (EditPoints && e.Button == MouseButtons.Left)
        {
            int hit = HitPoint(e.X, e.Y, lx, ly, rot);
            if (hit >= 0) { dragPoint = hit; return; }
            ToWorld(e.X, e.Y, out float wx, out float wy);
            FormationMotion.ToLocal(lx, ly, rot, wx, wy, out float localX, out float localY);
            file.Points.Add(new FormationPoint
            {
                Id = "p" + (file.Points.Count + 1),
                X = MathF.Round(localX, 2),
                Y = MathF.Round(localY, 2)
            });
            Changed?.Invoke();
            return;
        }
        if (EditPoints && e.Button == MouseButtons.Right)
        {
            int hit = HitPoint(e.X, e.Y, lx, ly, rot);
            if (hit >= 0)
            {
                file.Points.RemoveAt(hit);
                Changed?.Invoke();
                return;
            }
        }
        if (e.Button != MouseButtons.Right) return;
        string? piece = HitPiece(e.X, e.Y, lx, ly, rot, speed);
        if (piece == null) return;
        if (!health.ContainsKey(piece)) health[piece] = 1f;
        float hit = Math.Clamp(ClickDamage, 0.01f, 1f);
        health[piece] = Math.Max(0f, health[piece] - hit);
    }

    private void StepHealth(float dt)
    {
        if (DrainPerSecond <= 0f && !AutoRespawn) return;
        foreach (string id in health.Keys.ToList())
        {
            if (DrainPerSecond > 0f && health[id] > 0f)
                health[id] = Math.Max(0f, health[id] - DrainPerSecond * dt);
            if (health[id] > 0f)
            {
                deadFor.Remove(id);
                continue;
            }
            deadFor[id] = deadFor.TryGetValue(id, out float waited) ? waited + dt : dt;
            if (!AutoRespawn || deadFor[id] < 1.5f) continue;
            health[id] = 1f;
            deadFor.Remove(id);
            suppressed.Remove(id);
            fired.Clear();
            seen.Clear();
        }
    }

    private void StepLeader(float dt)
    {
        if (Motion != 2) return;
        if (!anchorReady)
        {
            anchorX = 0;
            anchorY = 0;
            anchorRot = 0;
            anchorReady = true;
        }
        float dx = goalX - anchorX;
        float dy = goalY - anchorY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        float pace = 6f * Math.Clamp(SpeedScale, 0.25f, 4f);
        if (dist < 0.2f)
        {
            anchorSpeed = 0;
            return;
        }
        anchorRot = MathF.Atan2(dx, dy) * 180f / MathF.PI;
        float step = Math.Min(dist, pace * dt);
        anchorX += dx / dist * step;
        anchorY += dy / dist * step;
        anchorSpeed = pace;
    }

    private (float X, float Y, float Rot, float Speed) CirclePose()
    {
        float angle = time * 28f;
        float speed = 4f * Math.Clamp(SpeedScale, 0.25f, 4f);
        return (MathF.Sin(angle * AiMath.Rad) * 10f, MathF.Cos(angle * AiMath.Rad) * 7f, angle + 90f, speed);
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (dragPoint < 0 || file == null || dragPoint >= file.Points.Count) return;
        (float lx, float ly, float rot, _) = Leader();
        ToWorld(e.X, e.Y, out float wx, out float wy);
        FormationMotion.ToLocal(lx, ly, rot, wx, wy, out float localX, out float localY);
        file.Points[dragPoint].X = MathF.Round(localX, 2);
        file.Points[dragPoint].Y = MathF.Round(localY, 2);
        Changed?.Invoke();
    }

    private void StepBodies(float dt)
    {
        if (file == null) return;
        (float lx, float ly, float rot, float speed) = Leader();
        if (trail.Count == 0 || AiMath.Distance(trail[^1].X, trail[^1].Y, lx, ly) >= 0.5f) trail.Add((lx, ly));
        if (trail.Count > 80) trail.RemoveRange(0, trail.Count - 80);
        foreach (FormationSlot slot in file.Expand())
        {
            FormationMotion.Pose target = FormationMotion.Place(slot, lx, ly, rot, speed, file.Spacing, trail, file.Points, time);
            if (!body.TryGetValue(slot.Id, out (float X, float Y, float Rot, float Vel) at))
            {
                body[slot.Id] = (target.X, target.Y, target.Rot, target.Speed);
                continue;
            }
            if (slot.FaceLocked)
            {
                body[slot.Id] = (target.X, target.Y, target.Rot, target.Speed);
                continue;
            }
            float x = at.X, y = at.Y, facing = at.Rot, vel = at.Vel;
            float pace = slot.TravelSpeed > 0.05f ? slot.TravelSpeed : 4f;
            FormationMotion.StepToward(ref x, ref y, ref facing, ref vel, target, pace, dt);
            body[slot.Id] = (x, y, facing, vel);
        }
    }

    private (float X, float Y, float Rot, float Speed) Leader()
    {
        if (Motion == 0) return CirclePose();
        if (!anchorReady)
        {
            anchorX = 0;
            anchorY = 0;
            anchorRot = 0;
            anchorSpeed = 0;
            anchorReady = true;
        }
        return (anchorX, anchorY, anchorRot, anchorSpeed);
    }

    private int HitPoint(int mx, int my, float lx, float ly, float rot)
    {
        if (file == null) return -1;
        int best = -1;
        float bestD = 12f;
        for (int i = 0; i < file.Points.Count; i++)
        {
            FormationMotion.Pose at = FormationMotion.Slot(lx, ly, 0f, 0f, file.Points[i].X, file.Points[i].Y);
            Point p = ToScreen(at.X, at.Y);
            float d = MathF.Sqrt((p.X - mx) * (p.X - mx) + (p.Y - my) * (p.Y - my));
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    private string? HitPiece(int mx, int my, float lx, float ly, float rot, float speed)
    {
        string? best = null;
        float bestD = 14f;
        void Consider(string id, float wx, float wy)
        {
            Point p = ToScreen(wx, wy);
            float d = MathF.Sqrt((p.X - mx) * (p.X - mx) + (p.Y - my) * (p.Y - my));
            if (d < bestD) { bestD = d; best = id; }
        }
        Consider("leader", lx, ly);
        if (file == null) return best;
        foreach (FormationSlot slot in file.Expand())
        {
            if (body.TryGetValue(slot.Id, out (float X, float Y, float Rot, float Vel) at)) Consider(slot.Id, at.X, at.Y);
            else
            {
                FormationMotion.Pose pose = FormationMotion.Place(slot, lx, ly, rot, speed, file.Spacing, trail, file.Points, time);
                Consider(slot.Id, pose.X, pose.Y);
            }
        }
        return best;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Palette palette = Theme.P;
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(palette.Input);
        DrawGrid(g, palette);
        if (file == null)
        {
            TextRenderer.DrawText(g, L.T("Pick a formation."), Font, ClientRectangle, palette.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        (float lx, float ly, float rot, float speed) = Leader();
        if (trail.Count == 0 || AiMath.Distance(trail[^1].X, trail[^1].Y, lx, ly) >= 0.5f) trail.Add((lx, ly));
        if (trail.Count > 80) trail.RemoveRange(0, trail.Count - 80);

        if (file.Points.Count >= 2)
        {
            Point[] poly = file.Points.Select(p =>
            {
                FormationMotion.Pose at = FormationMotion.Slot(lx, ly, 0f, 0f, p.X, p.Y);
                return ToScreen(at.X, at.Y);
            }).ToArray();
            using var pen = new Pen(Color.FromArgb(140, palette.Accent), 1.5f) { DashStyle = DashStyle.Dash };
            g.DrawPolygon(pen, poly);
        }
        for (int i = 0; i < file.Points.Count; i++)
        {
            FormationMotion.Pose at = FormationMotion.Slot(lx, ly, 0f, 0f, file.Points[i].X, file.Points[i].Y);
            Point p = ToScreen(at.X, at.Y);
            using var brush = new SolidBrush(palette.Accent);
            g.FillRectangle(brush, p.X - 4, p.Y - 4, 8, 8);
            TextRenderer.DrawText(g, file.Points[i].Id, Font, new Point(p.X + 6, p.Y - 8), palette.Muted);
        }

        var actors = new List<FormationActor>();
        var poses = new Dictionary<string, (float X, float Y, float Rot)>(StringComparer.OrdinalIgnoreCase);
        void Actor(string id, string role, int order, bool leader)
        {
            if (!health.ContainsKey(id)) health[id] = 1f;
            actors.Add(new FormationActor
            {
                Id = id, Role = role, RoleOrder = order, IsLeader = leader, Alive = health[id] > 0f,
                HealthFraction = health[id], ShieldSuppressed = suppressed.Contains(id)
            });
        }
        Actor("leader", "", 0, true);
        poses["leader"] = (lx, ly, rot);
        var roleCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (FormationSlot slot in file.Expand())
        {
            roleCount.TryGetValue(slot.Role, out int n);
            roleCount[slot.Role] = n + 1;
            Actor(slot.Id, slot.Role, n, false);
            if (body.TryGetValue(slot.Id, out (float X, float Y, float Rot, float Vel) at))
                poses[slot.Id] = (at.X, at.Y, at.Rot);
            else
            {
                FormationMotion.Pose pose = FormationMotion.Place(slot, lx, ly, rot, speed, file.Spacing, trail, file.Points, time);
                poses[slot.Id] = (pose.X, pose.Y, pose.Rot);
            }
        }
        var orders = new List<FormationOrder>();
        FormationRules.Evaluate(file, actors, seen, fired, orders);
        var shielded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (FormationOrder order in orders)
        {
            if (order.ClearShield) suppressed.Add(order.ActorId);
            if (order.Shield && !suppressed.Contains(order.ActorId)) shielded[order.ActorId] = order.Effect ?? "";
            if (order.Kill) health[order.ActorId] = 0f;
            if (order.HealFraction > 0f && health.TryGetValue(order.ActorId, out float hp) && hp > 0f)
                health[order.ActorId] = Math.Min(1f, hp + order.HealFraction);
        }

        foreach (KeyValuePair<string, (float X, float Y, float Rot)> pair in poses)
        {
            Point p = ToScreen(pair.Value.X, pair.Value.Y);
            bool dead = health.TryGetValue(pair.Key, out float hp) && hp <= 0f;
            Color fill = dead ? palette.Muted : pair.Key == "leader" ? Color.OrangeRed : Color.SteelBlue;
            using var brush = new SolidBrush(fill);
            g.FillEllipse(brush, p.X - 9, p.Y - 9, 18, 18);
            float face = pair.Value.Rot * MathF.PI / 180f;
            g.DrawLine(Pens.White, p.X, p.Y, p.X + MathF.Sin(face) * 12f, p.Y - MathF.Cos(face) * 12f);
            if (shielded.TryGetValue(pair.Key, out string? look))
            {
                Color ring = look.Equals("tankShield", StringComparison.OrdinalIgnoreCase) ? Color.Goldenrod : Color.DeepSkyBlue;
                using var pen = new Pen(ring, 2f);
                g.DrawEllipse(pen, p.X - 14, p.Y - 14, 28, 28);
            }
            TextRenderer.DrawText(g, pair.Key, Font, new Point(p.X + 12, p.Y - 8), palette.Text);
        }
        string hint = EditPoints
            ? L.T("Click the grid to add a point. Drag a point to move it. Right click a point to remove it.")
            : Motion == 2
                ? L.T("Click the grid to send the leader there. Right click damages the piece under the cursor.")
                : L.T("Right click damages the piece under the cursor.");
        TextRenderer.DrawText(g, hint, Font, new Rectangle(8, Height - 22, Width - 16, 20), palette.Muted, TextFormatFlags.Left);
    }

    private void DrawGrid(Graphics g, Theme.Palette palette)
    {
        float step = 4f * PixelsPerUnit;
        if (step < 14f) step *= 2f;
        using var pen = new Pen(Color.FromArgb(48, palette.Muted), 1f);
        float ox = Width / 2f;
        float oy = Height / 2f;
        for (float x = ox; x < Width; x += step) g.DrawLine(pen, x, 0, x, Height);
        for (float x = ox; x > 0; x -= step) g.DrawLine(pen, x, 0, x, Height);
        for (float y = oy; y < Height; y += step) g.DrawLine(pen, 0, y, Width, y);
        for (float y = oy; y > 0; y -= step) g.DrawLine(pen, 0, y, Width, y);
        using var axis = new Pen(Color.FromArgb(110, palette.Muted), 1f);
        g.DrawLine(axis, ox, 0, ox, Height);
        g.DrawLine(axis, 0, oy, Width, oy);
    }
}
