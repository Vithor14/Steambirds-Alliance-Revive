using System.Text;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// Shows a ConsoleBuffer: colored lines, filter, auto-scroll. The lines live in the buffer (LauncherContext), so a new
// view (after a language change rebuilds the screens) shows everything again. New lines are drawn in batches by a
// UI timer, so a server that prints thousands of lines does not freeze the window. The console is dark in both themes.
internal sealed class ConsoleView : UserControl
{
    private static readonly Color BackgroundColor = Color.FromArgb(22, 22, 26);

    private static Color ColorOf(LineKind kind) => kind switch
    {
        LineKind.Launcher => Color.FromArgb(110, 190, 255),
        LineKind.Error => Color.FromArgb(255, 115, 105),
        LineKind.Warning => Color.FromArgb(235, 195, 95),
        LineKind.Muted => Color.FromArgb(140, 140, 140),
        LineKind.Good => Color.FromArgb(125, 215, 125),
        _ => Color.FromArgb(214, 214, 214)
    };

    private readonly ConsoleBuffer buffer;
    private readonly RichTextBox box;
    private readonly CheckBox autoScroll;
    private readonly TextBox filterBox;
    private readonly System.Windows.Forms.Timer flushTimer;
    private readonly System.Windows.Forms.Timer filterTimer;
    private string filter = "";
    private bool showInfo = true;
    private long shownEnd;
    private long shownGeneration = -1;
    private bool needsRebuild = true;

    // Owners can add their own buttons here.
    public FlowLayoutPanel Toolbar { get; }

    public ConsoleView(ConsoleBuffer buffer)
    {
        this.buffer = buffer;

        Toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 0, 2)
        };
        autoScroll = new ThemedCheckBox { Text = L.T("Auto-scroll"), Checked = true, AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
        filterBox = new ThemedTextBox { Width = 220, PlaceholderText = L.T("Filter lines..."), Margin = new Padding(3, 4, 3, 3) };
        Toolbar.Controls.Add(autoScroll);
        Toolbar.Controls.Add(Ui.Text(L.T("Filter:")));
        Toolbar.Controls.Add(filterBox);
        Toolbar.Controls.Add(Ui.Button(L.T("Clear"), (_, _) => buffer.Clear()));
        Toolbar.Controls.Add(Ui.Button(L.T("Copy all"), (_, _) => CopyAll()));

        box = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = BackgroundColor,
            ForeColor = ColorOf(LineKind.Normal),
            Font = new Font("Consolas", 9f),
            WordWrap = false,
            DetectUrls = false,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.Both
        };
        box.HandleCreated += (_, _) => needsRebuild = true;

        // Fill first, then Top: WinForms docks the last added control first.
        Controls.Add(box);
        Controls.Add(Toolbar);

        flushTimer = new System.Windows.Forms.Timer { Interval = 120 };
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
        autoScroll.CheckedChanged += (_, _) =>
        {
            if (autoScroll.Checked) ScrollToEnd();
        };
    }

    private bool Matches(ConsoleBuffer.Line line) =>
        (showInfo || !line.Detail) && (filter.Length == 0 || line.Text.Contains(filter, StringComparison.OrdinalIgnoreCase));

    // Adds the "Show player actions (info)" box: info lines (what the players do) are hidden while it is off.
    public void AddInfoToggle(bool initial, ToolTip tip, Action<bool> changed)
    {
        showInfo = initial;
        var toggle = new ThemedCheckBox { Text = L.T("Show player actions (info)"), Checked = initial, AutoSize = true, Margin = new Padding(12, 6, 3, 3) };
        tip.SetToolTip(toggle, L.T("Off: the console shows warnings, errors and the events of the server (players connecting and leaving, dungeons, portals, deaths). On: also every skill, shot and game message of the players. The log file always has everything."));
        toggle.CheckedChanged += (_, _) =>
        {
            showInfo = toggle.Checked;
            needsRebuild = true;
            changed(showInfo);
            Flush();
        };
        Toolbar.Controls.Add(toggle);
    }

    private void CopyAll()
    {
        List<ConsoleBuffer.Line> lines = buffer.Since(0, out _, out _, out _);
        string text = string.Join(Environment.NewLine, lines.Where(Matches).Select(l => l.Text));
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

    private void Flush()
    {
        // A console on a tab that was never shown has no window yet: the lines wait in the buffer.
        if (!box.IsHandleCreated) return;

        List<ConsoleBuffer.Line> lines = buffer.Since(needsRebuild ? 0 : shownEnd, out long end, out bool truncated, out long generation);
        if (needsRebuild || truncated || generation != shownGeneration)
        {
            if (!needsRebuild) lines = buffer.Since(0, out end, out _, out generation);
            needsRebuild = false;
            shownGeneration = generation;
            shownEnd = end;
            Rebuild(lines);
            return;
        }
        shownEnd = end;
        if (lines.Count > 0) AppendToBox(lines.Where(Matches).ToList());
    }

    private void Rebuild(List<ConsoleBuffer.Line> lines)
    {
        SetRedraw(false);
        try
        {
            box.Clear();
            WriteLines(lines.Where(Matches).ToList());
        }
        finally
        {
            SetRedraw(true);
        }
        if (autoScroll.Checked) ScrollToEnd();
    }

    private void AppendToBox(List<ConsoleBuffer.Line> lines)
    {
        if (lines.Count == 0) return;
        bool follow = autoScroll.Checked;
        var scroll = new NativeMethods.Point32();
        int selectionStart = box.SelectionStart;
        int selectionLength = box.SelectionLength;
        if (!follow) NativeMethods.SendMessage(box.Handle, NativeMethods.EmGetScrollPos, IntPtr.Zero, ref scroll);

        SetRedraw(false);
        try
        {
            WriteLines(lines);
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

    // Consecutive lines of the same kind are written in one call.
    private void WriteLines(List<ConsoleBuffer.Line> lines)
    {
        var builder = new StringBuilder();
        LineKind current = LineKind.Normal;
        foreach (ConsoleBuffer.Line line in lines)
        {
            if (line.Kind != current && builder.Length > 0)
            {
                WriteText(builder.ToString(), ColorOf(current));
                builder.Clear();
            }
            current = line.Kind;
            builder.Append(line.Text).Append('\n');
        }
        if (builder.Length > 0) WriteText(builder.ToString(), ColorOf(current));
    }

    private void WriteText(string text, Color color)
    {
        box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;
        box.SelectionColor = color;
        box.AppendText(text);
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
