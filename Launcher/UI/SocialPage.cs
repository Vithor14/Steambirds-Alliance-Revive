using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// "Friends and flocks" tab of the Server tab: the flocks of this host with their members (flocks.json, read only: the players manage
// them in the game's Flock Hall) and the settings of the social features (social.json: friends, flocks, teleport to players).
internal sealed class SocialPage : UserControl
{
    private readonly ListView flocks;
    private readonly ListView members;
    private readonly Label summary;
    private readonly System.Windows.Forms.Timer clock;
    private DateTime stamp = DateTime.MinValue;
    private List<FlockData> data = new List<FlockData>();

    public SocialPage(LauncherContext context)
    {
        ToolTip tip = context.ToolTip;

        summary = Ui.Note("");
        summary.Dock = DockStyle.Top;
        summary.AutoSize = true;

        flocks = Ui.Grid((L.T("Flock"), 130), (L.T("Founder"), 110), (L.T("Members"), 65), (L.T("Waiting"), 60), (L.T("Joining"), 100), (L.T("Created"), 120));
        flocks.Dock = DockStyle.Fill;
        flocks.SelectedIndexChanged += (_, _) => ShowMembers();

        members = Ui.Grid((L.T("Player"), 130), (L.T("Role"), 90), (L.T("Status"), 90), (L.T("Joined"), 120), (L.T("Last seen"), 120));
        members.Dock = DockStyle.Fill;

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 160 };
        Label flocksTitle = Ui.Bold(L.T("Flocks of this server"));
        flocksTitle.Dock = DockStyle.Top;
        Label membersTitle = Ui.Bold(L.T("Members of the selected flock"));
        membersTitle.Dock = DockStyle.Top;
        split.Panel1.Controls.Add(flocks);
        split.Panel1.Controls.Add(flocksTitle);
        split.Panel2.Controls.Add(members);
        split.Panel2.Controls.Add(membersTitle);

        var openFolder = Ui.Button(L.T("Show flocks.json"), (_, _) =>
        {
            if (File.Exists(FlockData.FilePath)) Ui.OpenFile(FindForm(), FlockData.FilePath);
            else Ui.ShowInfo(FindForm(), L.T("flocks.json does not exist yet: it is created when the first flock is made in the game."));
        });
        tip.SetToolTip(openFolder, L.T("Opens the file where the server keeps the flocks. Change it only with the server stopped."));
        var top = Ui.WrapFlow(openFolder);
        top.Dock = DockStyle.Top;

        var listPage = new Panel { Dock = DockStyle.Fill };
        listPage.Controls.Add(split);
        listPage.Controls.Add(summary);
        listPage.Controls.Add(top);

        var settings = new SettingsPage(tip,
            L.T("Friends, flocks and teleport to players (social.json). The server reads the file again by itself, also while it is running. " +
                "A number at 0 or -1 uses the game's own value."),
            new SettingsSection(L.T("Friends and flocks"), "social.json", SettingsSection.AllExcept()));

        var tabs = new ThemedTabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(TabOf(L.T("Flocks"), listPage));
        tabs.TabPages.Add(TabOf(L.T("Social settings"), settings));
        Controls.Add(tabs);

        clock = new System.Windows.Forms.Timer { Interval = 3000 };
        clock.Tick += (_, _) => Reload(false);
        clock.Start();
        Reload(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) clock.Dispose();
        base.Dispose(disposing);
    }

    private static TabPage TabOf(string title, Control content)
    {
        var page = new TabPage(title) { Padding = new Padding(6) };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    private static string When(long unixSeconds) =>
        unixSeconds <= 0 ? "" : DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    private static string JoinMode(int mode) => mode switch
    {
        1 => L.T("Rank 100+"),
        2 => L.T("Invite only"),
        _ => L.T("Open for all")
    };

    private void Reload(bool force)
    {
        DateTime now = FlockData.Stamp();
        if (!force && now == stamp) return;
        stamp = now;
        data = FlockData.Load();

        string? selected = flocks.SelectedItems.Count > 0 ? flocks.SelectedItems[0].Tag as string : null;
        flocks.BeginUpdate();
        flocks.Items.Clear();
        foreach (FlockData flock in data.OrderByDescending(f => f.MemberCount).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var item = new ListViewItem(flock.Name) { Tag = flock.Id, Selected = flock.Id == selected };
            item.SubItems.Add(flock.FounderName);
            item.SubItems.Add(flock.MemberCount.ToString());
            item.SubItems.Add(flock.PendingCount.ToString());
            item.SubItems.Add(JoinMode(flock.InviteMode));
            item.SubItems.Add(When(flock.Created));
            flocks.Items.Add(item);
        }
        flocks.EndUpdate();

        Ui.SetStatus(summary, data.Count == 0
            ? L.T("No flocks yet. Players create them in the Flock Hall of the Rebel City (they need a flock founder token).")
            : L.F("{0} flock(s), {1} member(s) in total.", data.Count, data.Sum(f => f.MemberCount)),
            TextRole.Normal);
        ShowMembers();
    }

    private void ShowMembers()
    {
        string? id = flocks.SelectedItems.Count > 0 ? flocks.SelectedItems[0].Tag as string : null;
        FlockData? flock = data.FirstOrDefault(f => f.Id == id);
        members.BeginUpdate();
        members.Items.Clear();
        if (flock != null)
        {
            foreach (FlockMemberData m in flock.Members.OrderByDescending(x => x.Founder).ThenByDescending(x => x.Role == 2).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var item = new ListViewItem(m.Name);
                item.SubItems.Add(L.T(m.RoleName));
                item.SubItems.Add(m.Banned ? L.T("Banned") : m.Pending ? L.T("Waiting") : L.T("Member"));
                item.SubItems.Add(When(m.JoinedAt));
                item.SubItems.Add(When(m.ActiveAt));
                members.Items.Add(item);
            }
        }
        members.EndUpdate();
    }
}
