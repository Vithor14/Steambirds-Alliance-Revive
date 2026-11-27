using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// The unit tree shared by the Enemy AI tab (docked on the left) and by the drop-down picker other editors open.
// Folders come from AiUnitOrigins: Dungeons > each dungeon, World > each biome, Generic, Without origin. A unit that
// lives in several places is listed in each. A unit with no AI file is drawn in a different color. On the Enemy AI
// tab, a unit that already has its own AI (not a generic one) is drawn green.
internal sealed class AiUnitList : UserControl
{
    private readonly TextBox filter;
    private readonly TreeView tree;
    private readonly bool chooseOnClick;
    private List<AiUnitGroup> groups = new List<AiUnitGroup>();
    private Func<AiUnitChoice, bool> hasAi = _ => true;
    private Func<AiUnitChoice, bool>? personal;
    private string currentLabel = "";
    private AiUnitChoice? currentChoice;
    private bool filling;

    public AiUnitList(bool chooseOnClick, string hint)
    {
        this.chooseOnClick = chooseOnClick;
        Dock = DockStyle.Fill;

        filter = new ThemedTextBox { Dock = DockStyle.Top, PlaceholderText = L.T("Filter units...") };
        filter.TextChanged += (_, _) =>
        {
            if (!filling) Fill();
        };
        var hintLabel = new Label
        {
            Dock = DockStyle.Bottom, Height = 52, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Text = hint
        };
        tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true, ShowLines = true };
        tree.AfterSelect += (_, e) =>
        {
            if (filling) return;
            if (e.Node?.Tag is not AiUnitChoice choice)
            {
                if (currentChoice != null) TrySelect(currentChoice);
                return;
            }
            currentChoice = choice;
            currentLabel = choice.Label;
            if (chooseOnClick) Chosen?.Invoke(choice);
        };
        tree.NodeMouseDoubleClick += (_, e) => Confirm(e.Node);
        tree.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Confirm(tree.SelectedNode);
        };

        Controls.Add(tree);
        Controls.Add(hintLabel);
        Controls.Add(filter);
    }

    public event Action<AiUnitChoice>? Chosen;

    public void FocusFilter() => filter.Focus();

    // Rebuilds the tree from these groups. The unit with this label stays selected, and choosing it does not raise Chosen.
    // personal, when given, paints those units green: they already have an AI of their own (not a generic one).
    public void Set(List<AiUnitGroup> groups, Func<AiUnitChoice, bool> hasAi, string currentLabel, Func<AiUnitChoice, bool>? personal = null)
    {
        this.groups = groups;
        this.hasAi = hasAi;
        this.personal = personal;
        this.currentLabel = currentLabel ?? "";
        Fill();
    }

    // Shows this unit in the tree (clears the filter when the filter is hiding it) without raising Chosen.
    public void ShowSelection(AiUnitChoice? unit)
    {
        if (unit != null && ReferenceEquals(tree.SelectedNode?.Tag, unit))
        {
            currentChoice = unit;
            currentLabel = unit.Label;
            return;
        }
        currentChoice = unit;
        currentLabel = unit?.Label ?? "";
        if (unit != null && TrySelect(unit)) return;
        if (filter.Text.Length > 0)
        {
            filling = true;
            filter.Text = "";
            filling = false;
        }
        Fill();
    }

    public static string NodeText(AiUnitChoice c)
    {
        if (!c.IsType) return c.Label;
        string name = c.TypeName.Length > 0 && !c.TypeName.Equals(c.TypeId, StringComparison.OrdinalIgnoreCase) ? " (" + c.TypeName + ")" : "";
        return c.TypeId + name + "  -  " + (c.OwnAi.Length > 0 ? c.OwnAi : L.T("no AI"));
    }

    private void Confirm(TreeNode? node)
    {
        if (node?.Tag is not AiUnitChoice choice) return;
        currentChoice = choice;
        currentLabel = choice.Label;
        Chosen?.Invoke(choice);
    }

    private bool TrySelect(AiUnitChoice unit)
    {
        TreeNode? node = Find(tree.Nodes, unit);
        if (node == null) return false;
        filling = true;
        tree.SelectedNode = node;
        node.EnsureVisible();
        filling = false;
        return true;
    }

    private static TreeNode? Find(TreeNodeCollection nodes, AiUnitChoice unit)
    {
        foreach (TreeNode node in nodes)
        {
            if (ReferenceEquals(node.Tag, unit)) return node;
            TreeNode? child = Find(node.Nodes, unit);
            if (child != null) return child;
        }
        return null;
    }

    private void Fill()
    {
        string text = filter.Text.Trim();
        filling = true;
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
        tree.SelectedNode = select;
        currentChoice = select?.Tag as AiUnitChoice;
        tree.EndUpdate();
        filling = false;
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
            if (text.Length > 0 && shown.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0 &&
                unit.Label.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0 &&
                group.Title.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var leaf = new TreeNode(shown) { Tag = unit, ToolTipText = unit.Label };
            if (personal != null && personal(unit)) leaf.ForeColor = Theme.ColorOf(TextRole.Good);
            else if (!hasAi(unit)) leaf.ForeColor = Theme.ColorOf(TextRole.Warn);
            node.Nodes.Add(leaf);
            count++;
            if (select == null && unit.Label == currentLabel) select = leaf;
        }
        if (count == 0) return null;
        node.Text = group.Title + "  (" + count + ")";
        node.Tag = count;
        return node;
    }
}

// The drop-down other editors use to pick a unit: the same tree, closed by a double-click, Enter or clicking away.
internal sealed class AiUnitPicker : Form
{
    private bool closing;

    public AiUnitPicker(List<AiUnitGroup> groups, Func<AiUnitChoice, bool> hasAi, string currentLabel)
    {
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = L.T("Pick a unit");
        ClientSize = new Size(520, 460);
        KeyPreview = true;

        var list = new AiUnitList(false, L.T("Units written in a different color have no AI file. Double-click or press Enter to pick one."))
        {
            Dock = DockStyle.Fill
        };
        list.Set(groups, hasAi, currentLabel);
        list.Chosen += choice =>
        {
            Chosen = choice;
            DialogResult = DialogResult.OK;
            CloseOnce();
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) CloseOnce();
        };
        Deactivate += (_, _) => CloseOnce();

        Controls.Add(list);
        Theme.AttachWindow(this);
        Shown += (_, _) => list.FocusFilter();
    }

    public AiUnitChoice? Chosen { get; private set; }

    private void CloseOnce()
    {
        if (closing) return;
        closing = true;
        Close();
    }
}
