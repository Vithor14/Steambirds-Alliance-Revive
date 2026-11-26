using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.Editor;

// "Unit types" page: the enemies of the map ("types"). A type is a game unit (template) plus the changes this map makes:
// behavior, health, weapons, AI, what it summons, what it leaves when it dies... (Boss phases, shields and summons by
// health are states of an AI file now: the Enemy AI tab.)
internal sealed class UnitTypesPage : EditorPage
{
    private readonly CollectionPanel types;
    private readonly JsonForm combatForm;
    private readonly JsonForm deathForm;
    private readonly JsonForm aiForm;
    private readonly Button aiButton;
    private readonly Label aiState;
    private readonly ThemedTabControl inner;

    public UnitTypesPage(EditorContext context) : base(context)
    {
        combatForm = new JsonForm(CombatFields(), Session.Checkpoint, TabFieldChanged);
        deathForm = new JsonForm(DeathFields(), Session.Checkpoint, TabFieldChanged);

        aiState = Ui.Wrapping("", 400, true);
        aiButton = Ui.Primary(L.T("Load unit in the AI editor"), (_, _) => EditAi());
        Context.Tip.SetToolTip(aiButton, L.T("Goes to the Enemy AI tab with this unit type loaded in the simulator (its game unit, speed, fire rate and AI), ready to edit."));
        aiForm = new JsonForm(AiFieldSpecs(), Session.Checkpoint, TabFieldChanged);
        // One field in a row of its own: a scroll bar here would only be in the way.
        aiForm.AutoScroll = false;
        TabPage aiPage = BuildAiPage();

        TabPage combatPage = new TabPage(L.T("Combat")) { Padding = new Padding(2) };
        combatPage.Controls.Add(combatForm);
        TabPage deathPage = new TabPage(L.T("Death and summons")) { Padding = new Padding(2) };
        deathPage.Controls.Add(deathForm);

        inner = new ThemedTabControl { Dock = DockStyle.Fill, Padding = new Point(8, 3) };
        inner.TabPages.AddRange(new[] { combatPage, aiPage, deathPage });

        types = new CollectionPanel(Session,
            new ObjectDictionarySource(
                () => Json.ObjOrNull(Document.Root, "types"),
                () => Json.Obj(Document.Root, "types"),
                NewType,
                (oldName, newName) => MapRefactor.Rename(Document, NameKind.UnitType, oldName, newName)),
            GeneralFields(), "unit", listHeight: 110, extra: inner, formHeight: 150,
            picture: item => Context.PictureOfType(item.Key));

        types.ItemSelected += item =>
        {
            combatForm.Bind(item?.Data);
            deathForm.Bind(item?.Data);
            aiForm.Bind(item?.Data);
            UpdateAiState();
        };
        types.ListChanged += () => Canvas.Invalidate();

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

    private TabPage BuildAiPage()
    {
        var page = new TabPage(L.T("Enemy AI")) { Padding = new Padding(6) };
        Label title = Ui.Bold(L.T("Enemy AI"));
        Label text = Ui.Wrapping(
            L.T("How this unit moves, when it shoots and when it blows up is an AI made in the Enemy AI tab of the launcher. The AI lives in its own file (server\\ai\\brains), not in the map, so the same one can serve many maps and can be shared. Pick it here and use the button to edit it and test it on this unit."),
            400, true);
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(title, 0, 0);
        table.Controls.Add(text, 0, 1);
        table.Controls.Add(aiForm, 0, 2);
        table.Controls.Add(aiButton, 0, 3);
        table.Controls.Add(aiState, 0, 4);
        page.Controls.Add(table);
        return page;
    }

    private IEnumerable<FieldSpec> AiFieldSpecs()
    {
        yield return new FieldSpec(EnemyAiEditors.AiField, "AI (file)", FieldKind.EditableChoice)
        {
            Choices = () => EnemyAiEditors.Current?.ApplicableBrainIds(EnemyAiEditors.UnitNamesOf(CurrentType)) ?? Enumerable.Empty<string>(),
            Help = "Id of an AI file of the server's ai\\brains folder. The list shows the AIs made for this unit first, then the generic ones; you can also type another id. Empty = the unit gets the generic chase AI."
        };
    }

    private void UpdateAiState()
    {
        JsonObject? type = CurrentType;
        aiButton.Enabled = EnemyAiEditors.Available && type != null;
        if (type == null)
        {
            aiState.Text = "";
            return;
        }
        string behavior = Json.Str(type, "behavior");
        aiState.Text = EnemyAiEditors.HasAi(type)
            ? L.F("This type uses the AI \"{0}\".", Json.Str(type, EnemyAiEditors.AiField))
            : behavior.Length > 0
                ? L.F("This type has no AI. It keeps the built-in behavior \"{0}\" of the map (not edited here). Pick an AI above, or open the AI editor, to control how it moves and shoots.", behavior)
                : L.T("This type has no AI. Pick one above, or open the AI editor, to control how it moves and shoots.");
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

    // A new type: when its name is a unit of the game it is made from that unit and takes the name the game shows.
    private static JsonObject NewType(string name)
    {
        bool known = EditorAssets.Current.FindUnit(name) != null;
        string shown = known ? EditorAssets.UnitDisplayName(name) : "";
        return new JsonObject
        {
            ["name"] = shown.Length > 0 ? shown : name,
            ["template"] = known ? name : "",
            [EnemyAiEditors.AiField] = EnemyAiEditors.DefaultAi
        };
    }

    // The game unit changed: the display name follows it, unless the user wrote a name of their own (a name is
    // "not their own" when it is empty, the key of the type, or what the game called the old unit).
    private void FollowTemplate(JsonObject type, string oldTemplate, string newTemplate)
    {
        string shown = EditorAssets.UnitDisplayName(newTemplate);
        if (shown.Length == 0) return;
        string current = Json.Str(type, "name");
        string key = Json.ObjectProperties(Json.ObjOrNull(Document.Root, "types")).FirstOrDefault(p => ReferenceEquals(p.Value, type)).Key ?? "";
        bool ownName = current.Length > 0 && current != key && current != oldTemplate && current != EditorAssets.UnitDisplayName(oldTemplate);
        if (!ownName) type["name"] = shown;
    }

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
            OnChanged = FollowTemplate,
            Picture = name => EditorAssets.Current.PictureOfUnit(name),
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
        yield return new FieldSpec("shieldedLook", "Shielded glow", FieldKind.Bool) { Help = "While invulnerable, the unit shows the game's pink shielded glow (a boss that cannot be hurt yet). Off for walls and scenery." };
        yield return new FieldSpec("opensAfterHitSeconds", "Wakes up when hit", FieldKind.Bool) { Help = "The unit stays idle until a shot hits it, then it wakes up and fights." };
        yield return new FieldSpec("freeRoam", "Free (no room)", FieldKind.Bool) { Help = "The unit does not belong to a room: it is born at the start and the room logic (waking up, clearing) ignores it." };
        yield return new FieldSpec("excludeFromRoomClear", "Does not count to clear the room", FieldKind.Bool)
        {
            Help = "The unit is never needed to clear the room, even when it has weapons."
        };

        // How the unit moves, when it shoots, which guns and when it blows up are made in the Enemy AI editor (the "Enemy AI"
        // tab of this page opens it). What stays here is what an AI file does not say.
        yield return FieldSpec.Header("Shooting");
        yield return Number("firstShotDelay", "Delay of the first shot (s)", "Seconds the unit waits before its first shot after it wakes up.");
        yield return new FieldSpec("freeAim", "Free aim", FieldKind.Bool) { Help = "The guns aim at the player on their own instead of pointing where the unit faces." };
        yield return new FieldSpec("homingBullets", "Homing bullets", FieldKind.Bool) { Help = "The bullets steer toward the player after they are fired." };

        yield return FieldSpec.Header("Escort and patrol (built-in behaviors)");
        yield return new FieldSpec("route", "Route", FieldKind.EditableChoice) { Choices = Context.RouteNames, Help = "The route is not used by the server yet." };
        yield return new FieldSpec("target", "Target", FieldKind.Text) { Help = "Name of a point, group or unit type the unit goes for (the built-in escort and patrol behaviors of the map; an AI has no escort or patrol mode)." };

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
            Choices = Context.TypeNames, Picture = Context.PictureOfType,
            Help = "Unit type that appears where this one died (wreckage, reward chest, ring of bullets)."
        };
        yield return new FieldSpec("sequence", "Then comes (sequence)", FieldKind.StringList)
        {
            Help = "Types that are born one after the other when the one before dies, separated by commas."
        };
        yield return Number("durationSeconds", "Lives only (s)", "The unit explodes by itself after this time.");
        yield return new FieldSpec("visualEffect", "Visual effect", FieldKind.Choice)
        {
            Choices = EditorAssets.EffectChoices,
            Help = "Effect of the client the unit shows from the start (a shield, a detection ring around a mine...). Empty = none."
        };

        yield return FieldSpec.Header("Summons");
        yield return new FieldSpec("periodicSummon.type", "Summons", FieldKind.EditableChoice) { Choices = Context.TypeNames, Picture = Context.PictureOfType, Help = "Unit type this unit keeps summoning while it is alive." };
        yield return new FieldSpec("periodicSummon.seconds", "Every (s)", FieldKind.OptionalNumber) { Help = "Seconds between two summons." };
        yield return new FieldSpec("periodicSummon.count", "Amount each time", FieldKind.OptionalNumber) { Help = "How many units appear at each summon." };
        yield return new FieldSpec("periodicSummon.max", "At most alive", FieldKind.OptionalNumber) { Help = "It stops summoning while this many of its summons are alive." };
        yield return new FieldSpec("periodicSummon.belowHealth", "Only below health (0-1)", FieldKind.OptionalNumber) { Help = "It only summons while its health is under this fraction (0.5 = half of the health)." };
        yield return new FieldSpec("periodicSummon.countsForRoomClear", "Summoned units count for the room", FieldKind.Bool) { DefaultBool = true, Help = "On: the room is only cleared when the summoned units are dead too." };

        yield return FieldSpec.Header("Damage chest (Big Treasure)");
        yield return new FieldSpec("dpsChest.seconds", "Seconds to open it", FieldKind.OptionalNumber) { Help = "The chest opens if the players do enough damage to it within this time." };
        yield return new FieldSpec("dpsChest.reward", "Reward", FieldKind.EditableChoice) { Choices = Context.TypeNames, Picture = Context.PictureOfType, Help = "Unit type that appears (the loot chest) when the chest is beaten in time." };
        yield return new FieldSpec("dpsChest.fail", "If it fails", FieldKind.EditableChoice) { Choices = Context.TypeNames, Picture = Context.PictureOfType, Help = "Unit type that appears when the time runs out." };

        yield return FieldSpec.Header("Speech (client bark keys)");
        yield return new FieldSpec("barks.onWake", "When it wakes up", FieldKind.Text) { Help = "Key of the speech line (client bark table) the unit says when it wakes up." };
        yield return new FieldSpec("barks.onDeath", "When it dies", FieldKind.Text) { Help = "Key of the speech line the unit says when it dies." };
    }
}
