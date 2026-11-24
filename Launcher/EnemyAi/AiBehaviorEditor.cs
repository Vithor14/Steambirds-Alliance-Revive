using System.Text.Json.Nodes;
using SteambirdsAi;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// Editor of one behavior file: a way of acting (how to move, how to shoot, what happens on contact).
internal sealed class AiBehaviorEditor : Panel
{
    private readonly AiStore store;
    private readonly JsonForm info;
    private readonly JsonForm values;
    private readonly Label modeHelp;
    private readonly Label usedBy;
    private AiDoc? doc;

    // False in the window of an embedded behavior: the list of AIs that use a file does not apply to it.
    public bool ShowUsedBy { get; set; } = true;

    public AiBehaviorEditor(AiStore store)
    {
        this.store = store;
        Dock = DockStyle.Fill;

        info = new JsonForm(AiFields.Info(), () => { }, _ => Changed());
        values = new JsonForm(AiFields.Behavior(), () => { }, spec => Changed(spec));
        modeHelp = Ui.Wrapping("", 420, true);
        usedBy = Ui.Wrapping("", 420, true);

        // The name, author and description are on their own tab, so the values get the whole height.
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(modeHelp, 0, 0);
        table.Controls.Add(values, 0, 1);
        table.Controls.Add(usedBy, 0, 2);

        var tabs = new ThemedTabControl { Dock = DockStyle.Fill, Padding = new Point(8, 3) };
        var behaviorPage = new TabPage(L.T("Behavior")) { Padding = new Padding(2) };
        behaviorPage.Controls.Add(table);
        var infoPage = new TabPage(L.T("Info")) { Padding = new Padding(2) };
        infoPage.Controls.Add(info);
        tabs.TabPages.Add(behaviorPage);
        tabs.TabPages.Add(infoPage);
        Controls.Add(tabs);
    }

    public event Action? Edited;

    public void Bind(AiDoc document)
    {
        doc = document;
        info.Bind(document.Root);
        values.Bind(document.Root);
        ShowHelp();
    }

    private void Changed(FieldSpec? spec = null)
    {
        ShowHelp();
        Edited?.Invoke();
    }

    private void ShowHelp()
    {
        if (doc == null) return;
        string mode = Json.Str(Json.ObjOrNull(doc.Root, "move"), "mode");
        AiOption? option = AiCatalog.MoveModes.FirstOrDefault(o => o.Id == mode);
        modeHelp.Text = option == null ? "" : L.T(option.Label) + ": " + L.T(option.Help);
        if (!ShowUsedBy)
        {
            usedBy.Text = "";
            return;
        }
        List<string> brains = store.UsedBy(doc).ToList();
        usedBy.Text = brains.Count == 0
            ? L.T("No AI uses this behavior yet.")
            : L.F("Used by: {0}", string.Join(", ", brains));
    }
}
