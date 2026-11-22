using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.Editor;

// "Rooms" page: the encounters of a dungeon. Each room has an area (where the enemy AI stays), a line that wakes it,
// the enemies that wait in it and, for arenas and ambushes, the waves.
internal sealed class RoomsPage : EditorPage
{
    private readonly CollectionPanel rooms;
    private readonly CollectionPanel groups;
    private readonly CollectionPanel waves;
    private readonly CollectionPanel waveGroups;
    private readonly ThemedTabControl inner;
    private readonly TabPage unitsPage;
    private readonly TabPage wavesPage;

    public RoomsPage(EditorContext context) : base(context)
    {
        groups = new CollectionPanel(Session,
            new UnitGroupSource(
                () => Json.ArrOrNull(CurrentRoom, "enemies"),
                () => CurrentRoom == null ? null : Json.Arr(CurrentRoom, "enemies"),
                Context.DefaultUnitType),
            GroupFields(), "", listHeight: 80);

        waveGroups = new CollectionPanel(Session,
            new UnitGroupSource(
                () => Json.ArrOrNull(CurrentWave, "groups"),
                () => CurrentWave == null ? null : Json.Arr(CurrentWave, "groups"),
                Context.DefaultUnitType),
            GroupFields(), "", listHeight: 70);

        waves = new CollectionPanel(Session,
            new ObjectArraySource(
                () => Json.ArrOrNull(CurrentRoom, "waves"),
                () => CurrentRoom == null ? new JsonArray() : Json.Arr(CurrentRoom, "waves"),
                "name",
                name => new JsonObject { ["name"] = name, ["delayBeforeSeconds"] = 5, ["groups"] = new JsonArray() }),
            WaveFields(), "wave", listHeight: 60, extra: waveGroups, formHeight: 90);

        unitsPage = new TabPage(L.T("Enemies")) { Padding = new Padding(2) };
        unitsPage.Controls.Add(groups);
        wavesPage = new TabPage(L.T("Waves")) { Padding = new Padding(2) };
        wavesPage.Controls.Add(waves);
        inner = new ThemedTabControl { Dock = DockStyle.Fill, Padding = new Point(10, 3) };
        inner.TabPages.AddRange(new[] { unitsPage, wavesPage });

        rooms = new CollectionPanel(Session,
            new ObjectArraySource(
                () => Json.ArrOrNull(Document.Root, "encounters"),
                () => Json.Arr(Document.Root, "encounters"),
                "room",
                name => new JsonObject
                {
                    ["room"] = name,
                    ["area"] = Context.AreaNames().FirstOrDefault() ?? "",
                    ["wakes"] = "onEnter",
                    ["enemies"] = new JsonArray()
                }),
            RoomFields(), "room", listHeight: 84,
            fieldRenamed: (oldName, newName) => MapRefactor.Rename(Document, NameKind.Room, oldName, newName),
            extra: inner, formHeight: 215);

        rooms.ItemSelected += _ =>
        {
            ReloadInner();
            if (rooms.Current != null) Canvas.Invalidate();
        };
        waves.ItemSelected += _ => waveGroups.Reload();
        rooms.ListChanged += () => Canvas.Invalidate();
        groups.ListChanged += () => Canvas.Invalidate();
        waveGroups.ListChanged += () => Canvas.Invalidate();
        waves.ListChanged += () => Canvas.Invalidate();

        Controls.Add(rooms);
        Reload();
    }

    private JsonObject? CurrentRoom => rooms?.Current?.Data;
    private JsonObject? CurrentWave => waves?.Current?.Data;

    public override void Reload()
    {
        rooms.Reload();
    }

    private void ReloadInner()
    {
        bool has = CurrentRoom != null;
        inner.Enabled = has;
        groups.Reload();
        waves.Reload();
        waveGroups.Reload();
    }

    public override bool ShowObject(MapObject item)
    {
        if (item.Kind != ObjectKind.Unit) return false;
        if (!rooms.SelectText(item.Owner)) return false;
        ReloadInner();
        if (!item.InWave)
        {
            inner.SelectedTab = unitsPage;
            if (item.Group != null) groups.SelectData(item.Group);
            return true;
        }

        inner.SelectedTab = wavesPage;
        foreach (JsonObject wave in Json.Objects(Json.ArrOrNull(CurrentRoom, "waves")))
        {
            if (!ReferenceEquals(wave["groups"], item.GroupContainer)) continue;
            waves.SelectData(wave);
            if (item.Group != null) waveGroups.SelectData(item.Group);
            break;
        }
        return true;
    }

    // Fields of a room.
    private IEnumerable<FieldSpec> RoomFields()
    {
        yield return new FieldSpec("room", "Room", FieldKind.Text) { Help = "Name of the room. Gates and conditions refer to it (cleared:<room>)." };
        yield return new FieldSpec("area", "Area", FieldKind.EditableChoice)
        {
            Choices = Context.AreaNames,
            Help = "Area where the enemy AI of this room stays. Required."
        };
        yield return new FieldSpec("line", "Wake-up line", FieldKind.EditableChoice)
        {
            Choices = Context.LineNames,
            Help = "The room wakes up when the player passes this line (v)."
        };
        yield return new FieldSpec("wakes", "Wakes up", FieldKind.EditableChoice)
        {
            Choices = () => EditorContext.WakeModes,
            Help = "aoEntrar = when the player enters the area. death:<unit type> = when that unit dies."
        };
        yield return new FieldSpec("boss", "Boss room", FieldKind.Bool) { Help = "The dungeon ends when every boss room is cleared (when the exit has no condition)." };
        yield return new FieldSpec("miniboss", "Mini-boss room", FieldKind.Bool) { Help = "Marks the room as a mini-boss room (the dungeon shows it as an optional fight)." };
        yield return new FieldSpec("closesOnEnter", "Closes its gates when entered", FieldKind.Bool)
        {
            Help = "The gates that lead to this room close behind the player and open when the room is cleared."
        };
        yield return new FieldSpec("altarRoom", "Altar room (not used yet)", FieldKind.Bool) { Help = "Reserved for altar rooms of the original dungeons. The server does not use it yet." };
        yield return new FieldSpec("note", "Note", FieldKind.MultiText) { Help = "Free text for you. The server ignores it." };
    }

    // Fields of a group of enemies (the same for a room and for a wave).
    private IEnumerable<FieldSpec> GroupFields()
    {
        yield return new FieldSpec("type", "Unit type", FieldKind.Choice)
        {
            Choices = Context.TypeNames,
            AllowEmpty = false,
            Help = "A type of the \"Unit types\" page."
        };
        yield return new FieldSpec("pos", "Positions (u, v, rotation)", FieldKind.Positions)
        {
            Help = "One enemy per line: u, v, rotation. You can also place them with the Enemy tool on the map."
        };
        yield return new FieldSpec("behavior", "Behavior", FieldKind.Choice) { Choices = () => EditorContext.Behaviors, Help = "Replaces the behavior of the type for this group." };
        yield return new FieldSpec("wakes", "Appears when", FieldKind.EditableChoice)
        {
            Choices = () => EditorContext.WakeModes,
            Help = "Empty = at the start. death:<unit type> = the unit is born when that type dies."
        };
        yield return new FieldSpec("route", "Route", FieldKind.EditableChoice) { Choices = Context.RouteNames, Help = "Route (Points tab) the units of this group follow." };
        yield return new FieldSpec("miniboss", "Mini-boss", FieldKind.Bool) { Help = "The units of this group are mini-bosses." };
        yield return new FieldSpec("note", "Note", FieldKind.Text) { Help = "Free text for you. The server ignores it." };
    }

    private static IEnumerable<FieldSpec> WaveFields()
    {
        yield return new FieldSpec("name", "Wave", FieldKind.Text) { Help = "Name of the wave, only to tell it apart in the list." };
        yield return new FieldSpec("delayBeforeSeconds", "Seconds before", FieldKind.OptionalNumber)
        {
            Help = "The first wave comes this long after the player enters. The others come when the one before is dead."
        };
        yield return new FieldSpec("message", "Message", FieldKind.Text) { Help = "Chat message shown when the wave starts." };
    }
}
