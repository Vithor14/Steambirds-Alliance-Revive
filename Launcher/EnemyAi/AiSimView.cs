using System.Diagnostics;
using System.Drawing.Drawing2D;
using SteambirdsAi;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// The simulator: runs an AI (the same code the server runs, Shared\EnemyAi) against a scripted player plane and draws
// it with the art of the game unit, its guns and bullets. The player flies where you click, in a circle, or stands still.
internal sealed class AiSimView : Panel
{
    private static string lastUnitName = "";

    private readonly LauncherContext context;
    private readonly AiSim sim = new AiSim();
    private readonly SimCanvas canvas;
    private readonly ComboBox unitBox;
    private readonly CheckBox armedOnly;
    private readonly Button playButton;
    private readonly ComboBox speedBox;
    private readonly ComboBox playerBox;
    private readonly NumberBox playerSpeed;
    private readonly NumberBox playerRadius;
    private readonly CheckBox playerVisible;
    private readonly NumberBox drain;
    private readonly CheckBox respawn;
    private readonly NumberBox speedMult;
    private readonly NumberBox turnMult;
    private readonly NumberBox fireMult;
    private readonly NumberBox aimLead;
    private readonly CheckBox freeAim;
    private readonly Label status;
    private readonly TextBox log;
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 33 };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<PointF> trail = new List<PointF>();
    private double lastTick;
    private double lastTrail;
    private bool playing = true;
    private bool loading;
    private AiBrain? brain;
    private string title = "";
    private AssetPicture? picture;

    public AiSimView(LauncherContext context)
    {
        this.context = context;
        Dock = DockStyle.Fill;
        sim.EventAdded += AddLog;

        canvas = new SimCanvas(this) { Dock = DockStyle.Fill };

        unitBox = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems, Width = 190 };
        unitBox.SelectionChangeCommitted += (_, _) => UnitChosen();
        unitBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            UnitChosen();
        };
        armedOnly = new ThemedCheckBox { Text = L.T("Armed units only"), AutoSize = true, Checked = true };
        armedOnly.CheckedChanged += (_, _) => FillUnits();

        playButton = Ui.Button(L.T("Pause"), (_, _) => TogglePlay());
        Button restart = Ui.Button(L.T("Restart"), (_, _) => Restart());
        speedBox = Combo(new[] { "0.25x", "0.5x", "1x", "2x", "4x" }, 2, 64);
        playerBox = Combo(new[] { L.T("Follows my clicks"), L.T("Flies in a circle"), L.T("Stands still") }, 1, 150);
        playerBox.SelectedIndexChanged += (_, _) => PlayerModeChanged();
        playerSpeed = Number(8, 0, 60, 1, 64);
        playerSpeed.ValueChanged += (_, _) => sim.PlayerSpeed = (float)playerSpeed.Value;
        playerRadius = Number(22, 4, 120, 2, 64);
        playerRadius.ValueChanged += (_, _) => sim.CircleRadius = (float)playerRadius.Value;
        playerVisible = new ThemedCheckBox { Text = L.T("Player in sight"), AutoSize = true, Checked = true };
        playerVisible.CheckedChanged += (_, _) => sim.PlayerVisible = playerVisible.Checked;

        drain = Number(0, 0, 100, 1, 64);
        drain.ValueChanged += (_, _) => sim.HealthDrainPerSecond = (float)drain.Value / 100f;
        Button hit = Ui.Button(L.T("Hit -25%"), (_, _) => sim.Damage(0.25f));
        Button heal = Ui.Button(L.T("Heal"), (_, _) => sim.Damage(-1f));
        respawn = new ThemedCheckBox { Text = L.T("Comes back after dying"), AutoSize = true, Checked = true };
        respawn.CheckedChanged += (_, _) => sim.AutoRespawn = respawn.Checked;

        speedMult = Number(1, 0.1m, 5, 0.1m, 64);
        turnMult = Number(1, 0.1m, 5, 0.1m, 64);
        fireMult = Number(1, 0.1m, 10, 0.1m, 64);
        aimLead = Number(0, 0, 3, 0.1m, 64);
        freeAim = new ThemedCheckBox { Text = L.T("Free aim"), AutoSize = true };
        foreach (NumberBox n in new[] { speedMult, turnMult, fireMult, aimLead }) n.ValueChanged += (_, _) => TuningChanged();
        freeAim.CheckedChanged += (_, _) => TuningChanged();

        var tip = context.ToolTip;
        tip.SetToolTip(unitBox, L.T("The game unit that carries the AI: its art, speed, turning and guns."));
        tip.SetToolTip(playerBox, L.T("How the player's plane moves in the test. With the first option, click on the map to send it somewhere."));
        tip.SetToolTip(playerVisible, L.T("Off = there is no player to look at (to test what the AI does when the player is gone)."));
        tip.SetToolTip(drain, L.T("The unit loses this percent of its health every second (to test health conditions and phases)."));
        tip.SetToolTip(speedMult, L.T("\"speedMultiplier\" of the unit type."));
        tip.SetToolTip(turnMult, L.T("\"turnMultiplier\" of the unit type."));
        tip.SetToolTip(fireMult, L.T("\"fireRateMultiplier\" of the unit type."));
        tip.SetToolTip(aimLead, L.T("\"aimLead\" of the unit type (a behavior's own value wins)."));
        tip.SetToolTip(freeAim, L.T("\"freeAim\" of the unit type: the guns turn to the player by themselves."));

        var controls = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
        controls.Controls.AddRange(new Control[]
        {
            Pair(L.T("Unit"), unitBox), armedOnly,
            playButton, restart, Pair(L.T("Speed"), speedBox),
            Pair(L.T("Player"), playerBox), Pair(L.T("speed"), playerSpeed), Pair(L.T("circle"), playerRadius), playerVisible,
            Pair(L.T("Health lost %/s"), drain), hit, heal, respawn,
            Pair(L.T("Speed x"), speedMult), Pair(L.T("Turn x"), turnMult), Pair(L.T("Fire x"), fireMult), Pair(L.T("Aim lead"), aimLead), freeAim
        });

        status = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = 20, TextAlign = ContentAlignment.MiddleLeft };
        log = new ThemedTextBox { Dock = DockStyle.Bottom, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 96, Font = new Font(FontFamily.GenericMonospace, 8.5f) };

        Controls.Add(canvas);
        Controls.Add(status);
        Controls.Add(log);
        Controls.Add(controls);

        timer.Tick += (_, _) => Tick();
        VisibleChanged += (_, _) => UpdateTimer();
        FillUnits();
        sim.PlayerSpeed = 8;
        sim.CircleRadius = 22;
        sim.SetPlayerMode(AiPlayerMode.Circle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }

    // A label and its control, kept together when the row wraps.
    private static FlowLayoutPanel Pair(string label, Control control)
    {
        return Ui.Flow(Ui.Text(label), control);
    }

    private static ComboBox Combo(string[] items, int selected, int width)
    {
        var box = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
        box.Items.AddRange(items);
        box.SelectedIndex = selected;
        return box;
    }

    private static NumberBox Number(decimal value, decimal min, decimal max, decimal step, int width)
    {
        return new NumberBox
        {
            Minimum = min, Maximum = max, Increment = step, Value = value, Width = width,
            DecimalPlaces = step < 1 ? 1 : 0, Margin = new Padding(3, 4, 3, 3)
        };
    }

    // ------------------------------------------------------------------ what is simulated

    // Starts simulating this AI (null = nothing to simulate). Called again whenever the file is edited.
    public void SetBrain(AiBrain? newBrain, string newTitle)
    {
        brain = newBrain;
        title = newTitle;
        ApplyUnit();
        sim.SetBrain(brain);
        trail.Clear();
        log.Clear();
        UpdateTimer();
        UpdateStatus();
        canvas.Invalidate();
    }

    // The game data may have changed (another game folder).
    public void ReloadUnits()
    {
        FillUnits();
    }

    private void FillUnits()
    {
        loading = true;
        try
        {
            unitBox.Items.Clear();
            IEnumerable<GameUnit> units = EditorAssets.Current.Units.Values.Where(u => !u.Wall);
            if (armedOnly.Checked) units = units.Where(u => u.Armed);
            foreach (GameUnit unit in units.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase)) unitBox.Items.Add(unit.Name);
            if (unitBox.Items.Count == 0) unitBox.Items.Add(L.T("(test plane)"));

            string wanted = lastUnitName;
            int index = wanted.Length > 0 ? unitBox.Items.IndexOf(wanted) : -1;
            if (index < 0)
            {
                // A mobile, visible unit with guns makes a good first choice: the middle one by health.
                List<GameUnit> candidates = EditorAssets.Current.Units.Values
                    .Where(u => u.Armed && !u.Wall && !u.Boss && u.VelMax >= 3 && u.VelMax <= 12 && u.Art.Length > 0 && !u.Art.StartsWith("!") &&
                                !u.Name.StartsWith("tut", StringComparison.OrdinalIgnoreCase) && !u.Name.StartsWith("ally", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(u => u.MaxHealth).ThenBy(u => u.Name, StringComparer.Ordinal).ToList();
                GameUnit? first = candidates.Count == 0 ? null : candidates[candidates.Count / 3];
                index = first == null ? 0 : Math.Max(0, unitBox.Items.IndexOf(first.Name));
            }
            unitBox.SelectedIndex = index;
        }
        finally
        {
            loading = false;
        }
        ApplyUnit();
        if (brain != null) SetBrain(brain, title);
    }

    private void UnitChosen()
    {
        if (loading) return;
        // From the list the chosen item is the answer (the text box may still show the old one); after Enter, the typed name.
        string name = unitBox.SelectedItem?.ToString() ?? unitBox.Text.Trim();
        if (!EditorAssets.Current.Units.ContainsKey(unitBox.Text.Trim()) || unitBox.DroppedDown) unitBox.Text = name;
        lastUnitName = unitBox.Text.Trim();
        ApplyUnit();
        sim.Reset();
        trail.Clear();
    }

    private void ApplyUnit()
    {
        string name = unitBox.Text.Trim();
        if (EditorAssets.Current.Units.TryGetValue(name, out GameUnit? unit))
        {
            sim.Unit = unit.ToStats();
            picture = EditorAssets.Current.UnitPicture(unit.Art);
        }
        else
        {
            sim.Unit = TestPlane();
            picture = null;
        }
        TuningChanged();
    }

    // When there is no game data (units_config.txt): a plane with one gun, so the AI can still be tried.
    private static AiUnitStats TestPlane()
    {
        var stats = new AiUnitStats { Name = "test plane", MaxHealth = 100f, VelMin = 4f, VelMax = 9f, Turn = 120f, Radius = 1.2f };
        stats.Guns.Add(new AiGunStats { Index = 0, Name = "gun0", FireRate = 3f, Burst = 3, Reload = 1.5f, Damage = 1f, Speed = 18f, Duration = 1.4f, Range = 22f, Multi = 1 });
        return stats;
    }

    private void TuningChanged()
    {
        sim.Tuning = new AiSimTuning
        {
            SpeedMult = (float)speedMult.Value,
            TurnMult = (float)turnMult.Value,
            FireRateMult = (float)fireMult.Value,
            AimLead = (float)aimLead.Value,
            FreeAim = freeAim.Checked
        };
    }

    private void PlayerModeChanged()
    {
        AiPlayerMode mode = playerBox.SelectedIndex switch { 0 => AiPlayerMode.Goal, 1 => AiPlayerMode.Circle, _ => AiPlayerMode.Still };
        sim.SetPlayerMode(mode);
        playerRadius.Enabled = mode == AiPlayerMode.Circle;
        playerSpeed.Enabled = mode != AiPlayerMode.Still;
    }

    // ------------------------------------------------------------------ running

    private void TogglePlay()
    {
        playing = !playing;
        playButton.Text = playing ? L.T("Pause") : L.T("Play");
        lastTick = clock.Elapsed.TotalSeconds;
    }

    private void Restart()
    {
        sim.Reset();
        trail.Clear();
        log.Clear();
    }

    private void UpdateTimer()
    {
        bool run = Visible && IsHandleCreated;
        if (run && !timer.Enabled)
        {
            lastTick = clock.Elapsed.TotalSeconds;
            timer.Start();
        }
        else if (!run)
        {
            timer.Stop();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateTimer();
    }

    private double SpeedFactor()
    {
        string text = speedBox.SelectedItem?.ToString() ?? "1x";
        return double.TryParse(text.TrimEnd('x'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 1.0;
    }

    private void Tick()
    {
        double now = clock.Elapsed.TotalSeconds;
        double real = Math.Min(0.1, now - lastTick);
        lastTick = now;

        if (playing && brain != null)
        {
            double remaining = real * SpeedFactor();
            while (remaining > 1e-6)
            {
                float step = (float)Math.Min(0.05, remaining);
                sim.Step(step);
                remaining -= step;
            }
            if (sim.Time - lastTrail >= 0.1)
            {
                lastTrail = sim.Time;
                if (sim.Dead) trail.Clear();
                else trail.Add(new PointF(sim.X, sim.Y));
                if (trail.Count > 160) trail.RemoveAt(0);
            }
            if (sim.Time < lastTrail) lastTrail = 0;
        }
        UpdateStatus();
        canvas.Invalidate();
    }

    private void AddLog(string line)
    {
        if (log.TextLength > 12000) log.Text = log.Text.Substring(log.TextLength - 6000);
        log.AppendText(line + "\r\n");
    }

    private void UpdateStatus()
    {
        if (brain == null)
        {
            status.Text = L.T("Select an AI or a behavior to see it act.");
            return;
        }
        float distance = AiMath.Distance(sim.X, sim.Y, sim.PlayerX, sim.PlayerY);
        status.Text = L.F("State: {0} ({1:0.0}s)   health {2:0}%   shots {3}   hits on the player {4}   distance {5:0.0}",
            sim.StateId, sim.StateTime, sim.MaxHealth > 0 ? sim.Health / sim.MaxHealth * 100f : 0f, sim.ShotsFired, sim.PlayerHits, distance);
    }

    // ------------------------------------------------------------------ canvas

    private sealed class SimCanvas : Control
    {
        private readonly AiSimView view;
        private float zoom = 7f;
        private float panX;
        private float panY;
        private bool panning;
        private bool aiming;
        private Point lastMouse;

        public SimCanvas(AiSimView view)
        {
            this.view = view;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            MinimumSize = new Size(120, 120);
        }

        private PointF ToScreen(float x, float y) => new PointF(Width / 2f + panX + x * zoom, Height / 2f + panY - y * zoom);

        private PointF ToWorld(float px, float py) => new PointF((px - Width / 2f - panX) / zoom, -(py - Height / 2f - panY) / zoom);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            lastMouse = e.Location;
            if (e.Button == MouseButtons.Left)
            {
                aiming = true;
                SetGoal(e.Location);
            }
            else
            {
                panning = true;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (panning)
            {
                panX += e.X - lastMouse.X;
                panY += e.Y - lastMouse.Y;
            }
            else if (aiming)
            {
                SetGoal(e.Location);
            }
            lastMouse = e.Location;
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            aiming = false;
            panning = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float before = zoom;
            zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.15f : 1f / 1.15f), 2f, 40f);
            // Keep the point under the cursor in place.
            float k = zoom / before;
            panX = (e.X - Width / 2f) - (e.X - Width / 2f - panX) * k;
            panY = (e.Y - Height / 2f) - (e.Y - Height / 2f - panY) * k;
            base.OnMouseWheel(e);
        }

        private void SetGoal(Point at)
        {
            PointF world = ToWorld(at.X, at.Y);
            if (view.sim.PlayerMode != AiPlayerMode.Goal) view.playerBox.SelectedIndex = 0;
            view.sim.GoalX = world.X;
            view.sim.GoalY = world.Y;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Palette p = Theme.P;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(p.Input);
            AiSim sim = view.sim;

            DrawGrid(g, p);

            // Where the player circles, and the home of the unit.
            if (sim.PlayerMode == AiPlayerMode.Circle)
            {
                PointF c = ToScreen(sim.CircleX, sim.CircleY);
                float r = sim.CircleRadius * zoom;
                using var pen = new Pen(Color.FromArgb(70, 80, 140, 220), 1f) { DashStyle = DashStyle.Dot };
                g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2, r * 2);
            }
            if (sim.PlayerMode == AiPlayerMode.Goal)
            {
                PointF goal = ToScreen(sim.GoalX, sim.GoalY);
                using var pen = new Pen(Color.FromArgb(160, 80, 140, 220), 1.5f);
                g.DrawEllipse(pen, goal.X - 6, goal.Y - 6, 12, 12);
            }
            PointF home = ToScreen(sim.HomeX, sim.HomeY);
            using (var pen = new Pen(Color.FromArgb(120, p.Muted), 1f))
            {
                g.DrawLine(pen, home.X - 5, home.Y, home.X + 5, home.Y);
                g.DrawLine(pen, home.X, home.Y - 5, home.X, home.Y + 5);
            }

            DrawTrail(g);
            DrawTarget(g, p);
            DrawBullets(g);
            DrawPlayer(g);
            DrawEnemy(g, p);
            DrawHud(g, p);
        }

        private void DrawGrid(Graphics g, Theme.Palette p)
        {
            float step = 10f * zoom;
            if (step < 12f) step *= 2f;
            Color line = Color.FromArgb(50, p.Muted);
            using var pen = new Pen(line, 1f);
            float ox = Width / 2f + panX;
            float oy = Height / 2f + panY;
            for (float x = ox - (float)Math.Ceiling(ox / step) * step; x < Width; x += step) g.DrawLine(pen, x, 0, x, Height);
            for (float y = oy - (float)Math.Ceiling(oy / step) * step; y < Height; y += step) g.DrawLine(pen, 0, y, Width, y);
            using var axis = new Pen(Color.FromArgb(110, p.Muted), 1f);
            g.DrawLine(axis, ox, 0, ox, Height);
            g.DrawLine(axis, 0, oy, Width, oy);
        }

        private void DrawTrail(Graphics g)
        {
            if (view.trail.Count < 2) return;
            PointF[] points = view.trail.Select(t => ToScreen(t.X, t.Y)).ToArray();
            using var pen = new Pen(Color.FromArgb(110, 200, 120, 60), 1.5f);
            g.DrawLines(pen, points);
        }

        // The point the AI is flying to.
        private void DrawTarget(Graphics g, Theme.Palette p)
        {
            AiOutput? o = view.sim.Last;
            if (o == null || view.sim.Dead) return;
            PointF from = ToScreen(view.sim.X, view.sim.Y);
            PointF to = ToScreen(o.TargetX, o.TargetY);
            using (var pen = new Pen(Color.FromArgb(90, 220, 170, 40), 1f) { DashStyle = DashStyle.Dash }) g.DrawLine(pen, from, to);
            using (var pen = new Pen(Color.FromArgb(200, 220, 170, 40), 1.5f))
            {
                g.DrawLine(pen, to.X - 4, to.Y - 4, to.X + 4, to.Y + 4);
                g.DrawLine(pen, to.X - 4, to.Y + 4, to.X + 4, to.Y - 4);
            }
        }

        private void DrawBullets(Graphics g)
        {
            using var fill = new SolidBrush(Color.FromArgb(230, 230, 70, 50));
            foreach (AiSimBullet b in view.sim.Bullets)
            {
                PointF at = ToScreen(b.X, b.Y);
                g.FillEllipse(fill, at.X - 2.5f, at.Y - 2.5f, 5f, 5f);
            }
        }

        private void DrawPlayer(Graphics g)
        {
            AiSim sim = view.sim;
            PointF at = ToScreen(sim.PlayerX, sim.PlayerY);
            GraphicsState state = g.Save();
            g.TranslateTransform(at.X, at.Y);
            g.RotateTransform(sim.PlayerRot);
            var arrow = new[] { new PointF(0, -11), new PointF(8, 9), new PointF(0, 4), new PointF(-8, 9) };
            int alpha = sim.PlayerVisible ? 235 : 90;
            using (var fill = new SolidBrush(Color.FromArgb(alpha, 70, 130, 230))) g.FillPolygon(fill, arrow);
            using (var pen = new Pen(Color.FromArgb(alpha, 30, 60, 140), 1.5f)) g.DrawPolygon(pen, arrow);
            g.Restore(state);
        }

        private void DrawEnemy(Graphics g, Theme.Palette p)
        {
            AiSim sim = view.sim;
            PointF at = ToScreen(sim.X, sim.Y);
            float r = Math.Max(6f, sim.Unit.Radius * zoom);

            if (sim.Dead)
            {
                using var pen = new Pen(Color.FromArgb(200, 230, 90, 40), 2f);
                g.DrawEllipse(pen, at.X - r * 1.6f, at.Y - r * 1.6f, r * 3.2f, r * 3.2f);
                g.DrawLine(pen, at.X - r, at.Y - r, at.X + r, at.Y + r);
                g.DrawLine(pen, at.X - r, at.Y + r, at.X + r, at.Y - r);
                return;
            }

            GraphicsState state = g.Save();
            g.TranslateTransform(at.X, at.Y);
            g.RotateTransform(sim.Rot);
            AssetPicture? pic = view.picture;
            if (pic != null)
            {
                float half = (float)(pic.Half * zoom);
                g.DrawImage(pic.Image, -half, -half, half * 2, half * 2);
            }
            else
            {
                using (var fill = new SolidBrush(Color.FromArgb(210, 200, 90, 60))) g.FillEllipse(fill, -r, -r, r * 2, r * 2);
                var nose = new[] { new PointF(0, -r - 5), new PointF(r * 0.55f, -r * 0.2f), new PointF(-r * 0.55f, -r * 0.2f) };
                using (var fill = new SolidBrush(Color.FromArgb(240, 250, 200, 80))) g.FillPolygon(fill, nose);
            }
            g.Restore(state);

            // Health bar.
            float width = Math.Max(26f, r * 2f);
            float fraction = sim.MaxHealth > 0f ? Math.Clamp(sim.Health / sim.MaxHealth, 0f, 1f) : 0f;
            RectangleF bar = new RectangleF(at.X - width / 2f, at.Y - r - 12f, width, 4f);
            using (var back = new SolidBrush(Color.FromArgb(160, 20, 20, 20))) g.FillRectangle(back, bar);
            using (var fill = new SolidBrush(fraction > 0.5f ? Color.LimeGreen : fraction > 0.25f ? Color.Gold : Color.OrangeRed))
                g.FillRectangle(fill, bar.X, bar.Y, bar.Width * fraction, bar.Height);
        }

        private void DrawHud(Graphics g, Theme.Palette p)
        {
            AiSim sim = view.sim;
            if (view.brain == null)
            {
                TextRenderer.DrawText(g, L.T("Select an AI or a behavior on the left."), Font, new Point(10, 10), p.Muted);
                return;
            }
            using var big = new Font(Font.FontFamily, 11f, FontStyle.Bold);
            TextRenderer.DrawText(g, view.title, big, new Point(10, 8), p.Text);
            TextRenderer.DrawText(g, L.T("State") + ": " + sim.StateId + "  (" + sim.StateTime.ToString("0.0") + "s)", Font, new Point(10, 30), p.Good);
            if (sim.Last != null && sim.Last.State.BehaviorId.Length > 0)
                TextRenderer.DrawText(g, L.T("behavior") + ": " + sim.Last.State.BehaviorId, Font, new Point(10, 48), p.Muted);
            TextRenderer.DrawText(g, L.T("Left click: send the player there    Right drag: move the view    Wheel: zoom"), Font,
                new Point(10, Height - 20), p.Muted);
        }
    }
}
