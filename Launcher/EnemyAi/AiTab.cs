using System.Text.Json.Nodes;
using SteambirdsAi;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// The "Enemy AI" tab: the list of AI files (left), the editor of the selected one (middle) and the simulator (right).
// Every AI and every behavior is its own .json file in the server's ai folder, so they can be copied, shared and loaded
// as needed; the server reads them again by itself when they change.
internal sealed class AiTab : Panel
{
    private readonly LauncherContext context;
    private readonly AiStore store;
    private readonly TreeView tree;
    private readonly TreeNode brainsNode;
    private readonly TreeNode behaviorsNode;
    private readonly AiBrainEditor brainEditor;
    private readonly AiBehaviorEditor behaviorEditor;
    private readonly Label placeholder;
    private readonly TextBox problems;
    private readonly AiSimView simView;
    private readonly Label idHint;
    private readonly SplitContainer outer;
    private readonly SplitContainer inner;
    private readonly Dictionary<AiDoc, TreeNode> nodes = new Dictionary<AiDoc, TreeNode>();
    private readonly Button duplicateButton;
    private readonly Button renameButton;
    private readonly Button deleteButton;
    private readonly Button saveButton;
    private readonly Button saveAllButton;
    private readonly Button revertButton;
    private readonly Button exportButton;
    private AiDoc? current;
    private bool placed;
    private bool refreshing;

    public AiTab(LauncherContext context, AiStore store)
    {
        this.context = context;
        this.store = store;
        Dock = DockStyle.Fill;

        if (EditorAssets.Current.Folder.Length == 0) EditorAssets.Load(context.GameFolder);

        // ---- toolbar
        saveButton = Ui.Button(L.T("Save"), (_, _) => SaveCurrent());
        saveAllButton = Ui.Button(L.T("Save all"), (_, _) => SaveAll());
        revertButton = Ui.Button(L.T("Revert"), (_, _) => Revert());
        duplicateButton = Ui.Button(L.T("Duplicate"), (_, _) => DuplicateCurrent());
        renameButton = Ui.Button(L.T("Rename..."), (_, _) => RenameCurrent());
        deleteButton = Ui.Button(L.T("Delete"), (_, _) => DeleteCurrent());
        exportButton = Ui.Button(L.T("Export..."), (_, _) => ExportCurrent());
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
        toolbar.Controls.AddRange(new Control[]
        {
            Ui.Button(L.T("New AI"), (_, _) => NewDoc(AiKind.Brain)),
            Ui.Button(L.T("New behavior"), (_, _) => NewDoc(AiKind.Behavior)),
            duplicateButton, renameButton, deleteButton,
            saveButton, saveAllButton, revertButton,
            Ui.Button(L.T("Import..."), (_, _) => ImportFile()), exportButton,
            Ui.Button(L.T("Open folder"), (_, _) => OpenFolder()),
            Ui.Button(L.T("Reload"), (_, _) => ReloadFromDisk())
        });
        context.ToolTip.SetToolTip(exportButton, L.T("Writes one file with this AI and the behaviors it uses, to send to someone else (Import... reads it)."));

        // ---- left: the files
        tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true };
        brainsNode = new TreeNode(L.T("AIs"));
        behaviorsNode = new TreeNode(L.T("Behaviors"));
        tree.Nodes.Add(brainsNode);
        tree.Nodes.Add(behaviorsNode);
        tree.AfterSelect += (_, e) =>
        {
            if (!refreshing) SelectDoc(e.Node?.Tag as AiDoc);
        };
        idHint = Ui.Wrapping("", 210, true);
        idHint.Dock = DockStyle.Bottom;
        Button copyId = Ui.Button(L.T("Copy id"), (_, _) =>
        {
            if (current != null) Clipboard.SetText(current.Id);
        });
        copyId.Dock = DockStyle.Bottom;
        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(tree);
        left.Controls.Add(copyId);
        left.Controls.Add(idHint);

        // ---- middle: the editors
        brainEditor = new AiBrainEditor(store) { Visible = false };
        behaviorEditor = new AiBehaviorEditor(store) { Visible = false };
        placeholder = Ui.Wrapping(
            L.T("Pick an AI or a behavior on the left, or make a new one.\n\nAn AI is a set of states (for example: wait, chase, retreat). Each state acts like a behavior and has transitions that say when to change to another state. A behavior is one way of acting: how to move and shoot.\n\nThe simulator on the right shows what you build, with the art and the guns of any game unit."),
            400, true);
        placeholder.Padding = new Padding(10);
        brainEditor.Edited += OnEdited;
        behaviorEditor.Edited += OnEdited;

        problems = new TextBox
        {
            Dock = DockStyle.Bottom, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Height = 70, Font = new Font(FontFamily.GenericMonospace, 8.5f)
        };
        var middle = new Panel { Dock = DockStyle.Fill };
        middle.Controls.Add(brainEditor);
        middle.Controls.Add(behaviorEditor);
        middle.Controls.Add(placeholder);
        middle.Controls.Add(problems);

        // ---- right: the simulator
        simView = new AiSimView(context);

        inner = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 5 };
        inner.Panel1.Controls.Add(middle);
        inner.Panel2.Controls.Add(simView);
        outer = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 5 };
        outer.Panel1.Controls.Add(left);
        outer.Panel2.Controls.Add(inner);

        Controls.Add(outer);
        Controls.Add(toolbar);

        store.Changed += RefreshTree;
        context.GameFolderChanged += OnGameFolderChanged;
        SizeChanged += (_, _) => PlaceSplitters();
        RefreshTree();
        SelectDoc(null);
    }

    // The unit types page and the map checks ask the tab to show an AI.
    public bool OpenBrain(string? id)
    {
        AiDoc? doc = string.IsNullOrWhiteSpace(id) ? null : store.Find(AiKind.Brain, id.Trim());
        if (doc == null) return false;
        SelectNode(doc);
        return true;
    }

    // True when nothing is lost: nothing is unsaved, or the user chose to save / discard.
    public bool ConfirmDiscard()
    {
        if (!store.AnyDirty) return true;
        DialogResult answer = MessageBox.Show(this,
            L.T("Some AI files were changed and not saved.\n\nSave them before closing?"), LauncherInfo.Title,
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return false;
        if (answer == DialogResult.Yes) return TrySaveAll();
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            store.Changed -= RefreshTree;
            context.GameFolderChanged -= OnGameFolderChanged;
        }
        base.Dispose(disposing);
    }

    private void OnGameFolderChanged()
    {
        EditorAssets.Load(context.GameFolder);
        simView.ReloadUnits();
    }

    // Widths are set once the tab has its real size (a SplitContainer rejects values while it is still 150 px wide).
    private void PlaceSplitters()
    {
        if (placed || Width < 700) return;
        try
        {
            outer.SplitterDistance = Math.Clamp(Width * 17 / 100, 170, 260);
            inner.SplitterDistance = Math.Max(300, inner.Width * 48 / 100);
            placed = true;
        }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
        {
            // Tried too early: it is tried again on the next resize.
        }
    }

    // ------------------------------------------------------------------ tree

    private void RefreshTree()
    {
        refreshing = true;
        try
        {
            AiDoc? keep = current;
            nodes.Clear();
            brainsNode.Nodes.Clear();
            behaviorsNode.Nodes.Clear();
            foreach (AiDoc doc in store.Brains) AddNode(brainsNode, doc);
            foreach (AiDoc doc in store.Behaviors) AddNode(behaviorsNode, doc);
            brainsNode.Expand();
            behaviorsNode.Expand();
            if (keep != null && nodes.TryGetValue(keep, out TreeNode? node)) tree.SelectedNode = node;
        }
        finally
        {
            refreshing = false;
        }
        UpdateButtons();
    }

    private void AddNode(TreeNode parent, AiDoc doc)
    {
        var node = new TreeNode(doc.Title) { Tag = doc };
        if (doc.LoadError != null) node.ForeColor = Theme.ColorOf(TextRole.Bad);
        node.ToolTipText = doc.LoadError ?? doc.DisplayName;
        parent.Nodes.Add(node);
        nodes[doc] = node;
    }

    private void UpdateNode(AiDoc doc)
    {
        if (nodes.TryGetValue(doc, out TreeNode? node)) node.Text = doc.Title;
    }

    private void SelectNode(AiDoc doc)
    {
        if (nodes.TryGetValue(doc, out TreeNode? node)) tree.SelectedNode = node;
        else SelectDoc(doc);
    }

    // ------------------------------------------------------------------ selection

    private void SelectDoc(AiDoc? doc)
    {
        current = doc;
        bool editable = doc != null && doc.LoadError == null;
        brainEditor.Visible = editable && doc!.Kind == AiKind.Brain;
        behaviorEditor.Visible = editable && doc!.Kind == AiKind.Behavior;
        placeholder.Visible = !editable;
        if (doc != null && doc.LoadError != null)
            placeholder.Text = L.F("This file could not be read, so it cannot be edited here:\n\n{0}\n\nFix it with a text editor (the file is in the ai folder) and press Reload.", doc.LoadError);
        else if (doc == null)
            placeholder.Text = L.T("Pick an AI or a behavior on the left, or make a new one.\n\nAn AI is a set of states (for example: wait, chase, retreat). Each state acts like a behavior and has transitions that say when to change to another state. A behavior is one way of acting: how to move and shoot.\n\nThe simulator on the right shows what you build, with the art and the guns of any game unit.");

        if (editable)
        {
            if (doc!.Kind == AiKind.Brain) brainEditor.Bind(doc);
            else behaviorEditor.Bind(doc);
        }
        idHint.Text = doc == null
            ? ""
            : doc.Kind == AiKind.Brain
                ? L.F("To use it: write \"ai\": \"{0}\" in a unit type (map editor, Units, Enemy AI) or in enemies.json.", doc.Id)
                : L.F("A behavior is used by the states of an AI: pick \"{0}\" in the \"Behavior (file)\" field of a state.", doc.Id);
        UpdateButtons();
        Simulate();
    }

    private void UpdateButtons()
    {
        bool has = current != null;
        bool editable = has && current!.LoadError == null;
        duplicateButton.Enabled = editable;
        renameButton.Enabled = has;
        deleteButton.Enabled = has;
        saveButton.Enabled = editable && (current!.Dirty || current.IsNew);
        revertButton.Enabled = editable && current!.Dirty && !current.IsNew;
        exportButton.Enabled = editable;
        saveAllButton.Enabled = store.AnyDirty;
    }

    private void OnEdited()
    {
        if (current == null) return;
        current.Dirty = true;
        UpdateNode(current);
        UpdateButtons();
        Simulate();
    }

    // Compiles what is open and shows the problems; the simulator starts again with it.
    private void Simulate()
    {
        var found = new List<AiProblem>();
        AiBrain? brain = current == null ? null : store.Compile(current, found);
        ShowProblems(found);
        simView.SetBrain(brain, current == null ? "" : current.DisplayName);
    }

    private void ShowProblems(List<AiProblem> found)
    {
        if (current == null)
        {
            problems.Text = "";
            return;
        }
        bool errors = found.Any(p => p.Error);
        problems.Text = found.Count == 0
            ? L.T("No problems found.")
            : string.Join("\r\n", found.Select(p => (p.Error ? L.T("ERROR") : L.T("warning")) + ": " + p.Where + " - " + p.Text));
        Ui.SetRole(problems, found.Count == 0 ? TextRole.Good : errors ? TextRole.Bad : TextRole.Warn);
    }

    // ------------------------------------------------------------------ actions

    private void NewDoc(AiKind kind)
    {
        string? name = AiPrompt.Ask(this, kind == AiKind.Brain ? L.T("New AI") : L.T("New behavior"),
            L.T("Name (the file name is made from it):"), kind == AiKind.Brain ? L.T("My AI") : L.T("My behavior"));
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            JsonObject root = kind == AiKind.Brain ? AiFields.NewBrain(name) : AiFields.NewBehavior(name);
            AiDoc doc = store.Add(kind, name, root, true);
            SelectNode(doc);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Ui.ShowError(this, L.F("Could not save the file: {0}", e.Message));
        }
    }

    private void DuplicateCurrent()
    {
        if (current == null) return;
        AiDoc copy = store.Duplicate(current);
        SelectNode(copy);
    }

    private void RenameCurrent()
    {
        if (current == null) return;
        string? id = AiPrompt.Ask(this, L.T("Rename"), L.T("New id (lowercase letters, digits and _):"), current.Id);
        if (string.IsNullOrWhiteSpace(id)) return;
        try
        {
            store.Rename(current, id);
            SelectNode(current);
            UpdateButtons();
        }
        catch (Exception e) when (e is InvalidOperationException || e is IOException)
        {
            Ui.ShowError(this, e.Message);
        }
    }

    private void DeleteCurrent()
    {
        if (current == null) return;
        string extra = current.Kind == AiKind.Behavior && store.UsedBy(current).Any()
            ? "\n\n" + L.F("These AIs use it and will have a state with a missing behavior: {0}", string.Join(", ", store.UsedBy(current)))
            : "";
        if (!Ui.Confirm(this, L.F("Delete \"{0}\"?\n\nThe file goes to server\\_backup, it is not lost for good.", current.Id) + extra)) return;
        try
        {
            AiDoc doc = current;
            current = null;
            store.Delete(doc);
            SelectDoc(null);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Ui.ShowError(this, L.F("Could not delete the file: {0}", e.Message));
        }
    }

    private void SaveCurrent()
    {
        if (current == null) return;
        try
        {
            store.Save(current);
            UpdateNode(current);
            UpdateButtons();
            Simulate();
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
        {
            Ui.ShowError(this, L.F("Could not save the file: {0}", e.Message));
        }
    }

    private void SaveAll()
    {
        TrySaveAll();
        UpdateButtons();
    }

    private bool TrySaveAll()
    {
        try
        {
            store.SaveAll();
            foreach (AiDoc doc in store.Docs) UpdateNode(doc);
            return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Ui.ShowError(this, L.F("Could not save the file: {0}", e.Message));
            return false;
        }
    }

    // Throws away the changes of the open file and reads it from the disk again.
    private void Revert()
    {
        if (current == null || current.IsNew) return;
        if (!Ui.Confirm(this, L.F("Throw away the changes of \"{0}\" and read it from the disk again?", current.Id))) return;
        AiKind kind = current.Kind;
        string id = current.Id;
        current.Dirty = false;
        current = null;
        store.Reload();
        AiDoc? again = store.Find(kind, id);
        if (again != null) SelectNode(again);
        else SelectDoc(null);
    }

    private void ReloadFromDisk()
    {
        string id = current?.Id ?? "";
        AiKind kind = current?.Kind ?? AiKind.Brain;
        store.Reload();
        AiDoc? again = id.Length > 0 ? store.Find(kind, id) : null;
        if (again != null) SelectNode(again);
        if (store.AnyDirty) Ui.ShowInfo(this, L.T("Files with unsaved changes were kept as they are in the launcher."));
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(store.BrainsFolder);
            Directory.CreateDirectory(store.BehaviorsFolder);
        }
        catch (IOException)
        {
            // Opening the folder reports it.
        }
        Ui.OpenFolder(this, store.Folder);
    }

    private void ExportCurrent()
    {
        if (current == null) return;
        using var dialog = new SaveFileDialog
        {
            Title = L.T("Export"),
            Filter = L.T("AI files") + " (*.json)|*.json",
            FileName = current.Id + ".ai.json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            store.Export(current, dialog.FileName);
            int behaviors = current.Kind == AiKind.Brain ? AiPackage.UsedBehaviors(current.Root).Count(id => store.Find(AiKind.Behavior, id) != null) : 0;
            Ui.ShowInfo(this, L.F("Saved {0}\n\nIt holds \"{1}\"{2}.", dialog.FileName, current.Id,
                behaviors > 0 ? " " + L.F("and {0} behavior(s) it uses", behaviors) : ""));
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Ui.ShowError(this, L.F("Could not save the file: {0}", e.Message));
        }
    }

    private void ImportFile()
    {
        using var dialog = new OpenFileDialog { Title = L.T("Import"), Filter = L.T("AI files") + " (*.json)|*.json|" + L.T("All files") + "|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            AiPackage.Contents contents = AiStore.ReadShared(dialog.FileName);
            string what = string.Join(", ", contents.Brains.Select(b => b.Id).Concat(contents.Behaviors.Select(b => b.Id)));
            if (!Ui.Confirm(this, L.F("Add {0} AI(s) and {1} behavior(s) to your files?\n\n{2}\n\nNothing of yours is overwritten: a file with the same id gets a new one.",
                    contents.Brains.Count, contents.Behaviors.Count, what))) return;
            List<AiDoc> added = store.Import(contents);
            AiDoc? first = added.FirstOrDefault(d => d.Kind == AiKind.Brain) ?? added.FirstOrDefault();
            if (first != null) SelectNode(first);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException || e is IOException || e is UnauthorizedAccessException)
        {
            Ui.ShowError(this, L.F("Could not read the file: {0}", e.Message));
        }
    }
}

// What the map editor uses to reach the Enemy AI tab: the "ai" field of a unit type names an AI file.
internal sealed class AiEditorBridge : IEnemyAiEditor
{
    private readonly AiStore store;
    private readonly Action<string?> open;

    public AiEditorBridge(AiStore store, Action<string?> open)
    {
        this.store = store;
        this.open = open;
    }

    public string DisplayName => L.T("Enemy AI");

    public IEnumerable<string> BrainIds() => store.Brains.Select(b => b.Id).ToList();

    public void Edit(IWin32Window owner, string typeName, JsonObject unitType, Action beforeChange, Action afterChange)
    {
        open(Json.Str(unitType, EnemyAiEditors.AiField));
    }

    public IEnumerable<string> Validate(string typeName, JsonObject unitType)
    {
        string id = Json.Str(unitType, EnemyAiEditors.AiField).Trim();
        if (id.Length > 0 && store.Find(AiKind.Brain, id) == null)
            yield return L.F("The unit type \"{0}\" uses the AI \"{1}\", which is not in the ai folder (the server will use its normal behavior).", typeName, id);
    }
}
