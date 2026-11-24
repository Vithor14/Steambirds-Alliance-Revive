using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.Editor;

// "Gates" page: walls of boxes that close a passage. A gate is a row of sub-cells (gates) or, in the side corridors,
// a column (sideGates). A gate with "destructible" on is a barricade the players break; with it off the gate
// is invulnerable and opens by itself when its condition is true.
internal sealed class GatesPage : EditorPage
{
    private readonly CollectionPanel gates;
    private readonly GateSource source;
    private readonly JsonForm rowForm;
    private readonly JsonForm columnForm;
    private readonly Button addColumnButton;

    public GatesPage(EditorContext context) : base(context)
    {
        source = new GateSource(Session, () => !string.IsNullOrEmpty(Canvas.GateType) ? Canvas.GateType : Context.TypeNames().FirstOrDefault() ?? "");

        rowForm = new JsonForm(RowFields(), Session.Checkpoint, GeometryChanged) { Dock = DockStyle.Fill };
        columnForm = new JsonForm(ColumnFields(), Session.Checkpoint, GeometryChanged) { Dock = DockStyle.Fill };
        var geometry = new Panel { Dock = DockStyle.Fill };
        geometry.Controls.Add(rowForm);
        geometry.Controls.Add(columnForm);

        addColumnButton = Ui.Button(L.T("Add side-corridor gate"), (_, _) => AddColumn());
        Context.Tip.SetToolTip(addColumnButton, L.T("A gate that runs along v, for the corridors at the side of the map (portoesLaterais)."));

        gates = new CollectionPanel(Session, source, CommonFields(), "gate", listHeight: 120, extra: geometry, formHeight: 230);
        gates.ItemSelected += item =>
        {
            bool column = item?.Column ?? false;
            rowForm.Visible = item != null && !column;
            columnForm.Visible = item != null && column;
            rowForm.Bind(item != null && !column ? item.Data : null);
            columnForm.Bind(item != null && column ? item.Data : null);
        };
        gates.UserSelected += item =>
        {
            if (item != null) Canvas.SelectData(item.Data);
        };
        gates.ListChanged += () => Canvas.Invalidate();

        Controls.Add(Stack((gates, true), (addColumnButton, false), (Hint("Gates can also be drawn on the map with the Gate tool. Drag a gate to move it."), false)));
        Reload();
    }

    public override void Reload()
    {
        gates.Reload();
    }

    public override bool ShowObject(MapObject item)
    {
        if (item.Kind != ObjectKind.Gate) return false;
        gates.SelectData(item.Data);
        return true;
    }

    private void AddColumn()
    {
        Session.Checkpoint();
        ListItem item = source.AddColumn("gate");
        Session.Touch();
        gates.Reload(item.Data);
    }

    private void GeometryChanged(FieldSpec spec)
    {
        Session.Touch();
    }

    private IEnumerable<FieldSpec> CommonFields()
    {
        yield return new FieldSpec("name", "Gate", FieldKind.Text) { Help = "Name of the gate. Other parts of the map (rooms, conditions) point to it by this name." };
        yield return new FieldSpec("type", "Unit type of the boxes", FieldKind.EditableChoice)
        {
            Choices = Context.TypeNames, Picture = Context.PictureOfType,
            Help = "A type of the \"Unit types\" page (or of enemies.json): one box of this type is placed on each free cell."
        };
        yield return new FieldSpec("damageable", "Destructible barricade", FieldKind.Bool)
        {
            DefaultBool = true,
            Help = "On: the boxes take damage; when one breaks, the whole gate falls.\nOff: the boxes are invulnerable and the gate opens when \"Opens when\" is true."
        };
        yield return new FieldSpec("opensWhen", "Opens when", FieldKind.Condition)
        {
            Condition = Context.EndConditions,
            Help = "What makes the gate open: a room is cleared, a unit type dies or a quest is done. With several conditions all of them must be true. Empty = it never opens by itself."
        };
        yield return new FieldSpec("room", "Leads to room", FieldKind.EditableChoice)
        {
            Choices = Context.RoomNames,
            Help = "The room behind the gate. A room that closes when entered closes this gate again behind the player."
        };
    }

    private static IEnumerable<FieldSpec> RowFields()
    {
        yield return FieldSpec.Header("Row (runs along u)");
        yield return new FieldSpec("sj", "Row (sj)", FieldKind.Int) { Min = -1000, Max = 1000, Help = "Sub-cell row: 3 u each (4 per tile)." };
        yield return new FieldSpec("siFrom", "From column (si)", FieldKind.Int) { Min = -1000, Max = 1000, Help = "First sub-cell column of the gate (3 u each)." };
        yield return new FieldSpec("siTo", "To column (si)", FieldKind.Int) { Min = -1000, Max = 1000, Help = "Last sub-cell column of the gate." };
    }

    private static IEnumerable<FieldSpec> ColumnFields()
    {
        yield return FieldSpec.Header("Column (runs along v)");
        yield return new FieldSpec("si", "Column (si)", FieldKind.Int) { Min = -1000, Max = 1000, Help = "Sub-cell column: 3 u each (4 per tile)." };
        yield return new FieldSpec("sjFrom", "From row (sj)", FieldKind.Int) { Min = -1000, Max = 1000, Help = "First sub-cell row of the gate (3 u each)." };
        yield return new FieldSpec("sjTo", "To row (sj)", FieldKind.Int) { Min = -1000, Max = 1000, Help = "Last sub-cell row of the gate." };
    }
}
