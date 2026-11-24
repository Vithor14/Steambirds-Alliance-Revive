using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// The list of the server's chat commands (Core\ServerCommandList.cs) with a filter. Double click, or Use, picks one: the
// command bar puts it in its box so the arguments can be typed after it.
internal sealed class CommandListForm : Form
{
    private readonly List<ServerCommandList.Entry> entries = ServerCommandList.All();
    private readonly ThemedTextBox filterBox;
    private readonly ThemedListView list;
    private readonly Label detail;

    // The usage of the picked entry ("/item ID [N]"); empty when the window was closed without a choice.
    public string Chosen { get; private set; } = "";

    public CommandListForm()
    {
        Text = L.T("Server commands");
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(860, 560);
        MinimumSize = new Size(560, 320);
        Padding = new Padding(10);

        filterBox = new ThemedTextBox { Dock = DockStyle.Top, PlaceholderText = L.T("Filter the commands...") };
        filterBox.TextChanged += (_, _) => Fill();

        list = Ui.Grid((L.T("Command"), 230), (L.T("Group"), 150), (L.T("What it does"), 420));
        list.DoubleClick += (_, _) => Pick();
        list.SelectedIndexChanged += (_, _) => ShowDetail();
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            Pick();
        };

        detail = Ui.Wrapping("", 780, gray: true);
        detail.Dock = DockStyle.Bottom;

        ThemedButton use = Ui.Primary(L.T("Use"), (_, _) => Pick());
        ThemedButton close = Ui.Button(L.T("Close"), (_, _) => Close());
        FlowLayoutPanel buttons = Ui.Flow(use, close);
        buttons.Dock = DockStyle.Bottom;
        buttons.FlowDirection = FlowDirection.RightToLeft;
        CancelButton = close;

        var spacer = new Panel { Dock = DockStyle.Top, Height = 8 };
        Controls.Add(Ui.Framed(list));
        Controls.Add(detail);
        Controls.Add(buttons);
        Controls.Add(spacer);
        Controls.Add(filterBox);
        Theme.AttachWindow(this);
        Fill();
    }

    private void Fill()
    {
        string filter = filterBox.Text.Trim();
        list.BeginUpdate();
        list.Items.Clear();
        foreach (ServerCommandList.Entry entry in entries)
        {
            if (filter.Length > 0 &&
                entry.Usage.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                entry.Description.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                entry.Group.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            var item = new ListViewItem(entry.Usage) { Tag = entry };
            item.SubItems.Add(entry.Group);
            item.SubItems.Add(entry.Description);
            list.Items.Add(item);
        }
        list.EndUpdate();
        ShowDetail();
    }

    private ServerCommandList.Entry? Selected => list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as ServerCommandList.Entry : null;

    private void ShowDetail()
    {
        ServerCommandList.Entry? entry = Selected;
        detail.Text = entry == null ? L.T("Pick a command and press Use (or double click it). The player it runs for is chosen in the command bar.") : entry.Usage + "  -  " + entry.Description;
    }

    private void Pick()
    {
        ServerCommandList.Entry? entry = Selected;
        if (entry == null) return;
        Chosen = entry.Usage;
        DialogResult = DialogResult.OK;
        Close();
    }
}
