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

    // The folder of the file inside brains\ (or behaviors\), with "/": "generic", "world", "dungeons/arena"; "" = loose in it.
    public string Origin { get; set; } = "";

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

    public string PathOf(AiDoc doc)
    {
        string root = doc.Kind == AiKind.Brain ? BrainsFolder : BehaviorsFolder;
        string folder = doc.Origin.Length == 0 ? root : System.IO.Path.Combine(root, doc.Origin.Replace('/', System.IO.Path.DirectorySeparatorChar));
        return System.IO.Path.Combine(folder, doc.Id + ".json");
    }

    // Checks a folder typed by the user ("generic", "world", "dungeons/<name>") and cleans it. null = not allowed.
    public static string? NormalizeOrigin(string text)
    {
        string[] parts = text.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Select(AiCatalog.CleanId).ToArray();
        if (parts.Length == 0 || parts.Any(p => p.Length == 0)) return null;
        bool simple = parts[0] == AiCatalog.GenericFolder || parts[0] == AiCatalog.WorldFolder;
        if (simple && parts.Length == 1) return parts[0];
        if (parts[0] == AiCatalog.DungeonsFolder && parts.Length == 2) return parts[0] + "/" + parts[1];
        return null;
    }

    public IEnumerable<AiDoc> Brains => docs.Where(d => d.Kind == AiKind.Brain).OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase);
    public IEnumerable<AiDoc> Behaviors => docs.Where(d => d.Kind == AiKind.Behavior).OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase);
    public bool AnyDirty => docs.Any(d => d.Dirty);

    public AiDoc? Find(AiKind kind, string id)
    {
        string wanted = AiCatalog.IdOfReference(id);
        return docs.FirstOrDefault(d => d.Kind == kind && string.Equals(d.Id, wanted, StringComparison.OrdinalIgnoreCase));
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
        foreach (AiDoc dup in docs.Where(d => d.LoadError == null && docs.Any(o => o != d && o.Kind == d.Kind && o.LoadError == null &&
                     string.Equals(o.Id, d.Id, StringComparison.OrdinalIgnoreCase) && docs.IndexOf(o) < docs.IndexOf(d))).ToList())
        {
            // Two files with the same id (in different folders): the server uses the first one and ignores this one.
            dup.LoadError = L.F("Another file already has the id \"{0}\" (in another folder): the server ignores this one.", dup.Id);
        }
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
        foreach (string path in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(p => AiLibrary.OriginOf(folder, p).Length == 0 ? 1 : 0).ThenBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string id = AiCatalog.CleanId(System.IO.Path.GetFileNameWithoutExtension(path));
            if (id.Length == 0) continue;
            try
            {
                JsonNode? node = JsonNode.Parse(TextFiles.ReadShared(path), null,
                    new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (node is not JsonObject root) throw new System.Text.Json.JsonException("not a JSON object");
                docs.Add(new AiDoc(kind, id, root) { Origin = AiLibrary.OriginOf(folder, path) });
            }
            catch (Exception e)
            {
                docs.Add(new AiDoc(kind, id, new JsonObject()) { LoadError = e.Message, Origin = AiLibrary.OriginOf(folder, path) });
            }
        }
    }

    public void Save(AiDoc doc)
    {
        if (doc.LoadError != null) throw new InvalidOperationException(doc.LoadError);
        if (IsProtected(doc)) throw new InvalidOperationException(L.T(ProtectedText));
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

    // ------------------------------------------------------------------ generic AIs are never overwritten

    public const string ProtectedText = "Generic AIs are never overwritten. Duplicate it, or just edit it: the changes go to a new AI made for the unit.";

    // A generic AI (folder brains\generic) that is on the disk, and a behavior file one of them uses, cannot be changed, renamed, moved,
    // deleted or saved over from the editor: the server runs them for every unit that has no AI of its own. An edit goes to a copy made for
    // the unit (DivertEdits); a file that only exists in the launcher (new, not saved yet) is not protected.
    public bool IsProtected(AiDoc doc)
    {
        if (doc.LoadError != null || doc.IsNew || !File.Exists(PathOf(doc))) return false;
        if (doc.Kind == AiKind.Brain) return AiLibrary.IsGenericOrigin(doc.Origin);
        return UsedBy(doc).Any(id => Find(AiKind.Brain, id) is { } brain && AiLibrary.IsGenericOrigin(brain.Origin));
    }

    // Puts the file back as it is on the disk (what the editor changed in memory is dropped).
    public void RestoreFromDisk(AiDoc doc)
    {
        JsonNode? node = JsonNode.Parse(TextFiles.ReadShared(PathOf(doc)), null,
            new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (node is not JsonObject fresh) throw new System.Text.Json.JsonException("not a JSON object");
        doc.Root.Clear();
        foreach (KeyValuePair<string, JsonNode?> pair in fresh.ToList())
        {
            fresh.Remove(pair.Key);
            doc.Root[pair.Key] = pair.Value;
        }
        doc.Dirty = false;
        Changed?.Invoke();
    }

    // "new <unit> AI" (or behavior): the name of what a generic file becomes when it is changed or exported for a unit.
    public static string SpecificName(AiKind kind, string unit)
    {
        string who = unit.Length > 0 ? unit : "unit";
        return kind == AiKind.Brain ? "new " + who + " AI" : "new " + who + " behavior";
    }

    // The copy of a file made for a unit: it keeps everything (embedded behaviors follow the new id), is listed in "units" so the unit
    // is offered it, and lives in the folder of the unit (a behavior is a loose file). Nothing is written unless save is true.
    public AiDoc AddSpecific(AiDoc source, JsonObject root, string name, string unit, string origin, bool save)
    {
        var copy = (JsonObject)root.DeepClone();
        copy["name"] = name;
        copy["author"] = "";
        if (source.Kind == AiKind.Brain && unit.Length > 0) copy["units"] = new JsonArray(unit);
        return Add(source.Kind, name, copy, save, source.Kind == AiKind.Brain ? origin : "");
    }

    // A protected file was edited in memory: its edits become a new AI made for the unit (unsaved, in the folder of the unit) and
    // the protected file is read from the disk again, as it was.
    public AiDoc DivertEdits(AiDoc source, string unit, string origin)
    {
        var edited = (JsonObject)source.Root.DeepClone();
        RestoreFromDisk(source);
        return AddSpecific(source, edited, SpecificName(source.Kind, unit), unit, origin, false);
    }

    // "Save as": the open file under a new name, saved now. A generic AI becomes an AI made for the unit; any other keeps its folder.
    // The original goes back to what is on the disk when it had unsaved changes (they live in the copy now).
    public AiDoc SaveAs(AiDoc source, string name, string unit, string origin)
    {
        var edited = (JsonObject)source.Root.DeepClone();
        bool generic = source.Kind == AiKind.Brain && AiLibrary.IsGenericOrigin(source.Origin);
        AiDoc made;
        if (generic)
        {
            made = AddSpecific(source, edited, name, unit, origin, true);
        }
        else
        {
            edited["name"] = name;
            made = Add(source.Kind, name, edited, true, source.Kind == AiKind.Brain ? source.Origin : "");
        }
        if (source.Dirty && !source.IsNew && File.Exists(PathOf(source))) RestoreFromDisk(source);
        return made;
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

    // Embedded behaviors are named "<id of the AI>_<name>": when the AI gets another id (rename, copy, import) they follow it.
    public static void RetagEmbedded(JsonObject brainRoot, string oldId, string newId)
    {
        if (oldId.Length == 0 || string.Equals(oldId, newId, StringComparison.Ordinal)) return;
        string oldPrefix = oldId + "_";
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonObject behavior in Json.Objects(Json.ArrOrNull(brainRoot, "behaviors")))
        {
            string id = Json.Str(behavior, "id");
            if (!id.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            string changed = newId + "_" + id.Substring(oldPrefix.Length);
            behavior["id"] = changed;
            renamed[id] = changed;
        }
        if (renamed.Count == 0) return;
        foreach (JsonObject state in Json.Objects(Json.ArrOrNull(brainRoot, "states")))
        {
            if (renamed.TryGetValue(Json.Str(state, "behavior"), out string? now)) state["behavior"] = now;
        }
    }

    // origin: the folder of the file inside brains\ ("generic", "world", "dungeons/<name>"). null = generic for an AI, none for a behavior.
    public AiDoc Add(AiKind kind, string wantedId, JsonObject root, bool save, string? origin = null)
    {
        string id = UniqueId(kind, wantedId);
        if (kind == AiKind.Brain) RetagEmbedded(root, Json.Str(root, "id"), id);
        root["format"] = AiCatalog.FormatVersion;
        root["id"] = id;
        var doc = new AiDoc(kind, id, root) { IsNew = true, Dirty = true, Origin = origin ?? (kind == AiKind.Brain ? AiCatalog.GenericFolder : "") };
        docs.Add(doc);
        if (save) Save(doc);
        else Changed?.Invoke();
        return doc;
    }

    // A copy of a generic AI is not generic (it would be protected too): with a place it goes to the folder of the unit and is made for it.
    public AiDoc Duplicate(AiDoc source, string unit = "", string? origin = null)
    {
        var copy = (JsonObject)source.Root.DeepClone();
        string name = Json.Str(copy, "name");
        copy["name"] = (name.Length > 0 ? name : source.Id) + " (copy)";
        string where = source.Origin;
        if (source.Kind == AiKind.Brain && origin != null && AiLibrary.IsGenericOrigin(source.Origin))
        {
            where = origin;
            copy["author"] = "";
            if (unit.Length > 0) copy["units"] = new JsonArray(unit);
        }
        return Add(source.Kind, source.Id + "_copy", copy, false, where);
    }

    // Moves the file to another folder of brains\ (generic, world, dungeons/<name>); the id stays.
    public void Move(AiDoc doc, string origin)
    {
        if (string.Equals(doc.Origin, origin, StringComparison.Ordinal)) return;
        if (IsProtected(doc)) throw new InvalidOperationException(L.T(ProtectedText));
        string oldPath = PathOf(doc);
        bool onDisk = File.Exists(oldPath) && !doc.IsNew;
        string previous = doc.Origin;
        doc.Origin = origin;
        try
        {
            if (onDisk)
            {
                string newPath = PathOf(doc);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(newPath)!);
                if (File.Exists(newPath)) throw new IOException(L.F("There is already a file called \"{0}\" in that folder.", doc.Id));
                File.Move(oldPath, newPath);
            }
        }
        catch
        {
            doc.Origin = previous;
            throw;
        }
        Changed?.Invoke();
    }

    // New id; the behaviors a renamed behavior was used by are updated (those brains become unsaved changes).
    public void Rename(AiDoc doc, string wantedId)
    {
        string id = AiCatalog.CleanId(wantedId);
        if (id.Length == 0 || string.Equals(id, doc.Id, StringComparison.Ordinal)) return;
        if (IsProtected(doc)) throw new InvalidOperationException(L.T(ProtectedText));
        if (Find(doc.Kind, id) != null && !string.Equals(id, doc.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.F("There is already a file called \"{0}\".", id));

        string oldId = doc.Id;
        string oldPath = PathOf(doc);
        bool onDisk = File.Exists(oldPath) && !doc.IsNew;
        doc.Id = id;
        doc.Root["id"] = id;
        if (doc.Kind == AiKind.Brain) RetagEmbedded(doc.Root, oldId, id);
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
        if (IsProtected(doc)) throw new InvalidOperationException(L.T(ProtectedText));
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

    // The AIs a game unit can use: first the ones made for it (their "units" list has it), then the generic ones (folder generic);
    // each group by id. An AI that is neither is not offered to the unit.
    public List<string> ApplicableBrainIds(IEnumerable<string> unitNames) => ApplicableBrains(unitNames).Select(b => b.Id).ToList();

    // The same with a flag: true = made for the unit (listed in its "units"), false = generic.
    public List<(string Id, bool Specific)> ApplicableBrains(IEnumerable<string> unitNames)
    {
        string[] names = unitNames.ToArray();
        var specific = new List<string>();
        var generic = new List<string>();
        foreach (AiDoc doc in Brains.Where(d => d.LoadError == null))
        {
            bool isGeneric = string.Equals(doc.Origin, AiCatalog.GenericFolder, StringComparison.OrdinalIgnoreCase);
            if (isGeneric) generic.Add(doc.Id);
            else if (AiLibrary.Applies(false, Json.Strings(doc.Root["units"]), names)) specific.Add(doc.Id);
        }
        return specific.Select(id => (id, true)).Concat(generic.Select(id => (id, false))).ToList();
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
    // forUnit: a generic AI is written as a new AI made for that unit (what a copy in the list would be), so the file is shared and
    // imported as a specific AI; the AI in the list is not touched. Returns the id of the AI the file holds.
    public string Export(AiDoc doc, string path, string forUnit = "", string origin = "")
    {
        var brains = new List<(string, JsonObject)>();
        var behaviors = new List<(string, JsonObject)>();
        string exportedId = doc.Id;
        JsonObject exportedRoot = doc.Root;
        if (doc.Kind == AiKind.Brain && AiLibrary.IsGenericOrigin(doc.Origin) && forUnit.Length > 0)
        {
            AiDoc copy = AddSpecific(doc, doc.Root, SpecificName(AiKind.Brain, forUnit), forUnit, origin, false);
            docs.Remove(copy);
            Changed?.Invoke();
            exportedId = copy.Id;
            exportedRoot = copy.Root;
        }
        if (doc.Kind == AiKind.Brain)
        {
            brains.Add((exportedId, exportedRoot));
            foreach (string id in AiPackage.UsedBehaviors(exportedRoot))
            {
                AiDoc? used = Find(AiKind.Behavior, id);
                if (used != null && used.LoadError == null) behaviors.Add((used.Id, used.Root));
            }
        }
        else
        {
            behaviors.Add((doc.Id, doc.Root));
        }
        string name = Json.Str(exportedRoot, "name").Length > 0 ? Json.Str(exportedRoot, "name") : exportedId;
        TextFiles.WriteAtomic(path, CompactJson.Write(AiPackage.Build(name, brains, behaviors)), false);
        return exportedId;
    }

    // Reads a file someone shared: a package, or a single brain or behavior file. Nothing is written yet.
    public static AiPackage.Contents ReadShared(string path)
    {
        return AiPackage.Read(TextFiles.ReadShared(path), System.IO.Path.GetFileNameWithoutExtension(path));
    }

    // Adds what a shared file holds. A file whose id already exists gets a new id (nothing of yours is overwritten);
    // the states of imported brains that used a renamed behavior follow it. Returns the documents that were added.
    // save false: the files only exist in the launcher (marked as unsaved) until the user saves them.
    // place: the folder and the unit the AIs are for. An AI that comes in is never generic (it would be protected at once) and one with
    // no "units" would be offered to no one.
    public List<AiDoc> Import(AiPackage.Contents contents, bool save = true, (string Origin, string Unit)? place = null)
    {
        var added = new List<AiDoc>();
        var renamedBehaviors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string id, JsonObject root) in contents.Behaviors)
        {
            AiDoc doc = Add(AiKind.Behavior, id, (JsonObject)root.DeepClone(), save);
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
            if (place != null)
            {
                if (place.Value.Unit.Length > 0 && Json.Strings(clone["units"]).Count == 0) clone["units"] = new JsonArray(place.Value.Unit);
                added.Add(Add(AiKind.Brain, id, clone, save, place.Value.Origin));
            }
            else
            {
                added.Add(Add(AiKind.Brain, id, clone, save));
            }
        }
        return added;
    }
}
