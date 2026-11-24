using System.Text;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// "Chat" page of the Server tab: what the players write in the game chat, separated by the map where they write it
// (Rebel City, the open world, each dungeon instance). The list on the left picks a map ("All maps" shows everything with
// the map name in front of each line). Whispers and flock chat are listed under the map of their sender and can be hidden.
// The lines come from the server ([CHATMON] lines, see ChatLog); the history lives in LauncherContext.ServerChat, so a new view
// (after a language change rebuilds the screens) shows it again. New lines are drawn in batches by a UI timer.
internal sealed class ChatPage : UserControl
{
    private static readonly Color BackgroundColor = Color.FromArgb(22, 22, 26);
    private static readonly Color MapColor = Color.FromArgb(214, 214, 214);
    private static readonly Color WhisperColor = Color.FromArgb(225, 150, 235);
    private static readonly Color FlockColor = Color.FromArgb(125, 215, 125);
    private static readonly Color TimeColor = Color.FromArgb(140, 140, 140);

    private sealed class MapInfo
    {
        public string Label = "";
        public int Count;
        public ListViewItem Item = null!;
    }

    private readonly ChatLog log;
    private readonly ThemedListView maps;
    private readonly RichTextBox box;
    private readonly CheckBox autoScroll;
    private readonly CheckBox showPrivate;
    private readonly TextBox filterBox;
    private readonly System.Windows.Forms.Timer flushTimer;
    private readonly System.Windows.Forms.Timer filterTimer;
    private readonly Dictionary<string, MapInfo> rooms = new Dictionary<string, MapInfo>();
    private readonly ListViewItem allItem;
    private string? selectedRoom;      // null = all maps
    private string filter = "";
    private long shownEnd;
    private long shownGeneration = -1;
    private bool needsRebuild = true;
    private int totalCount;

    public ChatPage(LauncherContext context)
    {
        log = context.ServerChat;
        ToolTip tip = context.ToolTip;

        autoScroll = new ThemedCheckBox { Text = L.T("Auto-scroll"), Checked = true, AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
        showPrivate = new ThemedCheckBox { Text = L.T("Show whispers and flock chat"), Checked = true, AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
        tip.SetToolTip(showPrivate, L.T("Whispers (/w) and flock chat (/f) are private between the players: turn this off to see only the chat of each map."));
        filterBox = new ThemedTextBox { Width = 220, PlaceholderText = L.T("Filter by player or text..."), Margin = new Padding(3, 4, 3, 3) };

        FlowLayoutPanel toolbar = Ui.Flow(autoScroll, showPrivate, Ui.Text(L.T("Filter:")), filterBox,
            Ui.Button(L.T("Clear"), (_, _) => log.Clear()),
            Ui.Button(L.T("Copy all"), (_, _) => CopyAll()));
        toolbar.Dock = DockStyle.Top;
        toolbar.AutoSize = true;
        toolbar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        toolbar.WrapContents = true;

        Label note = Ui.Note(L.T("The chat of the players, by map. It starts empty every time the server starts. Only the chat the server relays is listed: the tutorial is private to each player."));
        note.Dock = DockStyle.Top;
        note.Padding = new Padding(3, 0, 3, 6);

        maps = Ui.Grid((L.T("Map"), 170), (L.T("Messages"), 70));
        maps.FillLastColumn = true;
        allItem = new ListViewItem(new[] { L.T("All maps"), "0" });
        maps.Items.Add(allItem);
        allItem.Selected = true;
        maps.SelectedIndexChanged += (_, _) =>
        {
            if (maps.SelectedItems.Count == 0) return;
            selectedRoom = maps.SelectedItems[0].Tag as string;
            needsRebuild = true;
            Flush();
        };

        box = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = BackgroundColor,
            ForeColor = MapColor,
            Font = new Font("Consolas", 9f),
            WordWrap = true,
            DetectUrls = false,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
        box.HandleCreated += (_, _) => needsRebuild = true;

        Card mapsCard = Ui.Framed(maps);
        mapsCard.Dock = DockStyle.Left;
        mapsCard.Width = 280;
        var boxHolder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };
        boxHolder.Controls.Add(Ui.Framed(box));

        // Fill first, then Left, then Top: WinForms docks the last added control first.
        Controls.Add(boxHolder);
        Controls.Add(mapsCard);
        Controls.Add(toolbar);
        Controls.Add(note);

        flushTimer = new System.Windows.Forms.Timer { Interval = 250 };
        flushTimer.Tick += (_, _) => Flush();
        flushTimer.Start();

        filterTimer = new System.Windows.Forms.Timer { Interval = 350 };
        filterTimer.Tick += (_, _) =>
        {
            filterTimer.Stop();
            filter = filterBox.Text.Trim();
            needsRebuild = true;
            Flush();
        };
        filterBox.TextChanged += (_, _) =>
        {
            filterTimer.Stop();
            filterTimer.Start();
        };
        showPrivate.CheckedChanged += (_, _) =>
        {
            needsRebuild = true;
            Flush();
        };
        autoScroll.CheckedChanged += (_, _) =>
        {
            if (autoScroll.Checked) ScrollToEnd();
        };
    }

    private bool Matches(ChatLog.Entry e)
    {
        if (selectedRoom != null && e.Room != selectedRoom) return false;
        if (!showPrivate.Checked && e.Channel != "map") return false;
        if (filter.Length == 0) return true;
        return e.Sender.Contains(filter, StringComparison.OrdinalIgnoreCase) || e.Target.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               e.Text.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private string Format(ChatLog.Entry e)
    {
        string map = selectedRoom == null ? "[" + e.MapLabel + "] " : "";
        string who = e.Channel switch
        {
            "whisper" => L.F("[whisper] {0} -> {1}", e.Sender, e.Target),
            "flock" => L.F("[flock {0}] {1}", e.Target, e.Sender),
            _ => e.Sender
        };
        return e.Time.ToString("HH:mm:ss") + "  " + map + who + ": " + e.Text;
    }

    private static Color ColorOf(ChatLog.Entry e) => e.Channel switch
    {
        "whisper" => WhisperColor,
        "flock" => FlockColor,
        _ => MapColor
    };

    // Keeps the list of maps and its counters up to date with the new entries.
    private void CountEntries(List<ChatLog.Entry> entries)
    {
        maps.BeginUpdate();
        try
        {
            foreach (ChatLog.Entry e in entries)
            {
                if (!rooms.TryGetValue(e.Room, out MapInfo? info))
                {
                    info = new MapInfo { Label = e.MapLabel };
                    info.Item = new ListViewItem(new[] { e.MapLabel, "0" }) { Tag = e.Room };
                    rooms[e.Room] = info;
                    maps.Items.Add(info.Item);
                }
                info.Count++;
                totalCount++;
                if (e.MapLabel.Length > 0 && info.Label != e.MapLabel)
                {
                    info.Label = e.MapLabel;
                    info.Item.Text = e.MapLabel;
                }
            }
            foreach (MapInfo info in rooms.Values) info.Item.SubItems[1].Text = info.Count.ToString();
            allItem.SubItems[1].Text = totalCount.ToString();
        }
        finally
        {
            maps.EndUpdate();
        }
    }

    private void ResetMaps()
    {
        maps.BeginUpdate();
        try
        {
            foreach (MapInfo info in rooms.Values) maps.Items.Remove(info.Item);
            rooms.Clear();
            totalCount = 0;
            allItem.SubItems[1].Text = "0";
            if (selectedRoom != null)
            {
                selectedRoom = null;
                allItem.Selected = true;
            }
        }
        finally
        {
            maps.EndUpdate();
        }
    }

    private void Flush()
    {
        // A page on a tab that was never shown has no window yet: the lines wait in the log.
        if (!box.IsHandleCreated) return;

        List<ChatLog.Entry> entries = log.Since(shownEnd, out long end, out bool truncated, out long generation);
        bool restart = truncated || generation != shownGeneration;
        if (restart)
        {
            ResetMaps();
            entries = log.Since(0, out end, out _, out generation);
        }
        shownGeneration = generation;
        shownEnd = end;
        if (entries.Count > 0 || restart) CountEntries(entries);

        if (restart || needsRebuild)
        {
            needsRebuild = false;
            List<ChatLog.Entry> all = restart ? entries : log.Since(0, out _, out _, out _);
            Rebuild(all.Where(Matches).ToList());
        }
        else if (entries.Count > 0)
        {
            AppendToBox(entries.Where(Matches).ToList());
        }
    }

    private void Rebuild(List<ChatLog.Entry> entries)
    {
        SetRedraw(false);
        try
        {
            box.Clear();
            WriteEntries(entries);
        }
        finally
        {
            SetRedraw(true);
        }
        if (autoScroll.Checked) ScrollToEnd();
    }

    private void AppendToBox(List<ChatLog.Entry> entries)
    {
        if (entries.Count == 0) return;
        bool follow = autoScroll.Checked;
        var scroll = new NativeMethods.Point32();
        int selectionStart = box.SelectionStart;
        int selectionLength = box.SelectionLength;
        if (!follow) NativeMethods.SendMessage(box.Handle, NativeMethods.EmGetScrollPos, IntPtr.Zero, ref scroll);

        SetRedraw(false);
        try
        {
            WriteEntries(entries);
            if (!follow)
            {
                box.SelectionStart = selectionStart;
                box.SelectionLength = selectionLength;
                NativeMethods.SendMessage(box.Handle, NativeMethods.EmSetScrollPos, IntPtr.Zero, ref scroll);
            }
        }
        finally
        {
            SetRedraw(true);
        }
        if (follow) ScrollToEnd();
    }

    // Consecutive lines of the same color are written in one call.
    private void WriteEntries(List<ChatLog.Entry> entries)
    {
        var builder = new StringBuilder();
        Color current = MapColor;
        foreach (ChatLog.Entry e in entries)
        {
            Color color = ColorOf(e);
            if (color != current && builder.Length > 0)
            {
                WriteText(builder.ToString(), current);
                builder.Clear();
            }
            current = color;
            builder.Append(Format(e)).Append('\n');
        }
        if (builder.Length > 0) WriteText(builder.ToString(), current);
    }

    private void WriteText(string text, Color color)
    {
        box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;
        box.SelectionColor = color;
        box.AppendText(text);
    }

    private void CopyAll()
    {
        List<ChatLog.Entry> entries = log.Since(0, out _, out _, out _);
        string text = string.Join(Environment.NewLine, entries.Where(Matches).Select(Format));
        if (text.Length == 0) return;
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), L.F("Could not copy: {0}", e.Message));
        }
    }

    private void ScrollToEnd()
    {
        if (!box.IsHandleCreated) return;
        box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;
        box.ScrollToCaret();
    }

    private void SetRedraw(bool on)
    {
        NativeMethods.SendMessage(box.Handle, NativeMethods.WmSetRedraw, on ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
        if (on) box.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            flushTimer.Stop();
            flushTimer.Dispose();
            filterTimer.Stop();
            filterTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
