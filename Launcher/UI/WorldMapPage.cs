using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// "World map" page of the Server tab: the open world (terrain Basic) as the server built it, with every player and dungeon carrier
// on it. Hovering a player shows its plane, rank, region, position and health. The data comes from the files the server writes while
// somebody is in the world (world-map.json and world-live.json, see Core\WorldMapFiles.cs), so it works with the server started
// by the launcher or by "Run server.ps1" as long as it is the one of the launcher's server folder.
internal sealed class WorldMapPage : UserControl
{
    private readonly LauncherContext context;
    private readonly WorldMapView view = new WorldMapView { Dock = DockStyle.Fill };
    private readonly Label status;
    private readonly Label summary;
    private readonly FlowLayoutPanel legend;
    private readonly ListView players;
    private readonly ListView carriers;
    private readonly System.Windows.Forms.Timer clock;
    private WorldMapData? map;
    private string? mapError;
    private DateTime mapStamp = DateTime.MinValue;
    private string playersSignature = "";
    private string carriersSignature = "";

    public WorldMapPage(LauncherContext context)
    {
        this.context = context;

        status = Ui.Note("");
        status.Dock = DockStyle.Top;
        status.AutoSize = true;

        summary = Ui.Wrapping("", 320, gray: false);
        summary.Dock = DockStyle.Top;

        legend = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 2, 0, 6) };

        players = Ui.Grid((L.T("Player"), 80), (L.T("Plane"), 80), (L.T("Lv."), 32), (L.T("Rank"), 38), (L.T("Zone"), 130));
        players.Dock = DockStyle.Fill;
        players.MultiSelect = false;
        players.SelectedIndexChanged += (_, _) =>
        {
            view.SelectedPlayer = players.SelectedItems.Count > 0 ? players.SelectedItems[0].Text : null;
            view.Invalidate();
        };
        view.PlayerClicked += name =>
        {
            foreach (ListViewItem item in players.Items) item.Selected = item.Text == name;
        };

        carriers = Ui.Grid((L.T("Dungeon carrier"), 150), (L.T("Position"), 90), (L.T("Leaves in"), 70));
        carriers.Dock = DockStyle.Bottom;
        carriers.Height = 90;

        Label playersTitle = Ui.Bold(L.T("Players in the open world"));
        playersTitle.Dock = DockStyle.Top;
        Label legendTitle = Ui.Bold(L.T("Regions"));
        legendTitle.Dock = DockStyle.Top;
        Label carriersTitle = Ui.Bold(L.T("Dungeon carriers"));
        carriersTitle.Dock = DockStyle.Bottom;

        // Fill first; the Top and Bottom controls added later sit around it.
        var side = new Panel { Dock = DockStyle.Right, Width = 380, Padding = new Padding(8, 0, 0, 0) };
        side.Controls.Add(players);
        side.Controls.Add(playersTitle);
        side.Controls.Add(legend);
        side.Controls.Add(legendTitle);
        side.Controls.Add(summary);
        side.Controls.Add(carriersTitle);
        side.Controls.Add(carriers);

        Controls.Add(view);
        Controls.Add(side);
        Controls.Add(status);

        clock = new System.Windows.Forms.Timer { Interval = 1000 };
        clock.Tick += (_, _) => RefreshData();
        clock.Start();
        RefreshData();
    }

    private void RefreshData()
    {
        // The picture changes only when the server builds a new world.
        DateTime stamp = WorldMapData.Stamp();
        if (stamp != mapStamp)
        {
            mapStamp = stamp;
            map = WorldMapData.Load(out mapError);
            view.SetMap(map);
            BuildLegend();
        }
        if (map == null)
        {
            view.Message = (mapError ?? L.T("The server has not built the open world yet.")) + "\n\n" +
                           L.T("The server builds the open world when it starts (or when the first player enters it, if \"Build the world before players come in\" is off).");
            view.SetLive(null);
            status.Text = L.T("No open world yet.");
            SetSummary(null);
            ShowPlayers(null);
            ShowCarriers(null);
            view.Invalidate();
            return;
        }

        WorldLiveData? live = WorldLiveData.Load();
        TimeSpan? age = live?.Age;
        bool serverUp = context.Server.State == ServerState.Running || NetworkCheck.AnyServerListening();
        bool fresh = serverUp && live != null && live.Active && age.HasValue && age.Value.TotalSeconds < 8;
        if (!fresh)
        {
            if (!serverUp)
                status.Text = L.F("The server is not running. The map is the last world it built (seed {0}).", map.Seed);
            else if (live != null && live.Active)
                status.Text = L.T("The server stopped updating the open world (is it still running?). The map is the last one built.");
            else if (live != null && live.Standby)
                status.Text = L.F("The open world is ready (seed {0}) and waits for the first player.", map.Seed);
            else
                status.Text = L.F("Nobody is in the open world right now. The map is the last one built (seed {0}).", map.Seed);
            live = null;
        }
        else
        {
            status.Text = L.F("{0}: {1} player(s) in the open world.", live!.Name, live.Players.Count);
        }
        view.SetLive(live);
        SetSummary(live);
        ShowPlayers(live);
        ShowCarriers(live);
    }

    private void SetSummary(WorldLiveData? live)
    {
        string text;
        if (map == null) text = "";
        else if (live == null)
            text = L.F("Seed {0}  ·  radius {1} units  ·  {2} x {2} cells", map.Seed, map.Radius.ToString("0"), map.Cells);
        else
            text = L.F("Seed {0}  ·  radius {1} units  ·  open for {2}\nEnemies alive: {3}  ·  spawn sites with enemies: {4} of {5}  ·  sites cleared: {6}",
                live.Seed, live.Radius.ToString("0"), WorldMapView.Duration(live.UptimeSeconds), live.EnemiesAlive, live.ActiveSites, live.Sites, live.SitesCleared);
        if (summary.Text != text) summary.Text = text;
    }

    private void BuildLegend()
    {
        legend.SuspendLayout();
        foreach (Control control in legend.Controls.Cast<Control>().ToList())
        {
            legend.Controls.Remove(control);
            control.Dispose();
        }
        if (map != null)
        {
            foreach (WorldMapData.LegendEntry entry in map.Legend)
            {
                legend.Controls.Add(new LegendItem(entry.Zone.Length > 0 ? entry.Zone : entry.Name, WorldMapView.ColorOf(entry)));
            }
        }
        legend.ResumeLayout();
    }

    // One entry of the legend: a square in the color of the zone on the map, then its name, drawn as one piece so the pair never
    // splits across two lines and the theme cannot repaint the square.
    private sealed class LegendItem : Control
    {
        private const int Swatch = 14;
        private const int Gap = 6;
        private readonly Color color;

        public LegendItem(string text, Color color)
        {
            this.color = color;
            Text = text;
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Margin = new Padding(3, 2, 8, 2);
            Size textSize = TextRenderer.MeasureText(text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            Size = new Size(Swatch + Gap + textSize.Width + 2, Math.Max(Swatch, textSize.Height) + 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Palette palette = Theme.P;
            e.Graphics.Clear(palette.Back);
            int top = (Height - Swatch) / 2;
            using (var fill = new SolidBrush(color)) e.Graphics.FillRectangle(fill, 0, top, Swatch, Swatch);
            using (var border = new Pen(palette.Border)) e.Graphics.DrawRectangle(border, 0, top, Swatch - 1, Swatch - 1);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Swatch + Gap, 0, Width - Swatch - Gap, Height), palette.Text,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    private void ShowPlayers(WorldLiveData? live)
    {
        List<WorldLiveData.PlayerInfo> list = live?.Players ?? new List<WorldLiveData.PlayerInfo>();
        string signature = string.Join("|", list.Select(p => p.Name + "," + p.PlaneClass + "," + p.PlaneLevel + "," + p.PilotRank + "," + p.Region));
        if (signature == playersSignature) return;
        playersSignature = signature;
        string? selected = view.SelectedPlayer;
        players.BeginUpdate();
        players.Items.Clear();
        foreach (WorldLiveData.PlayerInfo p in list)
        {
            var item = new ListViewItem(p.Name);
            item.SubItems.Add(p.PlaneClass.Length > 0 ? p.PlaneClass : "-");
            item.SubItems.Add(p.PlaneLevel?.ToString() ?? "-");
            item.SubItems.Add(p.PilotRank?.ToString() ?? "-");
            item.SubItems.Add(p.Region.Length > 0 ? p.Region : "-");
            item.ForeColor = WorldMapView.ColorOfPlayer(p.Name);
            item.Selected = p.Name == selected;
            players.Items.Add(item);
        }
        players.EndUpdate();
    }

    private void ShowCarriers(WorldLiveData? live)
    {
        List<WorldLiveData.CarrierInfo> list = live?.Carriers ?? new List<WorldLiveData.CarrierInfo>();
        string signature = string.Join("|", list.Select(c => c.Label + "," + c.SecondsLeft / 5));
        if (signature == carriersSignature) return;
        carriersSignature = signature;
        carriers.BeginUpdate();
        carriers.Items.Clear();
        foreach (WorldLiveData.CarrierInfo c in list)
        {
            var item = new ListViewItem(c.Label);
            item.SubItems.Add(c.X.ToString("0") + ", " + c.Y.ToString("0"));
            item.SubItems.Add(WorldMapView.Duration(c.SecondsLeft));
            carriers.Items.Add(item);
        }
        carriers.EndUpdate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            clock.Stop();
            clock.Dispose();
        }
        base.Dispose(disposing);
    }
}
