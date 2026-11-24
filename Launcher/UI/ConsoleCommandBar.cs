using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// The command bar under the server console: "Run for" (which player), the command box, Send and the list of commands.
// A command is the chat command a player would type in the game (Server\Admin\ConsoleCommands.cs runs it as that player):
// the answer goes to the player's chat, and the server writes a [CONSOLE] line in the console.
internal sealed class ConsoleCommandBar : FlowLayoutPanel
{
    // What the "Run for" choices stand for. Value is the first part of the line sent to the server.
    private sealed class Target
    {
        public string Text = "";
        public string Value = "";
        public override string ToString() => Text;
    }

    private readonly LauncherContext context;
    private readonly ThemedComboBox targetBox;
    private readonly ThemedTextBox commandBox;
    private readonly ThemedButton sendButton;
    private readonly List<string> history = new List<string>();
    private int historyIndex;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private bool refreshing;
    private string lastNames = "";

    public ConsoleCommandBar(LauncherContext context)
    {
        this.context = context;
        ToolTip tip = context.ToolTip;

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = true;
        FlowDirection = FlowDirection.LeftToRight;
        Padding = new Padding(0, 6, 0, 0);

        targetBox = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, Margin = new Padding(3, 4, 3, 3) };
        commandBox = new ThemedTextBox { Width = 320, PlaceholderText = "/command", Margin = new Padding(3, 4, 3, 3) };
        sendButton = Ui.Primary(L.T("Send"), (_, _) => Send());
        ThemedButton listButton = Ui.Button(L.T("Command list..."), (_, _) => ShowList());
        ThemedButton refreshButton = Ui.Button(L.T("Refresh players"), (_, _) => _ = RefreshPlayersAsync());

        tip.SetToolTip(targetBox, L.T("The player the command runs for, as if they had typed it in the chat. The first choice is the game of this PC (the host)."));
        tip.SetToolTip(commandBox, L.T("A chat command, for example /xp 500 or /dungeon bomb. Enter sends it; the up and down arrows bring back the last ones."));
        tip.SetToolTip(refreshButton, L.T("Reads the list of players online from the server."));
        HelpButton help = Ui.Help(L.T("Runs a chat command of the server (the ones in the command list) as the player chosen in \"Run for\": the same thing as that player typing it in the game chat, but it works without chat permission (network.json chatCommands). What the command answers appears in that player's chat; the server also writes a [CONSOLE] line here. \"All players\" runs it once for each player online. Commands that only make sense in a place (/room, /step...) need that player to be there."),
            L.T("Server commands"));

        commandBox.KeyDown += OnKeyDown;
        context.ServerStateChanged += OnServerStateChanged;

        Controls.Add(Ui.Text(L.T("Run for:")));
        Controls.Add(targetBox);
        Controls.Add(Ui.Text(L.T("Command:")));
        Controls.Add(commandBox);
        Controls.Add(sendButton);
        Controls.Add(listButton);
        Controls.Add(refreshButton);
        Controls.Add(help);

        refreshTimer = new System.Windows.Forms.Timer { Interval = 6000 };
        refreshTimer.Tick += (_, _) =>
        {
            if (Visible && context.Server.State == ServerState.Running) _ = RefreshPlayersAsync();
        };
        refreshTimer.Start();

        FillTargets(new List<string>());
        UpdateEnabled();
    }

    private void OnServerStateChanged()
    {
        if (IsDisposed) return;
        UpdateEnabled();
        if (context.Server.State == ServerState.Running) _ = RefreshPlayersAsync();
        else FillTargets(new List<string>());
    }

    private void UpdateEnabled()
    {
        bool running = context.Server.State == ServerState.Running;
        commandBox.Enabled = running;
        sendButton.Enabled = running;
        targetBox.Enabled = running;
    }

    // "Players of this PC", "All players" and each player online.
    private void FillTargets(List<string> names)
    {
        string key = string.Join("|", names);
        if (key == lastNames && targetBox.Items.Count > 0) return;
        lastNames = key;

        string? keep = (targetBox.SelectedItem as Target)?.Value;
        targetBox.Items.Clear();
        targetBox.Items.Add(new Target { Text = L.T("Players of this PC (host)"), Value = "" });
        targetBox.Items.Add(new Target { Text = L.T("All players online"), Value = "*" });
        foreach (string name in names) targetBox.Items.Add(new Target { Text = name, Value = name });

        int index = 0;
        if (keep != null)
        {
            for (int i = 0; i < targetBox.Items.Count; i++)
            {
                if (((Target)targetBox.Items[i]!).Value == keep)
                {
                    index = i;
                    break;
                }
            }
        }
        targetBox.SelectedIndex = index;
    }

    // The players come from the info page of the server (the same one the server list reads).
    private async Task RefreshPlayersAsync()
    {
        if (refreshing || IsDisposed) return;
        refreshing = true;
        try
        {
            ServerBrowser.ProbeResult probe = await ServerBrowser.ProbeAsync("localhost", ServerNetworkFile.Read().HttpPort, 2000, CancellationToken.None);
            if (IsDisposed || probe.Server == null) return;
            FillTargets(probe.Server.PlayerNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList());
        }
        catch
        {
            // The server is busy or stopping: the list stays as it was.
        }
        finally
        {
            refreshing = false;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = e.SuppressKeyPress = true;
            Send();
        }
        else if (e.KeyCode == Keys.Up && history.Count > 0)
        {
            e.Handled = e.SuppressKeyPress = true;
            historyIndex = Math.Max(0, historyIndex - 1);
            commandBox.Text = history[historyIndex];
            commandBox.SelectionStart = commandBox.TextLength;
        }
        else if (e.KeyCode == Keys.Down && history.Count > 0)
        {
            e.Handled = e.SuppressKeyPress = true;
            historyIndex = Math.Min(history.Count, historyIndex + 1);
            commandBox.Text = historyIndex < history.Count ? history[historyIndex] : "";
            commandBox.SelectionStart = commandBox.TextLength;
        }
    }

    private void Send()
    {
        string text = commandBox.Text.Trim();
        if (text.Length == 0) return;
        if (!text.StartsWith("/")) text = "/" + text;
        string value = (targetBox.SelectedItem as Target)?.Value ?? "";
        string line = value.Length == 0 ? text : value + "\t" + text;
        if (!context.Server.SendCommand(line, out string error))
        {
            Ui.ShowError(FindForm(), error);
            return;
        }
        if (history.Count == 0 || history[^1] != text) history.Add(text);
        if (history.Count > 50) history.RemoveAt(0);
        historyIndex = history.Count;
        commandBox.Clear();
    }

    private void ShowList()
    {
        using var form = new CommandListForm();
        if (form.ShowDialog(FindForm()) != DialogResult.OK || form.Chosen.Length == 0) return;
        // Only the command goes in the box: the arguments of the usage ("ID [N]") are for the host to fill in.
        string chosen = form.Chosen;
        int space = chosen.IndexOf(' ');
        commandBox.Text = (space < 0 ? chosen : chosen.Substring(0, space)) + (space < 0 ? "" : " ");
        commandBox.Focus();
        commandBox.SelectionStart = commandBox.TextLength;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            refreshTimer.Stop();
            refreshTimer.Dispose();
            context.ServerStateChanged -= OnServerStateChanged;
        }
        base.Dispose(disposing);
    }
}
