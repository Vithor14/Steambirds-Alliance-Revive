using System.Text.Json.Nodes;
using SteambirdsAi;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;

namespace SteambirdsLauncher.EnemyAi;

internal enum AiKind { Brain, Behavior }

// One AI file in memory: the JSON tree the editor changes (so fields it does not know are kept when it saves).
internal sealed class AiDoc
{
    public AiDoc(AiKind kind, string id, JsonObject root)
    {
        Kind = kind;
        Id = id;
        Root = root;
    }

    public AiKind Kind { get; }
    public string Id { get; set; }
    public JsonObject Root { get; }
    public bool Dirty { get; set; }

    // The file is not in the folder yet.
    public bool IsNew { get; set; }

    // The file could not be read: it is listed (so it is not forgotten) but cannot be edited.
    public string? LoadError { get; set; }

    public string Title => (Dirty ? "* " : "") + Id;

    public string DisplayName => Json.Str(Root, "name").Length > 0 ? Json.Str(Root, "name") : Id;
}

// The AI files of the server folder (ai\behaviors, ai\brains) as editable documents: load, save, create, rename,
// delete (to the backup folder), import and export (one file, or a package with a brain and the behaviors it needs).
internal sealed class AiStore
{
    private readonly List<AiDoc> docs = new List<AiDoc>();

    public AiStore(string folder)
    {
        Folder = folder;
    }

    public string Folder { get; }
    public IReadOnlyList<AiDoc> Docs => docs;
    public event Action? Changed;

    public string BehaviorsFolder => AiLibrary.BehaviorsPath(Folder);
    public string BrainsFolder => AiLibrary.BrainsPath(Folder);

    public string PathOf(AiDoc doc) => System.IO.Path.Combine(doc.Kind == AiKind.Brain ? BrainsFolder : BehaviorsFolder, doc.Id + ".json");

    public IEnumerable<AiDoc> Brains => docs.Where(d => d.Kind == AiKind.Brain).OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase);
    public IEnumerable<AiDoc> Behaviors => docs.Where(d => d.Kind == AiKind.Behavior).OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase);
    public bool AnyDirty => docs.Any(d => d.Dirty);

    public AiDoc? Find(AiKind kind, string id)
    {
        return docs.FirstOrDefault(d => d.Kind == kind && string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public void RaiseChanged() => Changed?.Invoke();

    // ------------------------------------------------------------------ load and save

    // Reads the folder again. Documents with unsaved changes are kept as they are.
    public void Reload()
    {
        List<AiDoc> keep = docs.Where(d => d.Dirty || d.IsNew).ToList();
        docs.Clear();
        Read(AiKind.Behavior, BehaviorsFolder);
        Read(AiKind.Brain, BrainsFolder);
        foreach (AiDoc doc in keep)
        {
            docs.RemoveAll(d => d.Kind == doc.Kind && string.Equals(d.Id, doc.Id, StringComparison.OrdinalIgnoreCase));
            docs.Add(doc);
        }
        Changed?.Invoke();
    }

    private void Read(AiKind kind, string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (string path in Directory.EnumerateFiles(folder, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string id = AiCatalog.CleanId(System.IO.Path.GetFileNameWithoutExtension(path));
            if (id.Length == 0) continue;
            try
            {
                JsonNode? node = JsonNode.Parse(TextFiles.ReadShared(path), null,
                    new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (node is not JsonObject root) throw new System.Text.Json.JsonException("not a JSON object");
                docs.Add(new AiDoc(kind, id, root));
            }
            catch (Exception e)
            {
                docs.Add(new AiDoc(kind, id, new JsonObject()) { LoadError = e.Message });
            }
        }
    }

    public void Save(AiDoc doc)
    {
        if (doc.LoadError != null) throw new InvalidOperationException(doc.LoadError);
        doc.Root["format"] = AiCatalog.FormatVersion;
        doc.Root["id"] = doc.Id;
        string path = PathOf(doc);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        TextFiles.WriteAtomic(path, CompactJson.Write(doc.Root), false);
        doc.Dirty = false;
        doc.IsNew = false;
        Changed?.Invoke();
    }

    public void SaveAll()
    {
        foreach (AiDoc doc in docs.Where(d => d.Dirty || d.IsNew).ToList())
        {
            if (doc.LoadError == null) Save(doc);
        }
    }

    // ------------------------------------------------------------------ create, rename, delete

    public string UniqueId(AiKind kind, string wanted)
    {
        string id = AiCatalog.CleanId(wanted);
        if (id.Length == 0) id = kind == AiKind.Brain ? "new_ai" : "new_behavior";
        string candidate = id;
        for (int n = 2; Find(kind, candidate) != null; n++) candidate = id + "_" + n;
        return candidate;
    }

    public AiDoc Add(AiKind kind, string wantedId, JsonObject root, bool save)
    {
        string id = UniqueId(kind, wantedId);
        root["format"] = AiCatalog.FormatVersion;
        root["id"] = id;
        var doc = new AiDoc(kind, id, root) { IsNew = true, Dirty = true };
        docs.Add(doc);
        if (save) Save(doc);
        else Changed?.Invoke();
        return doc;
    }

    public AiDoc Duplicate(AiDoc source)
    {
        var copy = (JsonObject)source.Root.DeepClone();
        string name = Json.Str(copy, "name");
        copy["name"] = (name.Length > 0 ? name : source.Id) + " (copy)";
        return Add(source.Kind, source.Id + "_copy", copy, false);
    }

    // New id; the behaviors a renamed behavior was used by are updated (those brains become unsaved changes).
    public void Rename(AiDoc doc, string wantedId)
    {
        string id = AiCatalog.CleanId(wantedId);
        if (id.Length == 0 || string.Equals(id, doc.Id, StringComparison.Ordinal)) return;
        if (Find(doc.Kind, id) != null && !string.Equals(id, doc.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.F("There is already a file called \"{0}\".", id));

        string oldId = doc.Id;
        string oldPath = PathOf(doc);
        bool onDisk = File.Exists(oldPath) && !doc.IsNew;
        doc.Id = id;
        doc.Root["id"] = id;
        if (onDisk)
        {
            // The file moves now; the id written inside it follows with the next save.
            string newPath = PathOf(doc);
            if (!string.Equals(oldPath, newPath, StringComparison.Ordinal)) File.Move(oldPath, newPath);
            doc.Dirty = true;
        }

        if (doc.Kind == AiKind.Behavior)
        {
            foreach (AiDoc brain in docs.Where(d => d.Kind == AiKind.Brain && d.LoadError == null))
            {
                bool changed = false;
                foreach (JsonObject state in Json.Objects(Json.ArrOrNull(brain.Root, "states")))
                {
                    if (string.Equals(Json.Str(state, "behavior"), oldId, StringComparison.OrdinalIgnoreCase))
                    {
                        state["behavior"] = id;
                        changed = true;
                    }
                }
                if (changed) brain.Dirty = true;
            }
        }
        Changed?.Invoke();
    }

    // The file goes to server\_backup\ai-<date> (never deleted for good).
    public void Delete(AiDoc doc)
    {
        string path = PathOf(doc);
        if (File.Exists(path) && !doc.IsNew)
        {
            string backup = LauncherPaths.NewBackupFolder("ai");
            File.Move(path, System.IO.Path.Combine(backup, doc.Kind + "-" + doc.Id + ".json"));
        }
        docs.Remove(doc);
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ what a brain uses

    public AiBehaviorFile? FindBehavior(string id)
    {
        AiDoc? doc = Find(AiKind.Behavior, id);
        if (doc == null || doc.LoadError != null) return null;
        try
        {
            return AiJson.ParseBehavior(doc.Root.ToJsonString());
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // The brain of a document ready to run, with the problems found. A behavior document gives a brain of one state.
    public AiBrain? Compile(AiDoc doc, List<AiProblem> problems)
    {
        if (doc.LoadError != null)
        {
            problems.Add(new AiProblem(true, doc.Id, doc.LoadError));
            return null;
        }
        try
        {
            if (doc.Kind == AiKind.Behavior)
            {
                problems.AddRange(AiLibrary.CheckBehavior(doc.Id, AiJson.ParseBehavior(doc.Root.ToJsonString())));
                var wrapper = new AiBrainFile
                {
                    Id = doc.Id,
                    Name = doc.DisplayName,
                    Initial = "run",
                    States = new List<AiStateFile> { new AiStateFile { Id = "run", Behavior = doc.Id } }
                };
                return AiLibrary.Compile(doc.Id, wrapper, FindBehavior, problems);
            }
            AiBrainFile file = AiJson.ParseBrain(doc.Root.ToJsonString());
            return AiLibrary.Compile(doc.Id, file, FindBehavior, problems);
        }
        catch (System.Text.Json.JsonException e)
        {
            problems.Add(new AiProblem(true, doc.Id, e.Message));
            return null;
        }
    }

    // Brains that use a behavior.
    public IEnumerable<string> UsedBy(AiDoc behavior)
    {
        foreach (AiDoc brain in Brains.Where(d => d.LoadError == null))
        {
            if (Json.Objects(Json.ArrOrNull(brain.Root, "states")).Any(s => string.Equals(Json.Str(s, "behavior"), behavior.Id, StringComparison.OrdinalIgnoreCase)))
                yield return brain.Id;
        }
    }

    // ------------------------------------------------------------------ sharing

    // One file with the brain and the behaviors it uses (or one file with the behavior alone).
    public void Export(AiDoc doc, string path)
    {
        var brains = new List<(string, JsonObject)>();
        var behaviors = new List<(string, JsonObject)>();
        if (doc.Kind == AiKind.Brain)
        {
            brains.Add((doc.Id, doc.Root));
            foreach (string id in AiPackage.UsedBehaviors(doc.Root))
            {
                AiDoc? used = Find(AiKind.Behavior, id);
                if (used != null && used.LoadError == null) behaviors.Add((used.Id, used.Root));
            }
        }
        else
        {
            behaviors.Add((doc.Id, doc.Root));
        }
        string name = Json.Str(doc.Root, "name").Length > 0 ? Json.Str(doc.Root, "name") : doc.Id;
        TextFiles.WriteAtomic(path, CompactJson.Write(AiPackage.Build(name, brains, behaviors)), false);
    }

    // Reads a file someone shared: a package, or a single brain or behavior file. Nothing is written yet.
    public static AiPackage.Contents ReadShared(string path)
    {
        return AiPackage.Read(TextFiles.ReadShared(path), System.IO.Path.GetFileNameWithoutExtension(path));
    }

    // Adds what a shared file holds. A file whose id already exists gets a new id (nothing of yours is overwritten);
    // the states of imported brains that used a renamed behavior follow it. Returns the documents that were added.
    public List<AiDoc> Import(AiPackage.Contents contents)
    {
        var added = new List<AiDoc>();
        var renamedBehaviors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string id, JsonObject root) in contents.Behaviors)
        {
            AiDoc doc = Add(AiKind.Behavior, id, (JsonObject)root.DeepClone(), true);
            if (!string.Equals(doc.Id, AiCatalog.CleanId(id), StringComparison.Ordinal)) renamedBehaviors[AiCatalog.CleanId(id)] = doc.Id;
            added.Add(doc);
        }
        foreach ((string id, JsonObject root) in contents.Brains)
        {
            var clone = (JsonObject)root.DeepClone();
            foreach (JsonObject state in Json.Objects(Json.ArrOrNull(clone, "states")))
            {
                string used = Json.Str(state, "behavior");
                if (used.Length > 0 && renamedBehaviors.TryGetValue(AiCatalog.CleanId(used), out string? mapped)) state["behavior"] = mapped;
            }
            added.Add(Add(AiKind.Brain, id, clone, true));
        }
        return added;
    }
}
