using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.Editor;

// "Unit types" page: the enemies of the map ("types"). A type is a game unit (template) plus the changes this map makes:
// behavior, health, weapons, phases, what it summons, what it leaves when it dies...
internal sealed class UnitTypesPage : EditorPage
{
    private readonly CollectionPanel types;
    private readonly JsonForm combatForm;
    private readonly JsonForm deathForm;
    private readonly CollectionPanel phases;
    private readonly Button aiButton;
    private readonly Label aiState;
    private readonly ThemedTabControl inner;
    private readonly TabPage phasesPage;

    public UnitTypesPage(EditorContext context) : base(context)
    {
        combatForm = new JsonForm(CombatFields(), Session.Checkpoint, TabFieldChanged);
        deathForm = new JsonForm(DeathFields(), Session.Checkpoint, TabFieldChanged);

        phases = new CollectionPanel(Session,
            new PhaseSource(
                () => Json.ArrOrNull(CurrentType, "phases"),
                () => CurrentType == null ? null : Json.Arr(CurrentType, "phases")),
            PhaseFields(), "", listHeight: 70);

        aiState = Ui.Wrapping("", 400, true);
        aiButton = Ui.Button(L.T("Edit AI..."), (_, _) => EditAi());
        Label soon = Ui.ComingSoon(Context.Tip,
            L.T("Coming soon: an editor for the AI of the enemies. The AI will be saved in the optional \"ia\" field of the unit type; the map editor already keeps that field and the server ignores it."));
        TabPage aiPage = BuildAiPage(soon);

        TabPage combatPage = new TabPage(L.T("Combat")) { Padding = new Padding(2) };
        combatPage.Controls.Add(combatForm);
        TabPage deathPage = new TabPage(L.T("Death and summons")) { Padding = new Padding(2) };
        deathPage.Controls.Add(deathForm);
        phasesPage = new TabPage(L.T("Phases")) { Padding = new Padding(2) };
        phasesPage.Controls.Add(phases);

        inner = new ThemedTabControl { Dock = DockStyle.Fill, Padding = new Point(8, 3) };
        inner.TabPages.AddRange(new[] { combatPage, deathPage, phasesPage, aiPage });

        types = new CollectionPanel(Session,
            new ObjectDictionarySource(
                () => Json.ObjOrNull(Document.Root, "types"),
                () => Json.Obj(Document.Root, "types"),
                name => new JsonObject { ["name"] = name, ["template"] = "", ["behavior"] = "chase" },
                (oldName, newName) => MapRefactor.Rename(Document, NameKind.UnitType, oldName, newName)),
            GeneralFields(), "unit", listHeight: 110, extra: inner, formHeight: 150);

        types.ItemSelected += item =>
        {
            combatForm.Bind(item?.Data);
            deathForm.Bind(item?.Data);
            phases.Reload();
            phases.Enabled = item != null;
            UpdateAiState();
        };
        types.ListChanged += () => Canvas.Invalidate();
        phases.ListChanged += () => Canvas.Invalidate();

        Controls.Add(types);
        Reload();
    }

    private JsonObject? CurrentType => types?.Current?.Data;

    public override void Reload()
    {
        types.Reload();
    }

    public override bool ShowObject(MapObject item)
    {
        if (item.Kind != ObjectKind.Unit) return false;
        return types.SelectKey(item.Name);
    }

    // Selects a type by name (used by the palette of the editor).
    public bool SelectType(string name) => types.SelectKey(name);

    private void TabFieldChanged(FieldSpec spec)
    {
        Session.Touch();
        UpdateAiState();
    }

    // ------------------------------------------------------------------ AI slot

    private TabPage BuildAiPage(Label soon)
    {
        var page = new TabPage(L.T("Enemy AI")) { Padding = new Padding(6) };
        Label title = Ui.Bold(L.T("Enemy AI"));
        Label text = Ui.Wrapping(
            L.T("The behavior list of the type (Combat tab) is what the server uses today. A custom AI editor will come later: it will keep its data in the \"ia\" field of the unit type. The map editor already preserves that field when it saves."),
            400, true);
        aiButton.Enabled = EnemyAiEditors.Available;
        soon.Visible = !EnemyAiEditors.Available;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        flow.Controls.Add(title);
        flow.Controls.Add(text);
        flow.Controls.Add(Ui.Flow(aiButton, soon));
        flow.Controls.Add(aiState);
        page.Controls.Add(flow);
        return page;
    }

    private void UpdateAiState()
    {
        JsonObject? type = CurrentType;
        if (type == null)
        {
            aiState.Text = "";
            return;
        }
        aiState.Text = EnemyAiEditors.HasAi(type)
            ? L.T("This type has AI data (\"ia\"). It is kept as it is.")
            : L.T("This type has no custom AI data.");
    }

    private void EditAi()
    {
        IEnemyAiEditor? editor = EnemyAiEditors.Current;
        ListItem? item = types.Current;
        if (editor == null || item == null) return;
        editor.Edit(this, item.Key, item.Data, Session.Checkpoint, () =>
        {
            Session.Touch();
            UpdateAiState();
        });
    }

    // ------------------------------------------------------------------ fields

    private static IEnumerable<string> GameUnitNames()
    {
        return EditorAssets.Current.Units.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IEnumerable<FieldSpec> GeneralFields()
    {
        yield return new FieldSpec("name", "Display name", FieldKind.Text) { Help = "Name shown in the log and in messages." };
        yield return new FieldSpec("template", "Game unit (template)", FieldKind.EditableChoice)
        {
            Choices = GameUnitNames,
            Help = "The unit of the game this type is made from (units_config.txt).\nSeveral can be given as a|b, or auto:<name>."
        };
        yield return new FieldSpec("team", "Team (1 ally, 2 enemy)", FieldKind.OptionalNumber) { Help = "Side of the unit: 1 = ally of the player, 2 = enemy. Empty = the team of the game unit." };
        yield return new FieldSpec("note", "Note", FieldKind.MultiText) { Help = "Free text for you. The server ignores it." };
    }

    private IEnumerable<FieldSpec> CombatFields()
    {
        yield return FieldSpec.Header("Role");
        yield return new FieldSpec("boss", "Boss", FieldKind.Bool) { Help = "Marks a boss: it gets a boss health bar and counts as a boss kill for the pilot." };
        yield return new FieldSpec("miniboss", "Mini-boss", FieldKind.Bool) { Help = "Marks a mini-boss: stronger than a normal enemy, with its own health bar." };
        yield return new FieldSpec("invulnerable", "Invulnerable", FieldKind.Bool) { Help = "The unit takes no damage (walls, decorations, scenery)." };
        yield return new FieldSpec("opensAfterHitSeconds", "Wakes up when hit", FieldKind.Bool) { Help = "The unit stays idle until a shot hits it, then it wakes up and fights." };
        yield return new FieldSpec("freeRoam", "Free (no room)", FieldKind.Bool) { Help = "The unit does not belong to a room: it is born at the start and the room logic (waking up, clearing) ignores it." };
        yield return new FieldSpec("excludeFromRoomClear", "Does not count to clear the room", FieldKind.Bool)
        {
            Help = "The unit is never needed to clear the room, even when it has weapons."
        };

        yield return FieldSpec.Header("Behavior");
        yield return new FieldSpec("behavior", "Behavior", FieldKind.Choice)
        {
            Choices = () => EditorContext.Behaviors,
            Help = "perseguir (chase), circular, torre (turret), kamikaze (explodes on contact), investida (charge), patrulhar, escoltar, parado (still)."
        };
        yield return Number("aggroRadius", "Aggro radius", "0 = shoots as soon as it is born.");
        yield return Number("preferredDistance", "Preferred distance", "Distance the unit keeps from the player. Bigger than the weapon range = it never shoots.");
        yield return Number("contactRadius", "Contact radius", "Kamikaze: distance where it explodes (default 1.4).");
        yield return Number("shotRange", "Shooting range", "Distance at which the unit starts to shoot.");
        yield return Number("aimLead", "Aim lead", "How far ahead of a moving player the unit aims (0 = at the player).");
        yield return Number("firstShotDelay", "Delay of the first shot (s)", "Seconds the unit waits before its first shot after it wakes up.");
        yield return new FieldSpec("freeAim", "Free aim", FieldKind.Bool) { Help = "The guns aim at the player on their own instead of pointing where the unit faces." };
        yield return new FieldSpec("homingBullets", "Homing bullets", FieldKind.Bool) { Help = "The bullets steer toward the player after they are fired." };
        yield return new FieldSpec("guns", "Weapons (mounts)", FieldKind.IntList)
        {
            Help = "Indexes of the gun mounts of the unit that shoot (0 = the first). Empty = all of them."
        };
        yield return new FieldSpec("route", "Route", FieldKind.EditableChoice) { Choices = Context.RouteNames, Help = "The route is not used by the server yet." };
        yield return new FieldSpec("target", "Target", FieldKind.Text) { Help = "Name of a point, group or unit type the unit goes for (escort and patrol behaviors)." };

        yield return FieldSpec.Header("Health and speed");
        yield return Number("health", "Health", "Starting health. Empty = the health of the game unit.");
        yield return Number("maxHealth", "Maximum health", "Health cap. Empty = the starting health.");
        yield return Number("healthMultiplier", "Health multiplier", "Multiplies the health of the game unit (2 = double).");
        yield return Number("speedMultiplier", "Speed multiplier", "Multiplies the speed of the unit (2 = twice as fast).");
        yield return Number("turnMultiplier", "Turn multiplier", "Multiplies how fast the unit turns.");
        yield return Number("fireRateMultiplier", "Fire rate multiplier", "Multiplies how often the unit shoots (2 = twice as often).");
        yield return Number("damageMultiple", "Damage multiplier", "Multiplies the damage of its bullets.");
    }

    private static FieldSpec Number(string key, string label, string help = "")
    {
        return new FieldSpec(key, label, FieldKind.OptionalNumber) { Help = help };
    }

    private IEnumerable<FieldSpec> DeathFields()
    {
        yield return FieldSpec.Header("When it dies");
        yield return new FieldSpec("onDeath", "Leaves", FieldKind.EditableChoice)
        {
            Choices = Context.TypeNames,
            Help = "Unit type that appears where this one died (wreckage, reward chest, ring of bullets)."
        };
        yield return new FieldSpec("sequence", "Then comes (sequence)", FieldKind.StringList)
        {
            Help = "Types that are born one after the other when the one before dies, separated by commas."
        };
        yield return Number("durationSeconds", "Lives only (s)", "The unit explodes by itself after this time.");
        yield return new FieldSpec("visualEffect", "Visual effect", FieldKind.Text) { Help = "Name of a statefulEffects of the client." };

        yield return FieldSpec.Header("Summons");
        yield return new FieldSpec("periodicSummon.type", "Summons", FieldKind.EditableChoice) { Choices = Context.TypeNames, Help = "Unit type this unit keeps summoning while it is alive." };
        yield return new FieldSpec("periodicSummon.seconds", "Every (s)", FieldKind.OptionalNumber) { Help = "Seconds between two summons." };
        yield return new FieldSpec("periodicSummon.count", "Amount each time", FieldKind.OptionalNumber) { Help = "How many units appear at each summon." };
        yield return new FieldSpec("periodicSummon.max", "At most alive", FieldKind.OptionalNumber) { Help = "It stops summoning while this many of its summons are alive." };
        yield return new FieldSpec("periodicSummon.belowHealth", "Only below health (0-1)", FieldKind.OptionalNumber) { Help = "It only summons while its health is under this fraction (0.5 = half of the health)." };
        yield return new FieldSpec("periodicSummon.countsForRoomClear", "Summoned units count for the room", FieldKind.Bool) { DefaultBool = true, Help = "On: the room is only cleared when the summoned units are dead too." };

        yield return FieldSpec.Header("Damage chest (Big Treasure)");
        yield return new FieldSpec("dpsChest.seconds", "Seconds to open it", FieldKind.OptionalNumber) { Help = "The chest opens if the players do enough damage to it within this time." };
        yield return new FieldSpec("dpsChest.reward", "Reward", FieldKind.EditableChoice) { Choices = Context.TypeNames, Help = "Unit type that appears (the loot chest) when the chest is beaten in time." };
        yield return new FieldSpec("dpsChest.fail", "If it fails", FieldKind.EditableChoice) { Choices = Context.TypeNames, Help = "Unit type that appears when the time runs out." };

        yield return FieldSpec.Header("Speech (client bark keys)");
        yield return new FieldSpec("barks.onWake", "When it wakes up", FieldKind.Text) { Help = "Key of the speech line (client bark table) the unit says when it wakes up." };
        yield return new FieldSpec("barks.phase", "When the phase changes", FieldKind.Text) { Help = "Key of the speech line the unit says when it changes phase." };
        yield return new FieldSpec("barks.onDeath", "When it dies", FieldKind.Text) { Help = "Key of the speech line the unit says when it dies." };
    }

    private IEnumerable<FieldSpec> PhaseFields()
    {
        yield return new FieldSpec("belowHealth", "Below health (0-1)", FieldKind.OptionalNumber)
        {
            Help = "0.66 = the phase starts when the health goes under 66%."
        };
        yield return new FieldSpec("behavior", "Behavior", FieldKind.Choice) { Choices = () => EditorContext.Behaviors };
        yield return Number("speedMultiplier", "Speed multiplier", "Multiplies the speed of the unit (2 = twice as fast).");
        yield return Number("fireRateMultiplier", "Fire rate multiplier", "Multiplies how often the unit shoots (2 = twice as often).");
        yield return new FieldSpec("guns", "Weapons (mounts)", FieldKind.IntList) { Help = "Indexes of the gun mounts that shoot during this phase. Empty = the ones of the type." };
        yield return new FieldSpec("summon", "Summons", FieldKind.EditableChoice) { Choices = Context.TypeNames, Help = "Unit type summoned when the phase starts." };
        yield return Number("summonCount", "Amount summoned", "How many units are summoned when the phase starts.");
        yield return new FieldSpec("message", "Message", FieldKind.Text) { Help = "Chat message shown when the phase starts." };
        yield return Number("shieldSeconds", "Shield (s)", "The boss takes no damage for this long.");
        yield return new FieldSpec("visualEffect", "Visual effect", FieldKind.Text) { Help = "Name of a client effect (statefulEffects) shown during this phase." };
    }
}
