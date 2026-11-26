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
    private readonly LauncherContext context;
    private readonly AiSim sim = new AiSim();
    private readonly SimCanvas canvas;
    private readonly Button playButton;
    private readonly ComboBox speedBox;
    private readonly ComboBox playerBox;
    private readonly NumberBox playerSpeed;
    private readonly NumberBox playerRadius;
    private readonly CheckBox playerVisible;
    private readonly ComboBox playerSpriteBox;
    private AssetPicture? playerPicture;
    private readonly NumberBox drain;
    private readonly ComboBox leaderBox;
    private readonly NumberBox clickDamage;
    private readonly CheckBox respawn;
    private readonly NumberBox speedMult;
    private readonly NumberBox turnMult;
    private readonly NumberBox fireMult;
    private readonly NumberBox aimLead;
    private readonly CheckBox freeAim;
    private readonly CheckBox showExits;
    private readonly CheckBox showRanges;
    private readonly Label status;
    private readonly TextBox log;
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 33 };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<PointF> trail = new List<PointF>();
    private string lastReportedState = "";
    private double lastTick;
    private double lastTrail;
    private bool playing = true;
    private AiBrain? brain;
    private string title = "";
    private string unitName = "";
    private string builtinFlight = "chase";
    private string unitCaption = "";
    private string aiCaption = "";
    private bool temporary;
    private AssetPicture? picture;

    public AiSimView(LauncherContext context)
    {
        this.context = context;
        Dock = DockStyle.Fill;
        sim.EventAdded += AddLog;

        canvas = new SimCanvas(this) { Dock = DockStyle.Fill };

        playButton = Ui.Button(L.T("Pause"), (_, _) => TogglePlay());
        Button restart = Ui.Button(L.T("Restart"), (_, _) => Restart());
        speedBox = Combo(new[] { "0.25x", "0.5x", "1x", "2x", "4x" }, 2, 64);
        playerBox = Combo(new[] { L.T("Follows my clicks"), L.T("Flies in a circle"), L.T("Stands still") }, 1, 150);
        playerBox.SelectedIndexChanged += (_, _) => PlayerModeChanged();
        playerSpeed = Number(8, 0, 60, 1, 64);
        playerSpeed.ValueChanged += (_, _) => sim.PlayerSpeed = (float)playerSpeed.Value;
        playerRadius = Number(22, 4, 120, 2, 64);
        playerRadius.ValueChanged += (_, _) => sim.CircleRadius = (float)playerRadius.Value;
        playerSpriteBox = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        playerSpriteBox.SelectedIndexChanged += (_, _) => PlayerSpriteChanged();
        FillPlayerSprites();
        playerVisible = new ThemedCheckBox { Text = L.T("Player in sight"), AutoSize = true, Checked = true };
        playerVisible.CheckedChanged += (_, _) => sim.PlayerVisible = playerVisible.Checked;

        drain = Number(0, 0, 100, 1, 64);
        drain.ValueChanged += (_, _) => sim.HealthDrainPerSecond = (float)drain.Value / 100f;
        Button hit = Ui.Button(L.T("Hit -25%"), (_, _) => sim.Damage(0.25f));
        Button heal = Ui.Button(L.T("Heal"), (_, _) => sim.Damage(-1f));
        Button killMinions = Ui.Button(L.T("Destroy summons"), (_, _) => sim.KillMinions());
        leaderBox = Combo(new[] { L.T("Runs its AI"), L.T("Goes where I Ctrl+click"), L.T("Stands still"), L.T("Taken away") }, 0, 170);
        leaderBox.Enabled = false;
        leaderBox.SelectedIndexChanged += (_, _) => ApplyLeaderMode();
        clickDamage = Number(25, 1, 100, 5, 64);
        respawn = new ThemedCheckBox { Text = L.T("Comes back after dying"), AutoSize = true, Checked = true };
        respawn.CheckedChanged += (_, _) => sim.AutoRespawn = respawn.Checked;

        speedMult = Number(1, 0.1m, 5, 0.1m, 64);
        turnMult = Number(1, 0.1m, 5, 0.1m, 64);
        fireMult = Number(1, 0.1m, 10, 0.1m, 64);
        aimLead = Number(0, 0, 3, 0.1m, 64);
        freeAim = new ThemedCheckBox { Text = L.T("Free aim"), AutoSize = true };
        showExits = new ThemedCheckBox { Text = L.T("Show ways out of the state"), AutoSize = true, Checked = true };
        showRanges = new ThemedCheckBox { Text = L.T("Show weapon range"), AutoSize = true };
        showExits.CheckedChanged += (_, _) => canvas.Invalidate();
        showRanges.CheckedChanged += (_, _) => canvas.Invalidate();
        foreach (NumberBox n in new[] { speedMult, turnMult, fireMult, aimLead }) n.ValueChanged += (_, _) => TuningChanged();
        freeAim.CheckedChanged += (_, _) => TuningChanged();

        var tip = context.ToolTip;
        tip.SetToolTip(playerBox, L.T("How the player's plane moves in the test. With the first option, click on the map to send it somewhere."));
        tip.SetToolTip(playerSpriteBox, L.T("The picture of the player's plane in the test (a 2D picture of a plane of the game; it only changes how it looks)."));
        tip.SetToolTip(playerVisible, L.T("Off = there is no player to look at (to test what the AI does when the player is gone)."));
        tip.SetToolTip(drain, L.T("The unit loses this percent of its health every second (to test health conditions and phases)."));
        tip.SetToolTip(killMinions, L.T("Destroys the units the AI summoned. They are real units here: they fly, shoot and can be hit with a right click."));
        tip.SetToolTip(leaderBox, L.T("The leader of the group the unit belongs to in the open world (a unit that has one shows it here, running its own AI). \"Goes where I Ctrl+click\" flies it to the point you Ctrl+click; \"Stands still\" parks it (still shooting); \"Taken away\" removes it."));
        tip.SetToolTip(clickDamage, L.T("How much health a right click takes from the unit under the mouse (percent of its maximum)."));
        tip.SetToolTip(speedMult, L.T("\"speedMultiplier\" of the unit type."));
        tip.SetToolTip(turnMult, L.T("\"turnMultiplier\" of the unit type."));
        tip.SetToolTip(fireMult, L.T("\"fireRateMultiplier\" of the unit type."));
        tip.SetToolTip(aimLead, L.T("\"aimLead\" of the unit type (a behavior's own value wins)."));
        tip.SetToolTip(freeAim, L.T("\"freeAim\" of the unit type: the guns turn to the player by themselves."));
        tip.SetToolTip(showExits, L.T("Draws what makes the AI leave the current state: a circle for a distance, a mark on the health bar, a clock for a time, a cone for facing the player. Each transition has its own color, and a list at the bottom has all of them (a condition that is true now is marked)."));
        tip.SetToolTip(showRanges, L.T("Draws how far each gun that the current state lets fire can shoot, and the area where the player has to be for it to shoot (the gun's arc). One color per gun, the same as its shots."));

        var controls = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
        controls.Controls.AddRange(new Control[]
        {
            playButton, restart, Pair(L.T("Speed"), speedBox),
            Pair(L.T("Player"), playerBox), Pair(L.T("Plane"), playerSpriteBox), Pair(L.T("speed"), playerSpeed), Pair(L.T("circle"), playerRadius), playerVisible,
            Pair(L.T("Health lost %/s"), drain), hit, heal, killMinions, Pair(L.T("Right click takes %"), clickDamage), Pair(L.T("Leader"), leaderBox), respawn,
            Pair(L.T("Speed x"), speedMult), Pair(L.T("Turn x"), turnMult), Pair(L.T("Fire x"), fireMult), Pair(L.T("Aim lead"), aimLead), freeAim, showExits, showRanges
        });

        status = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = 20, TextAlign = ContentAlignment.MiddleLeft };
        log = new ThemedTextBox { Dock = DockStyle.Bottom, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 96, Font = new Font(FontFamily.GenericMonospace, 8.5f) };

        Controls.Add(canvas);
        Controls.Add(status);
        Controls.Add(log);
        Controls.Add(controls);

        timer.Tick += (_, _) => Tick();
        VisibleChanged += (_, _) => UpdateTimer();
        ApplyUnit();
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

    private void BindFormation(AiBrain? next)
    {
        sim.Formation = null;
        if (next == null || string.IsNullOrWhiteSpace(next.Formation)) return;
        try
        {
            FormationLibrary library = FormationLibrary.Load(LauncherPaths.AiFolder);
            sim.Formation = library.Get(next.Formation);
        }
        catch (IOException)
        {
            sim.Formation = null;
        }
    }

    // Starts simulating this AI (null = nothing to simulate). Called again whenever the file is edited.
    public void SetBrain(AiBrain? newBrain, string newTitle)
    {
        brain = newBrain;
        title = newTitle;
        ApplyUnit();
        BindFormation(brain);
        sim.SetBrain(brain);
        trail.Clear();
        log.Clear();
        UpdateTimer();
        UpdateStatus();
        canvas.Invalidate();
    }

    // The AI was edited: the simulation goes on with the new version (only the Restart button starts it again).
    public void ReplaceBrain(AiBrain newBrain, string newTitle)
    {
        brain = newBrain;
        title = newTitle;
        string before = sim.Formation?.Id ?? "";
        BindFormation(brain);
        sim.ReplaceBrain(newBrain);
        if (!string.Equals(before, sim.Formation?.Id ?? "", StringComparison.OrdinalIgnoreCase)) sim.RespawnFormation();
        UpdateStatus();
        canvas.Invalidate();
    }

    // What the tab chose: the game unit that is flown and drawn, and what the enemy type says about it (speed, aim...).
    // A plain game unit has no type, so its settings are the neutral ones.
    public void SetUnit(AiUnitChoice? choice, AiSimSetup? leader = null)
    {
        sim.LeaderSetup = leader;
        leaderBox.Enabled = leader != null;
        if (leaderBox.SelectedIndex != 0) leaderBox.SelectedIndex = 0;
        sim.SetLeaderEnabled(true);
        unitName = choice?.UnitName ?? "";
        builtinFlight = string.IsNullOrWhiteSpace(choice?.BuiltInBehavior) ? "chase" : choice!.BuiltInBehavior.Trim().ToLowerInvariant();
        unitCaption = choice == null ? "" : choice.IsType ? choice.TypeName + " (" + (unitName.Length > 0 ? unitName : L.T("test plane")) + ")" : unitName.Length > 0 ? unitName : L.T("test plane");
        speedMult.Value = Clamp(speedMult, choice?.SpeedMult ?? 1f);
        turnMult.Value = Clamp(turnMult, choice?.TurnMult ?? 1f);
        fireMult.Value = Clamp(fireMult, choice?.FireRateMult ?? 1f);
        aimLead.Value = Clamp(aimLead, choice?.AimLead ?? 0f);
        freeAim.Checked = choice?.FreeAim ?? false;
        ApplyUnit();
        sim.Reset();
        trail.Clear();
        log.Clear();
        canvas.Invalidate();
    }

    // How the AI's summons are made (by the name it gives).
    public Func<string, AiSimSetup?>? SummonResolver
    {
        get => sim.SummonResolver;
        set => sim.SummonResolver = value;
    }

    // The leader box follows the state of the arena: it only works when the unit has a leader.
    private void ApplyLeaderMode()
    {
        int mode = leaderBox.SelectedIndex;
        sim.SetLeaderEnabled(mode != 3);
        AiSimUnit? leader = sim.Leader;
        if (leader == null) return;
        leader.Control = mode == 1 ? AiSimControl.Goal : mode == 2 ? AiSimControl.Still : AiSimControl.Ai;
        leader.GoalX = leader.X;
        leader.GoalY = leader.Y;
    }

    // The weapons of the unit in the simulator (what the weapons checklist of the editors shows).
    public IReadOnlyList<AiGunStats> UnitGuns => sim.Unit.Guns;

    // What the HUD says: which AI the unit runs, and whether it is the unit's own or one loaded only to be edited.
    public void SetCaptions(string ai, bool loadedForEditing)
    {
        aiCaption = ai;
        temporary = loadedForEditing;
        canvas.Invalidate();
    }

    // Planes of the game the player can be drawn as: the friendly ones first (the pictures come from the game files; the player's own
    // classes have none), then a few fighters. "Arrow" is the plain triangle used when there is no picture.
    private static readonly string[] PlayerSprites =
    {
        "allybird", "allygooseprefab", "enemystarfighter", "enemyfighter", "enemyhunter", "enemyracehunter", "enemylasership", "enemytiewingman"
    };

    private void FillPlayerSprites()
    {
        string keep = playerSpriteBox.SelectedItem?.ToString() ?? "";
        playerSpriteBox.Items.Clear();
        foreach (string art in PlayerSprites)
        {
            if (EditorAssets.Current.UnitPicture(art) != null) playerSpriteBox.Items.Add(art);
        }
        playerSpriteBox.Items.Add(L.T("Arrow"));
        int index = keep.Length > 0 ? playerSpriteBox.Items.IndexOf(keep) : 0;
        playerSpriteBox.SelectedIndex = Math.Max(0, index);
    }

    private void PlayerSpriteChanged()
    {
        string chosen = playerSpriteBox.SelectedItem?.ToString() ?? "";
        playerPicture = chosen.Length > 0 && chosen != L.T("Arrow") ? EditorAssets.Current.UnitPicture(chosen) : null;
        canvas?.Invalidate();
    }

    private static decimal Clamp(NumberBox box, float value) => Math.Clamp((decimal)value, box.Minimum, box.Maximum);

    private void ApplyUnit()
    {
        if (unitName.Length > 0 && EditorAssets.Current.Units.TryGetValue(unitName, out GameUnit? unit))
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
    private static AiUnitStats TestPlane() => AiSimSetups.TestPlane();

    private void TuningChanged()
    {
        sim.Tuning = new AiSimTuning
        {
            SpeedMult = (float)speedMult.Value,
            TurnMult = (float)turnMult.Value,
            FireRateMult = (float)fireMult.Value,
            AimLead = (float)aimLead.Value,
            FreeAim = freeAim.Checked,
            Builtin = builtinFlight
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
        ApplyLeaderMode();
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

    // The state the AI is in now ("" when there is no AI), for the graph view.
    public event Action<string>? RunningStateChanged;

    private void UpdateStatus()
    {
        string running = brain == null ? "" : sim.StateId;
        if (running != lastReportedState)
        {
            lastReportedState = running;
            RunningStateChanged?.Invoke(running);
        }
        if (brain == null)
        {
            status.Text = L.T("Select an AI or a behavior to see it act.");
            return;
        }
        float distance = AiMath.Distance(sim.X, sim.Y, sim.PlayerX, sim.PlayerY);
        status.Text = L.F("State: {0} ({1:0.0}s)   health {2:0}%   shots {3}   hits on the player {4}   distance {5:0.0}",
            sim.StateId, sim.StateTime, sim.MaxHealth > 0 ? sim.Health / sim.MaxHealth * 100f : 0f, sim.ShotsFired, sim.PlayerHits, distance) +
            (sim.TotalSummoned > 0 ? "   " + L.F("summons alive {0}/{1}", sim.MinionsAlive, sim.TotalSummoned) : "") +
            (sim.Shielded ? "   " + L.T("SHIELD") : "");
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
        private bool aimingLeader;
        private bool rightMoved;
        private Point rightDown;
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
            if (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Control) != 0 && view.sim.Leader != null)
            {
                aimingLeader = true;
                SetLeaderGoal(e.Location);
            }
            else if (e.Button == MouseButtons.Left)
            {
                aiming = true;
                SetGoal(e.Location);
            }
            else
            {
                panning = true;
                rightMoved = false;
                rightDown = e.Location;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (panning)
            {
                // A right click that hardly moves is a hit on the unit under the mouse; a drag moves the view.
                if (Math.Abs(e.X - rightDown.X) + Math.Abs(e.Y - rightDown.Y) > 4) rightMoved = true;
                if (rightMoved)
                {
                    panX += e.X - lastMouse.X;
                    panY += e.Y - lastMouse.Y;
                }
            }
            else if (aimingLeader)
            {
                SetLeaderGoal(e.Location);
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
            if (panning && e.Button != MouseButtons.Left && !rightMoved) HitUnitAt(e.Location);
            aiming = false;
            aimingLeader = false;
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

        // The leader flies to the point (the box says so).
        private void SetLeaderGoal(Point at)
        {
            AiSimUnit? leader = view.sim.Leader;
            if (leader == null) return;
            if (view.leaderBox.SelectedIndex != 1) view.leaderBox.SelectedIndex = 1;
            leader = view.sim.Leader;
            if (leader == null) return;
            PointF world = ToWorld(at.X, at.Y);
            leader.GoalX = world.X;
            leader.GoalY = world.Y;
        }

        // Right click on a unit of the arena: it loses the percent of the box (a shield ignores it, like the server's).
        private void HitUnitAt(Point at)
        {
            AiSim sim = view.sim;
            PointF world = ToWorld(at.X, at.Y);
            AiSimUnit? best = null;
            float bestDistance = float.MaxValue;
            foreach (AiSimUnit u in sim.Units)
            {
                if (u.Dead) continue;
                float reach = Math.Max(1.2f, u.Unit.Radius) + 4f / zoom;
                float d = AiMath.Distance(u.X, u.Y, world.X, world.Y);
                if (d <= reach && d < bestDistance)
                {
                    best = u;
                    bestDistance = d;
                }
            }
            if (best == null) return;
            best.Damage((float)view.clickDamage.Value / 100f);
            Invalidate();
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

            if (view.showRanges.Checked && view.brain != null) AiSimOverlay.DrawWeapons(g, sim, ToScreen(sim.X, sim.Y), zoom, Font, GunColor, Size);

            DrawTrail(g);
            DrawTarget(g, p);
            DrawBullets(g);
            DrawPlayer(g);
            DrawUnits(g, p);
            if (view.showExits.Checked && view.brain != null)
            {
                float unitRadius = Math.Max(6f, sim.Unit.Radius * zoom);
                AiSimOverlay.DrawConditions(g, sim, ToScreen(sim.X, sim.Y), unitRadius, zoom, Font, p, Size);
            }
            DrawHud(g, p);
            if (view.brain != null)
            {
                int bottom = Height - 24;
                if (view.showExits.Checked) AiSimOverlay.DrawLegend(g, sim, Font, p, Size, bottom);
            }
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

        private static readonly Color[] GunColors =
        {
            Color.FromArgb(230, 70, 50), Color.FromArgb(240, 160, 30), Color.FromArgb(170, 70, 220), Color.FromArgb(30, 190, 190),
            Color.FromArgb(230, 60, 160), Color.FromArgb(120, 190, 40)
        };

        // Each gun of the unit has its own color: the shots and the gun list of the HUD use it.
        private static Color GunColor(int gun, int alpha)
        {
            Color c = GunColors[Math.Abs(gun) % GunColors.Length];
            return Color.FromArgb(alpha, c);
        }

        private void DrawBullets(Graphics g)
        {
            foreach (AiSimBullet b in view.sim.Bullets)
            {
                PointF at = ToScreen(b.X, b.Y);
                using var fill = new SolidBrush(GunColor(b.Gun, 235));
                g.FillEllipse(fill, at.X - 3f, at.Y - 3f, 6f, 6f);
            }
        }

        private void DrawPlayer(Graphics g)
        {
            AiSim sim = view.sim;
            PointF at = ToScreen(sim.PlayerX, sim.PlayerY);
            GraphicsState state = g.Save();
            g.TranslateTransform(at.X, at.Y);
            g.RotateTransform(sim.PlayerRot);
            AssetPicture? plane = view.playerPicture;
            if (plane != null)
            {
                // A 2D picture of a plane of the game, nose up like the unit's: the same turn as the arrow.
                float half = Math.Max(10f, 2.4f * zoom);
                using var attributes = new System.Drawing.Imaging.ImageAttributes();
                attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = sim.PlayerVisible ? 1f : 0.35f });
                g.DrawImage(plane.Image, new[] { new PointF(-half, -half), new PointF(half, -half), new PointF(-half, half) },
                    new RectangleF(0, 0, plane.Image.Width, plane.Image.Height), GraphicsUnit.Pixel, attributes);
                g.Restore(state);
                return;
            }
            var arrow = new[] { new PointF(0, -11), new PointF(8, 9), new PointF(0, 4), new PointF(-8, 9) };
            int alpha = sim.PlayerVisible ? 235 : 90;
            using (var fill = new SolidBrush(Color.FromArgb(alpha, 70, 130, 230))) g.FillPolygon(fill, arrow);
            using (var pen = new Pen(Color.FromArgb(alpha, 30, 60, 140), 1.5f)) g.DrawPolygon(pen, arrow);
            g.Restore(state);
        }

        // The picture of a unit: the edited one has its own; the others come from the game's art.
        private AssetPicture? PictureOf(AiSimUnit u)
        {
            if (u.IsMain) return view.picture;
            return u.Unit.Art.Length > 0 && EditorAssets.Current.HasPictures ? EditorAssets.Current.UnitPicture(u.Unit.Art) : null;
        }

        // The leader and the summoned units first, the edited unit on top.
        private void DrawUnits(Graphics g, Theme.Palette p)
        {
            foreach (AiSimUnit u in view.sim.Units.Where(x => !x.IsMain).ToList()) DrawUnit(g, p, u);
            DrawUnit(g, p, view.sim.Main);
        }

        private void DrawUnit(Graphics g, Theme.Palette p, AiSimUnit u)
        {
            PointF at = ToScreen(u.X, u.Y);
            float r = Math.Max(6f, u.Unit.Radius * zoom);

            if (u.Dead)
            {
                using var pen = new Pen(Color.FromArgb(200, 230, 90, 40), 2f);
                g.DrawEllipse(pen, at.X - r * 1.6f, at.Y - r * 1.6f, r * 3.2f, r * 3.2f);
                g.DrawLine(pen, at.X - r, at.Y - r, at.X + r, at.Y + r);
                g.DrawLine(pen, at.X - r, at.Y + r, at.X + r, at.Y - r);
                return;
            }

            GraphicsState state = g.Save();
            g.TranslateTransform(at.X, at.Y);
            g.RotateTransform(u.Rot);
            AssetPicture? pic = PictureOf(u);
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

            if (u.Shielded)
            {
                using var ring = new Pen(Color.FromArgb(200, 90, 200, 255), 2.5f);
                using var glow = new SolidBrush(Color.FromArgb(45, 90, 200, 255));
                g.FillEllipse(glow, at.X - r * 1.7f, at.Y - r * 1.7f, r * 3.4f, r * 3.4f);
                g.DrawEllipse(ring, at.X - r * 1.7f, at.Y - r * 1.7f, r * 3.4f, r * 3.4f);
            }

            // Health bar.
            bool detailed = u.IsMain && view.showExits.Checked;
            float width = Math.Max(detailed ? 64f : 26f, r * 2f);
            float fraction = u.MaxHealth > 0f ? Math.Clamp(u.Health / u.MaxHealth, 0f, 1f) : 0f;
            RectangleF bar = new RectangleF(at.X - width / 2f, at.Y - r - 12f, width, 4f);
            using (var back = new SolidBrush(Color.FromArgb(160, 20, 20, 20))) g.FillRectangle(back, bar);
            using (var fill = new SolidBrush(fraction > 0.5f ? Color.LimeGreen : fraction > 0.25f ? Color.Gold : Color.OrangeRed))
                g.FillRectangle(fill, bar.X, bar.Y, bar.Width * fraction, bar.Height);
            if (detailed && view.brain != null) AiSimOverlay.DrawHealthMarks(g, view.sim, bar, Font);

            // The others say who they are: the leader and the summoned units.
            if (!u.IsMain)
            {
                string name = u.Role == AiSimUnit.RoleLeader ? L.T("leader") + " " + u.Label : u.Label;
                using var small = new Font(Font.FontFamily, 7.5f);
                Size size = TextRenderer.MeasureText(name, small);
                TextRenderer.DrawText(g, name, small, new Point((int)(at.X - size.Width / 2f), (int)(bar.Y - size.Height - 1)), p.Muted);
            }
        }

        private void DrawHud(Graphics g, Theme.Palette p)
        {
            AiSim sim = view.sim;
            if (view.brain == null)
            {
                TextRenderer.DrawText(g, L.T("This unit has no AI file. Load one from the list on the left to see it act."), Font, new Point(10, 10), p.Muted);
                return;
            }
            using var big = new Font(Font.FontFamily, 11f, FontStyle.Bold);
            int y = 8;
            TextRenderer.DrawText(g, L.T("Unit") + ": " + view.unitCaption, big, new Point(10, y), p.Text);
            y += 22;
            TextRenderer.DrawText(g, (view.temporary ? L.T("AI loaded to test") : L.T("AI of the unit")) + ": " + view.aiCaption, Font, new Point(10, y),
                view.temporary ? p.Warn : p.Good);
            y += 18;
            TextRenderer.DrawText(g, L.T("State") + ": " + sim.StateId + "  (" + sim.StateTime.ToString("0.0") + "s)", Font, new Point(10, y), p.Good);
            y += 18;
            if (sim.Last != null && sim.Last.State.BehaviorId.Length > 0)
            {
                TextRenderer.DrawText(g, L.T("behavior") + ": " + sim.Last.State.BehaviorId, Font, new Point(10, y), p.Muted);
                y += 18;
            }
            if (sim.Shielded || sim.TotalSummoned > 0 || sim.VisualEffect.Length > 0 || sim.Homing)
            {
                string extra = (sim.Shielded ? L.T("shield up") + "   " : "") +
                    (sim.TotalSummoned > 0 ? L.F("summons alive {0}/{1}", sim.MinionsAlive, sim.TotalSummoned) + "   " : "") +
                    (sim.VisualEffect.Length > 0 ? L.T("effect") + ": " + sim.VisualEffect + "   " : "") +
                    (sim.Homing ? L.T("homing bullets") : "");
                TextRenderer.DrawText(g, extra.TrimEnd(), Font, new Point(10, y), p.Warn);
                y += 18;
            }
            AiSimUnit? leader = sim.Leader;
            if (leader != null)
            {
                string how = leader.Control == AiSimControl.Goal ? L.T("goes where you Ctrl+click") : leader.Control == AiSimControl.Still ? L.T("stands still") : L.T("runs its AI");
                TextRenderer.DrawText(g, L.T("Leader") + ": " + leader.Label + " (" + (leader.Dead ? L.T("destroyed") : leader.StateId + ", " + L.F("health {0:0}%", leader.MaxHealth > 0 ? leader.Health / leader.MaxHealth * 100f : 0f)) + ", " + how + ")",
                    Font, new Point(10, y), p.Muted);
                y += 18;
            }
            DrawGunList(g, p, y + 2);
            TextRenderer.DrawText(g, L.T("Left click: send the player there    Ctrl+click: send the leader    Right click on a unit: hit it    Right drag: move the view    Wheel: zoom"), Font,
                new Point(10, Height - 20), p.Muted);
        }

        // The guns of the unit: color = the color of its shots; dimmed when the state does not let it fire right now.
        private void DrawGunList(Graphics g, Theme.Palette p, int y)
        {
            AiOutput? o = view.sim.Last;
            foreach (AiGunStats gun in view.sim.Unit.Guns.Where(x => x.Damage > 0f))
            {
                bool allowed = o != null && o.FireEnabled && (o.Guns.Length == 0 || Array.IndexOf(o.Guns, gun.Index) >= 0);
                using (var dot = new SolidBrush(GunColor(gun.Index, allowed ? 255 : 90))) g.FillEllipse(dot, 12, y + 4, 9, 9);
                string name = gun.Name.Length > 0 ? gun.Name : "gun" + gun.Index;
                string reach = allowed && view.showRanges.Checked ? "   -   " + AiSimOverlay.GunSummary(view.sim, gun) : "";
                TextRenderer.DrawText(g, "#" + gun.Index + " " + name + reach + (allowed ? "" : "  (" + L.T("off in this state") + ")"), Font, new Point(26, y), allowed ? p.Text : p.Muted);
                y += 16;
            }
        }
    }
}
