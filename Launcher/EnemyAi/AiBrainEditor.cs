using System.Text.Json.Nodes;
using SteambirdsAi;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// Editor of one AI (brain): its states, what each state does and the transitions between them.
internal sealed class AiBrainEditor : Panel
{
    private readonly AiStore store;
    private readonly JsonForm info;
    private readonly ListEditor statesList;
    private readonly JsonForm stateForm;
    private readonly JsonForm ownForm;
    private readonly JsonForm effectsForm;
    private readonly AiGraphView graph;
    private readonly Label behaviorInfo;
    private readonly Button newBehaviorButton;
    private readonly Button editBehaviorButton;
    private readonly Label ownNote;
    private readonly ListEditor transitions;
    private readonly JsonForm toForm;
    private readonly ListEditor conditions;
    private readonly JsonForm conditionForm;
    private readonly Label conditionHelp;
    private AiDoc? doc;
    private JsonObject? state;
    private string stateId = "";

    public AiBrainEditor(AiStore store)
    {
        this.store = store;
        Dock = DockStyle.Fill;

        info = new JsonForm(AiFields.BrainInfo(StateIds), () => { }, _ => Changed());

        statesList = new ListEditor(() => AiFields.NewState(StateIds()), AiFields.DescribeState, 112);
        statesList.SelectionChanged += OnStateSelected;

        stateForm = new JsonForm(AiFields.State(BehaviorIds), () => { }, OnStateField);
        behaviorInfo = Ui.Wrapping("", 420, true);
        newBehaviorButton = Ui.Button(L.T("New behavior..."), (_, _) => NewBehavior());
        editBehaviorButton = Ui.Button(L.T("Edit behavior..."), (_, _) => EditBehavior());
        ownForm = new JsonForm(AiFields.Behavior(), () => { }, _ =>
        {
            statesList.RefreshText();
            Changed();
        });
        effectsForm = new JsonForm(AiFields.Effects(AiUnits.TypeNames), () => { }, _ =>
        {
            statesList.RefreshText();
            Changed();
        });
        graph = new AiGraphView();
        graph.StateChosen += id => statesList.SelectById(id);
        graph.Edited += () => Edited?.Invoke();
        ownNote = Ui.Wrapping(L.T("These are the values of the state's own behavior: how it moves, shoots and what happens on contact. They are used (and editable here) while no behavior is picked in the \"Behavior (file)\" field of the State tab. To change a behavior you picked, use \"Edit behavior...\" (one inside this AI) or its file in the list on the left."), 420, true);

        transitions = new ListEditor(() => AiFields.NewTransition(DefaultTarget()), AiFields.DescribeTransition, 100);
        transitions.SelectionChanged += OnTransitionSelected;
        transitions.Edited += () =>
        {
            statesList.RefreshText();
            Changed();
        };
        toForm = new JsonForm(AiFields.Transition(StateIds), () => { }, _ =>
        {
            transitions.RefreshText();
            statesList.RefreshText();
            Changed();
        });
        conditions = new ListEditor(AiFields.NewCondition, AiFields.DescribeCondition, 100);
        conditions.Edited += () =>
        {
            transitions.RefreshText();
            Changed();
        };
        conditionForm = new JsonForm(AiFields.Condition(), () => { }, _ =>
        {
            conditions.RefreshText();
            transitions.RefreshText();
            ShowConditionHelp();
            Changed();
        });
        conditionHelp = Ui.Wrapping("", 420, true);

        // Wired last: these refer to forms that are made after the lists.
        statesList.Edited += () =>
        {
            info.RefreshValues();
            toForm.RefreshValues();
            Changed();
        };
        conditions.SelectionChanged += condition =>
        {
            conditionForm.Bind(condition);
            ShowConditionHelp();
        };

        var detail = new ThemedTabControl { Dock = DockStyle.Fill, Padding = new Point(8, 3) };
        detail.TabPages.Add(StatePage());
        detail.TabPages.Add(OwnPage());
        detail.TabPages.Add(EffectsPage());
        detail.TabPages.Add(TransitionsPage());

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.Controls.Add(Ui.Bold(L.T("States")), 0, 0);
        table.Controls.Add(statesList, 0, 1);
        table.Controls.Add(detail, 0, 2);

        // The list of states with their transitions is what opens by default; the graph is an extra way to see the same AI.
        var views = new ThemedTabControl { Dock = DockStyle.Fill, Padding = new Point(8, 3) };
        var listPage = new TabPage(L.T("States and transitions")) { Padding = new Padding(2) };
        listPage.Controls.Add(table);
        var graphPage = new TabPage(L.T("Graph")) { Padding = new Padding(2) };
        graphPage.Controls.Add(graph);
        var infoPage = new TabPage(L.T("Info")) { Padding = new Padding(2) };
        infoPage.Controls.Add(info);
        views.TabPages.Add(listPage);
        views.TabPages.Add(graphPage);
        views.TabPages.Add(infoPage);
        Controls.Add(views);
    }

    public event Action? Edited;

    // The state the simulator is running now, so the graph can show it ("" = none).
    public void SetRunningState(string id) => graph.SetRunningState(id);

    // ------------------------------------------------------------------ pages

    private TabPage StatePage()
    {
        var page = new TabPage(L.T("State")) { Padding = new Padding(2) };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.Controls.Add(stateForm, 0, 0);
        table.Controls.Add(Ui.WrapFlow(newBehaviorButton, editBehaviorButton), 0, 1);
        table.Controls.Add(behaviorInfo, 0, 2);
        page.Controls.Add(table);
        return page;
    }

    private TabPage OwnPage()
    {
        var page = new TabPage(L.T("Behavior values")) { Padding = new Padding(2) };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.Controls.Add(ownNote, 0, 0);
        table.Controls.Add(ownForm, 0, 1);
        page.Controls.Add(table);
        return page;
    }

    private TabPage EffectsPage()
    {
        var page = new TabPage(L.T("Summons and shield")) { Padding = new Padding(2) };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.Controls.Add(Ui.Wrapping(L.T("What the unit does when it is in this state, besides moving and shooting. Summons and the shield belong to the state, not to a behavior file."), 420, true), 0, 0);
        table.Controls.Add(effectsForm, 0, 1);
        page.Controls.Add(table);
        return page;
    }

    private TabPage TransitionsPage()
    {
        var page = new TabPage(L.T("Transitions")) { Padding = new Padding(2) };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(Control control, int height)
        {
            table.RowStyles.Add(height > 0 ? new RowStyle(SizeType.Absolute, height) : new RowStyle(SizeType.AutoSize));
            control.Dock = DockStyle.Fill;
            table.Controls.Add(control, 0, table.RowCount++);
        }
        table.RowCount = 0;
        Row(Ui.Wrapping(L.T("Checked in order, every moment. The first transition whose conditions are all true changes the state."), 420, true), 0);
        Row(transitions, 96);
        Row(toForm, 34);
        Row(Ui.Bold(L.T("Conditions (all must be true)")), 24);
        Row(conditions, 96);
        Row(conditionForm, 130);
        Row(conditionHelp, 0);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(table);
        page.Controls.Add(scroll);
        return page;
    }

    // ------------------------------------------------------------------ binding

    public void Bind(AiDoc document)
    {
        doc = document;
        info.Bind(document.Root);
        statesList.Bind(Json.ArrOrNull(document.Root, "states"), () => Json.Arr(document.Root, "states"));
        graph.Bind(document.Root);
        graph.SelectState(stateId);
    }

    private IEnumerable<string> StateIds()
    {
        return doc == null ? Enumerable.Empty<string>() : Json.Objects(Json.ArrOrNull(doc.Root, "states")).Select(s => Json.Str(s, "id")).Where(s => s.Length > 0).ToList();
    }

    // An embedded behavior is named "<id of the AI>_<name>", so it can never have the id of a behavior file (the prefix goes away when
    // it is saved in the behaviors folder).
    private string EmbeddedPrefix => (doc == null ? "" : doc.Id) + "_";

    private string WithoutPrefix(string id)
    {
        return id.StartsWith(EmbeddedPrefix, StringComparison.OrdinalIgnoreCase) && id.Length > EmbeddedPrefix.Length ? id.Substring(EmbeddedPrefix.Length) : id;
    }

    // The behaviors a state can pick: the ones inside this AI first, then the files of the behaviors folder.
    private IEnumerable<string> BehaviorIds()
    {
        List<string> ids = EmbeddedIds().ToList();
        foreach (AiDoc file in store.Behaviors)
        {
            if (!ids.Contains(file.Id, StringComparer.OrdinalIgnoreCase)) ids.Add(file.Id);
        }
        return ids;
    }

    private IEnumerable<string> EmbeddedIds()
    {
        return doc == null ? Enumerable.Empty<string>() : Json.Objects(Json.ArrOrNull(doc.Root, "behaviors")).Select(b => Json.Str(b, "id")).Where(i => i.Length > 0).ToList();
    }

    // The behavior of this AI with that id (null when the id is a file of the behaviors folder, or nothing).
    private JsonObject? EmbeddedOf(string id)
    {
        if (doc == null || id.Length == 0) return null;
        return Json.Objects(Json.ArrOrNull(doc.Root, "behaviors")).FirstOrDefault(b => string.Equals(Json.Str(b, "id"), id, StringComparison.OrdinalIgnoreCase));
    }

    private string UniqueBehaviorId(string wanted)
    {
        string name = AiCatalog.CleanId(wanted);
        if (name.Length == 0) name = "new_behavior";
        string id = EmbeddedPrefix + name;
        string candidate = id;
        for (int n = 2; EmbeddedOf(candidate) != null || store.Find(AiKind.Behavior, candidate) != null; n++) candidate = id + "_" + n;
        return candidate;
    }

    // "New behavior...": asks for a name and opens a window with only that behavior. It can stay inside this AI or be saved in the
    // behaviors folder; either way the state that was selected uses it.
    private void NewBehavior()
    {
        if (doc == null || state == null) return;
        string? name = AiPrompt.Ask(FindForm()!, L.T("New behavior"), L.T("Name (the file name is made from it):"), L.T("My behavior"));
        if (string.IsNullOrWhiteSpace(name)) return;

        string id = UniqueBehaviorId(name);
        JsonObject root = AiFields.NewBehavior(name);
        root["format"] = AiCatalog.FormatVersion;
        root["id"] = id;
        using var window = new AiBehaviorWindow(store, new AiDoc(AiKind.Behavior, id, root), true);
        window.ShowDialog(FindForm());
        if (window.Result == AiBehaviorResult.Cancel) return;

        try
        {
            if (window.Result == AiBehaviorResult.Shared)
            {
                // In the behaviors folder it is a behavior of everybody: it goes by its name, without the name of this AI.
                AiDoc saved = store.Add(AiKind.Behavior, WithoutPrefix(id), root, true);
                state["behavior"] = saved.Id;
            }
            else
            {
                Json.Arr(doc.Root, "behaviors").Add(root);
                state["behavior"] = id;
            }
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Ui.ShowError(this, L.F("Could not save the file: {0}", e.Message));
            return;
        }
        AfterBehaviorChange();
    }

    // "Edit behavior...": the behavior of this AI that the state uses, in its own window.
    private void EditBehavior()
    {
        if (doc == null || state == null) return;
        string id = Json.Str(state, "behavior");
        JsonObject? embedded = EmbeddedOf(id);
        if (embedded == null) return;

        using var window = new AiBehaviorWindow(store, new AiDoc(AiKind.Behavior, id, embedded), false);
        window.ShowDialog(FindForm());
        if (window.Result == AiBehaviorResult.Shared)
        {
            try
            {
                Json.Arr(doc.Root, "behaviors").Remove(embedded);
                AiDoc saved = store.Add(AiKind.Behavior, WithoutPrefix(id), embedded, true);
                // Every state of this AI that used it now uses the file.
                foreach (JsonObject other in Json.Objects(Json.ArrOrNull(doc.Root, "states")))
                {
                    if (string.Equals(Json.Str(other, "behavior"), id, StringComparison.OrdinalIgnoreCase)) other["behavior"] = saved.Id;
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Ui.ShowError(this, L.F("Could not save the file: {0}", e.Message));
            }
        }
        if (window.Edited || window.Result == AiBehaviorResult.Shared) AfterBehaviorChange();
    }

    private void AfterBehaviorChange()
    {
        stateForm.Bind(state);
        UpdateOwnEnabled();
        ShowBehaviorInfo();
        statesList.RefreshText();
        Changed();
    }

    private string DefaultTarget()
    {
        List<string> ids = StateIds().ToList();
        return ids.FirstOrDefault(i => i != stateId) ?? stateId;
    }

    private void OnStateSelected(JsonObject? selected)
    {
        state = selected;
        stateId = Json.Str(selected, "id");
        stateForm.Bind(selected);
        ownForm.Bind(selected);
        effectsForm.Bind(selected);
        graph.SelectState(stateId);
        UpdateOwnEnabled();
        transitions.Bind(Json.ArrOrNull(selected, "transitions"), selected == null ? null : () => Json.Arr(selected, "transitions"));
        ShowBehaviorInfo();
    }

    private void OnTransitionSelected(JsonObject? transition)
    {
        toForm.Bind(transition);
        conditions.Bind(Json.ArrOrNull(transition, "when"), transition == null ? null : () => Json.Arr(transition, "when"));
    }

    private void OnStateField(FieldSpec spec)
    {
        if (state == null || doc == null) return;
        if (spec.Key == "id") RenameState();
        UpdateOwnEnabled();
        ShowBehaviorInfo();
        statesList.RefreshText();
        Changed();
    }

    // A state got a new name: the transitions that go to it and the "first state" follow.
    private void RenameState()
    {
        if (state == null || doc == null) return;
        string newId = Json.Str(state, "id").Trim();
        bool taken = Json.Objects(Json.ArrOrNull(doc.Root, "states")).Any(s => !ReferenceEquals(s, state) && Json.Str(s, "id") == newId);
        if (newId.Length == 0 || taken)
        {
            Ui.ShowError(this, newId.Length == 0 ? L.T("A state needs a name.") : L.F("There is already a state called \"{0}\".", newId));
            state["id"] = stateId;
            stateForm.RefreshValues();
            return;
        }
        state["id"] = newId;
        foreach (JsonObject other in Json.Objects(Json.ArrOrNull(doc.Root, "states")))
        {
            foreach (JsonObject transition in Json.Objects(Json.ArrOrNull(other, "transitions")))
            {
                if (Json.Str(transition, "to") == stateId) transition["to"] = newId;
            }
        }
        if (Json.Str(doc.Root, "initial") == stateId) doc.Root["initial"] = newId;
        stateId = newId;
        info.RefreshValues();
        toForm.RefreshValues();
        transitions.RefreshText();
    }

    private void UpdateOwnEnabled()
    {
        bool own = state != null && Json.Str(state, "behavior").Length == 0;
        ownForm.SetEnabled(own);
        ownNote.Visible = true;
    }

    private void ShowBehaviorInfo()
    {
        newBehaviorButton.Enabled = state != null;
        editBehaviorButton.Enabled = state != null && EmbeddedOf(Json.Str(state, "behavior")) != null;
        if (state == null)
        {
            behaviorInfo.Text = "";
            return;
        }
        string id = Json.Str(state, "behavior");
        if (id.Length == 0)
        {
            behaviorInfo.Text = L.T("This state has its own behavior (Behavior values tab).");
            return;
        }
        JsonObject? embedded = EmbeddedOf(id);
        if (embedded != null)
        {
            string embeddedMode = Json.Str(Json.ObjOrNull(embedded, "move"), "mode");
            behaviorInfo.Text = L.T("Inside this AI") + ": " + (Json.Str(embedded, "name").Length > 0 ? Json.Str(embedded, "name") : id) + "  (" + embeddedMode + ")\n" + Json.Str(embedded, "description");
            return;
        }
        AiDoc? behavior = store.Find(AiKind.Behavior, id);
        if (behavior == null)
        {
            behaviorInfo.Text = L.F("The behavior \"{0}\" does not exist in the behaviors folder.", id);
            return;
        }
        string mode = Json.Str(Json.ObjOrNull(behavior.Root, "move"), "mode");
        behaviorInfo.Text = behavior.DisplayName + "  (" + mode + ")\n" + Json.Str(behavior.Root, "description");
    }

    private void ShowConditionHelp()
    {
        string type = Json.Str(conditions.Current, "type");
        AiOption? option = AiCatalog.ConditionTypes.FirstOrDefault(o => o.Id == type);
        conditionHelp.Text = option == null ? "" : L.T(option.Help);
    }

    private void Changed()
    {
        graph.RedrawFromFile();
        Edited?.Invoke();
    }
}
