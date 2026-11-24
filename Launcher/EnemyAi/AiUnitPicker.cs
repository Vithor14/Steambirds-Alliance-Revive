using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// The drop-down of the Enemy AI tab's unit bar: a button that opens a tree of units sorted by origin (Dungeons > each dungeon,
// World > each biome, Generic, Without origin). A unit that is in several places shows in each. A unit with no AI is drawn in a
// different color.
internal sealed class AiUnitPicker : Form
{
    private readonly TextBox filter;
    private readonly TreeView tree;
    private readonly List<AiUnitGroup> groups;
    private readonly Func<AiUnitChoice, bool> hasAi;
    private readonly string currentLabel;
    private bool closing;

    public AiUnitPicker(List<AiUnitGroup> groups, Func<AiUnitChoice, bool> hasAi, string currentLabel)
    {
        this.groups = groups;
        this.hasAi = hasAi;
        this.currentLabel = currentLabel;

        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = L.T("Pick a unit");
        ClientSize = new Size(520, 460);
        KeyPreview = true;

        filter = new ThemedTextBox { Dock = DockStyle.Top };
        filter.TextChanged += (_, _) => Fill();
        var hint = new Label
        {
            Dock = DockStyle.Bottom, Height = 34, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
            Text = L.T("Units written in a different color have no AI file. Double-click or press Enter to pick one.")
        };
        tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true, ShowLines = true };
        tree.NodeMouseDoubleClick += (_, e) => Pick(e.Node);
        tree.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Pick(tree.SelectedNode);
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) CloseOnce();
        };
        Deactivate += (_, _) => CloseOnce();

        Controls.Add(tree);
        Controls.Add(hint);
        Controls.Add(filter);
        Theme.AttachWindow(this);
        Fill();
        Shown += (_, _) => filter.Focus();
    }

    public AiUnitChoice? Chosen { get; private set; }

    private void CloseOnce()
    {
        if (closing) return;
        closing = true;
        Close();
    }

    private void Pick(TreeNode? node)
    {
        if (node?.Tag is not AiUnitChoice choice) return;
        Chosen = choice;
        DialogResult = DialogResult.OK;
        CloseOnce();
    }

    public static string NodeText(AiUnitChoice c)
    {
        if (!c.IsType) return c.Label;
        string name = c.TypeName.Length > 0 && !c.TypeName.Equals(c.TypeId, StringComparison.OrdinalIgnoreCase) ? " (" + c.TypeName + ")" : "";
        return c.TypeId + name + "  -  " + (c.OwnAi.Length > 0 ? c.OwnAi : L.T("no AI"));
    }

    private void Fill()
    {
        string text = filter.Text.Trim();
        tree.BeginUpdate();
        tree.Nodes.Clear();
        TreeNode? select = null;
        foreach (AiUnitGroup group in groups)
        {
            TreeNode? node = Add(group, text, ref select);
            if (node != null) tree.Nodes.Add(node);
        }
        if (text.Length > 0) tree.ExpandAll();
        else
        {
            foreach (TreeNode top in tree.Nodes) top.Expand();
            if (select != null) select.EnsureVisible();
        }
        if (select != null) tree.SelectedNode = select;
        tree.EndUpdate();
    }

    // A folder with the units that match the filter (null when nothing in it matches).
    private TreeNode? Add(AiUnitGroup group, string text, ref TreeNode? select)
    {
        var node = new TreeNode(group.Title) { ForeColor = Theme.ColorOf(TextRole.Muted) };
        int count = 0;
        foreach (AiUnitGroup child in group.Children)
        {
            TreeNode? sub = Add(child, text, ref select);
            if (sub == null) continue;
            node.Nodes.Add(sub);
            count += sub.Tag is int n ? n : 0;
        }
        foreach (AiUnitChoice unit in group.Units)
        {
            string shown = NodeText(unit);
            if (text.Length > 0 && shown.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0 && group.Title.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var leaf = new TreeNode(shown) { Tag = unit, ToolTipText = unit.Label };
            if (!hasAi(unit)) leaf.ForeColor = Theme.ColorOf(TextRole.Warn);
            node.Nodes.Add(leaf);
            count++;
            if (unit.Label == currentLabel) select = leaf;
        }
        if (count == 0) return null;
        node.Text = group.Title + "  (" + count + ")";
        node.Tag = count;
        return node;
    }
}
