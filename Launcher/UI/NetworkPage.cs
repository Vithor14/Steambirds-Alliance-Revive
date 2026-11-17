using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// "Network and multiplayer" page of the Server tab:
//  - the addresses other players use to join this server (one per network: local network, Radmin VPN, Hamachi...),
//    with a button to copy each one;
//  - the Windows Firewall rule for the server (other PCs are blocked without it on "public" networks, like most VPNs);
//  - the settings of server\network.json: name, who can join, max players, chat commands, welcome message and ports.
internal sealed class NetworkPage : UserControl
{
    private readonly LauncherContext context;
    private readonly FlowLayoutPanel addresses;
    private readonly Label acceptState;
    private readonly Label firewallState;
    private readonly Button firewallButton;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private string shownAddresses = "";
    private bool checkingFirewall;

    public NetworkPage(LauncherContext context)
    {
        this.context = context;
        ToolTip tip = context.ToolTip;

        // ------------------------------------------------------------ how to join
        acceptState = Ui.Text("");
        addresses = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0)
        };
        firewallState = Ui.Text("");
        firewallButton = Ui.Button(L.T("Allow in the Windows Firewall"), async (_, _) => await AddFirewallRuleAsync());
        tip.SetToolTip(firewallButton, L.F("Creates the inbound rule \"{0}\" for server\\SteambirdsServer.exe on every network (local network, VPNs and internet). Windows asks for administrator permission.", FirewallRule.RuleName));

        TableLayoutPanel join = Ui.Table(2);
        join.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        join.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(join, Ui.Text(L.T("Other players:")), acceptState);
        Ui.AddRow(join, Ui.Text(L.T("Addresses to share:")), addresses);
        Ui.AddRow(join, Ui.Text(L.T("Firewall:")), Ui.Flow(firewallState, firewallButton));
        Label help = Ui.Wrapping(L.T("Players on the same local network or VPN (Radmin VPN, Hamachi, ZeroTier...) see this server in the list of their launcher, or type the address above in \"Connect by IP\". On the internet, forward in the router the login port (TCP) and the Nexus, tutorial and dungeon ports (UDP) to this PC, and share your public IP."), 900, gray: true);
        Ui.AddRow(join, help);
        join.SetColumnSpan(help, 2);
        GroupBox joinGroup = Ui.Group(L.T("How other players join"), join);
        joinGroup.Dock = DockStyle.Top;

        // ------------------------------------------------------------ network.json
        var section = new SettingsSection(L.T("Network"), ServerNetworkFile.FileName, SettingsSection.AllExcept())
        {
            SavedNote = L.T("The name, players, commands and message are read again by the server by itself; ports and \"players of other PCs\" change when the server restarts.")
        };
        var settings = new SettingsPage(tip,
            L.T("Name, who can join and ports of the server of this PC (network.json). The launcher of this PC opens the game on these ports by itself; players of other PCs type the login port (httpPort) after the IP when it is not 6443."),
            section)
        {
            Dock = DockStyle.Fill
        };

        Controls.Add(settings);
        Controls.Add(joinGroup);

        refreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        refreshTimer.Tick += (_, _) => RefreshState();
        refreshTimer.Start();
        RefreshState();
        _ = CheckFirewallAsync();
    }

    private void RefreshState()
    {
        ServerNetworkFile file = ServerNetworkFile.Read();
        if (file.AllowOtherPlayers)
        {
            Ui.SetStatus(acceptState, file.ShowInServerSearch
                ? L.F("accepted (up to {0} players), and the server shows up in the search of the launchers", file.MaxPlayers)
                : L.F("accepted (up to {0} players), but hidden from the search: they type the address", file.MaxPlayers), TextRole.Good);
        }
        else
        {
            Ui.SetStatus(acceptState, L.T("only this PC (\"allowOtherPlayers\" is off below)"), TextRole.Warn);
        }

        List<LocalNetwork> networks = LocalNetworks.List();
        string key = file.HttpPort + "|" + string.Join(",", networks.Select(n => n.Kind + "=" + n.Address));
        if (key == shownAddresses) return;
        shownAddresses = key;

        addresses.SuspendLayout();
        foreach (Control old in addresses.Controls.Cast<Control>().ToList()) old.Dispose();
        addresses.Controls.Clear();
        if (networks.Count == 0)
        {
            addresses.Controls.Add(Ui.Note(L.T("no network found on this PC")));
        }
        foreach (LocalNetwork network in networks)
        {
            string address = file.HttpPort == ServerNetworkFile.DefaultHttpPort ? network.Address.ToString() : network.Address + ":" + file.HttpPort;
            Label label = Ui.Text(network.Kind + ":  " + address);
            label.Font = new Font(Control.DefaultFont, network.IsVpn ? FontStyle.Bold : FontStyle.Regular);
            Button copy = Ui.Button(L.T("Copy"), (_, _) => CopyText(address));
            addresses.Controls.Add(Ui.Flow(label, copy));
        }
        addresses.ResumeLayout(true);
    }

    private void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            context.ServerNotice(L.F("copied: {0}", text));
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), L.F("Could not copy: {0}", e.Message));
        }
    }

    private async Task CheckFirewallAsync()
    {
        if (checkingFirewall) return;
        checkingFirewall = true;
        Ui.SetStatus(firewallState, L.T("checking..."), TextRole.Muted);
        bool? exists = await Task.Run(() => FirewallRule.Exists());
        checkingFirewall = false;
        if (IsDisposed) return;
        if (exists == true)
        {
            Ui.SetStatus(firewallState, L.T("the server is allowed on every network"), TextRole.Good);
            firewallButton.Text = L.T("Create the rule again");
        }
        else if (exists == false)
        {
            Ui.SetStatus(firewallState, L.T("no rule yet: Windows may block players of other PCs (above all on VPNs)"), TextRole.Warn);
            firewallButton.Text = L.T("Allow in the Windows Firewall");
        }
        else
        {
            Ui.SetStatus(firewallState, L.T("could not check the firewall"), TextRole.Muted);
        }
    }

    private async Task AddFirewallRuleAsync()
    {
        firewallButton.Enabled = false;
        try
        {
            string? error = await Task.Run(() => FirewallRule.Add());
            if (error == null) context.ServerNotice(L.F("firewall: rule \"{0}\" created for {1}", FirewallRule.RuleName, LauncherPaths.ServerExe));
            else if (error.Length == 0) context.ServerNotice(L.T("firewall: the administrator permission was not given; nothing changed"));
            else Ui.ShowError(FindForm(), L.F("Could not create the firewall rule: {0}", error));
        }
        finally
        {
            firewallButton.Enabled = true;
        }
        await CheckFirewallAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            refreshTimer.Stop();
            refreshTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
