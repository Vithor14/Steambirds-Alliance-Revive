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

    public AiBehaviorEditor(AiStore store)
    {
        this.store = store;
        Dock = DockStyle.Fill;

        info = new JsonForm(AiFields.Info(), () => { }, _ => Changed());
        values = new JsonForm(AiFields.Behavior(), () => { }, spec => Changed(spec));
        modeHelp = Ui.Wrapping("", 420, true);
        usedBy = Ui.Wrapping("", 420, true);

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(info, 0, 0);
        table.Controls.Add(modeHelp, 0, 1);
        table.Controls.Add(values, 0, 2);
        table.Controls.Add(usedBy, 0, 3);
        Controls.Add(table);
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
        List<string> brains = store.UsedBy(doc).ToList();
        usedBy.Text = brains.Count == 0
            ? L.T("No AI uses this behavior yet.")
            : L.F("Used by: {0}", string.Join(", ", brains));
    }
}
