using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// One page of a help window: a title and its paragraphs (English texts; the window translates each one with L.T, so every
// paragraph is a line of the language file). A paragraph that starts with "## " is a sub-heading; one that starts with
// "- " is an item of a list; the others are plain text.
internal sealed class HelpTopic
{
    public HelpTopic(string title, params string[] paragraphs)
    {
        Title = title;
        Paragraphs = paragraphs;
    }

    public string Title { get; }
    public IReadOnlyList<string> Paragraphs { get; }
}

// The help of an editor: the list of topics on the left, the text of the chosen one on the right. One window per editor
// (the "key"): asking again brings the same window to the front on the topic asked.
internal sealed class HelpWindow : Form
{
    private static readonly Dictionary<string, HelpWindow> Open = new Dictionary<string, HelpWindow>(StringComparer.Ordinal);

    private readonly string key;
    private readonly IReadOnlyList<HelpTopic> topics;
    private readonly ListBox list;
    private readonly RichTextBox text;

    private HelpWindow(string key, string title, IReadOnlyList<HelpTopic> topics)
    {
        this.key = key;
        this.topics = topics;
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(900, 600);
        MinimumSize = new Size(560, 360);
        Padding = new Padding(8);

        list = new ListBox { Dock = DockStyle.Left, Width = 230, IntegralHeight = false, BorderStyle = BorderStyle.None };
        foreach (HelpTopic topic in topics) list.Items.Add(L.T(topic.Title));
        list.SelectedIndexChanged += (_, _) => ShowTopic(list.SelectedIndex);

        Theme.Palette p = Theme.P;
        text = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = p.Input,
            ForeColor = p.Text,
            DetectUrls = false,
            Font = new Font(Font.FontFamily, 10f),
            Margin = new Padding(0)
        };
        var textHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 8, 8, 8), BackColor = p.Input };
        textHost.Controls.Add(text);

        var close = Ui.Button(L.T("Close"), (_, _) => Close());
        FlowLayoutPanel buttons = Ui.Flow(close);
        buttons.Dock = DockStyle.Bottom;
        buttons.FlowDirection = FlowDirection.RightToLeft;
        CancelButton = close;

        Controls.Add(textHost);
        Controls.Add(new Panel { Dock = DockStyle.Left, Width = 8 });
        Controls.Add(list);
        Controls.Add(buttons);
        Theme.AttachWindow(this);

        // The attach above colors the lists; the text keeps the colors set here.
        textHost.BackColor = p.Input;
        text.BackColor = p.Input;
        text.ForeColor = p.Text;
        FormClosed += (_, _) => Open.Remove(this.key);
    }

    // Opens the help (or brings the open one to the front) on the topic with this index.
    public static void ShowFor(IWin32Window owner, string key, string title, IReadOnlyList<HelpTopic> topics, int topic = 0)
    {
        if (Open.TryGetValue(key, out HelpWindow? existing) && !existing.IsDisposed)
        {
            existing.Select(topic);
            if (existing.WindowState == FormWindowState.Minimized) existing.WindowState = FormWindowState.Normal;
            existing.Activate();
            return;
        }
        var window = new HelpWindow(key, title, topics);
        Open[key] = window;
        window.Select(topic);
        window.Show(owner);
    }

    private void Select(int index)
    {
        if (topics.Count == 0) return;
        list.SelectedIndex = Math.Clamp(index, 0, topics.Count - 1);
    }

    private void ShowTopic(int index)
    {
        if (index < 0 || index >= topics.Count) return;
        HelpTopic topic = topics[index];
        Theme.Palette p = Theme.P;
        text.SuspendLayout();
        text.Clear();
        Append(L.T(topic.Title) + "\n", 15f, FontStyle.Bold, p.Text, 0);
        for (int i = 0; i < topic.Paragraphs.Count; i++)
        {
            string line = L.T(topic.Paragraphs[i]);
            bool nextIsItem = i + 1 < topic.Paragraphs.Count && topic.Paragraphs[i + 1].StartsWith("- ", StringComparison.Ordinal);
            if (line.StartsWith("## ", StringComparison.Ordinal))
                Append(line.Substring(3) + "\n", 11f, FontStyle.Bold, p.Text, 0, before: true);
            else if (line.StartsWith("- ", StringComparison.Ordinal))
                Append("•  " + line.Substring(2) + (nextIsItem ? "\n" : "\n\n"), 10f, FontStyle.Regular, p.Text, 14);
            else
                Append(line + "\n\n", 10f, FontStyle.Regular, p.Text, 0);
        }
        text.Select(0, 0);
        text.ResumeLayout();
        text.ScrollToCaret();
    }

    private void Append(string line, float size, FontStyle style, Color color, int indent, bool before = false)
    {
        text.SelectionStart = text.TextLength;
        text.SelectionLength = 0;
        text.SelectionIndent = indent;
        text.SelectionHangingIndent = indent > 0 ? 12 : 0;
        text.SelectionFont = new Font(Font.FontFamily, size, style);
        text.SelectionColor = color;
        text.AppendText((before && text.TextLength > 0 ? "\n" : "") + line);
    }
}
