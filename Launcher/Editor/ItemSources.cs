using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.Editor;

// One entry of a list in a property page. "Data" is the JSON object the form edits: the real object of the map, or
// (for lists of numbers and of points) a small object made for the form whose changes the source copies back.
internal sealed class ListItem
{
    public ListItem(string text, JsonObject data)
    {
        Text = text;
        Data = data;
    }

    public string Text { get; set; }
    public JsonObject Data { get; }

    // Key of a dictionary entry (the name), empty for entries of an array.
    public string Key { get; init; } = "";

    // Array that holds Data (to remove it) and what the source needs to know about the entry.
    public JsonArray? Container { get; init; }
    public bool Column { get; init; }

    public override string ToString() => Text;
}

// Where the entries of a list come from and how to add, remove and rename them in the map.
internal abstract class ItemSource
{
    public abstract List<ListItem> Load();
    public abstract ListItem? Add(string suggestedName);
    public abstract void Remove(ListItem item);

    public virtual ListItem? Duplicate(ListItem item) => null;

    public virtual bool CanDuplicate => false;

    // Dictionary entries are named by their key, which the list editor shows in a box of its own.
    public virtual bool NamedByKey => false;

    public virtual bool Rename(ListItem item, string newName) => false;

    // The form changed Data: copy it back when Data is not the real object.
    public virtual void Commit(ListItem item)
    {
    }

    // Text of the entry in the list (called again after the form changed it).
    public virtual string TextOf(ListItem item) => item.Text;
}

// "name": { ... } entries: pontos, areas, tipos.
internal sealed class ObjectDictionarySource : ItemSource
{
    private readonly Func<JsonObject?> find;
    private readonly Func<JsonObject> findOrCreate;
    private readonly Func<string, JsonObject> create;
    private readonly Action<string, string>? renamed;

    public ObjectDictionarySource(Func<JsonObject?> find, Func<JsonObject> findOrCreate, Func<string, JsonObject> create,
        Action<string, string>? renamed = null)
    {
        this.find = find;
        this.findOrCreate = findOrCreate;
        this.create = create;
        this.renamed = renamed;
    }

    public override bool NamedByKey => true;
    public override bool CanDuplicate => true;

    public override List<ListItem> Load()
    {
        return Json.ObjectProperties(find()).Select(p => new ListItem(p.Key, p.Value) { Key = p.Key }).ToList();
    }

    public override ListItem? Add(string suggestedName)
    {
        JsonObject parent = findOrCreate();
        string name = Json.UniqueName(parent.Select(p => p.Key), suggestedName);
        JsonObject created = create(name);
        parent[name] = created;
        return new ListItem(name, created) { Key = name };
    }

    public override void Remove(ListItem item) => find()?.Remove(item.Key);

    public override ListItem? Duplicate(ListItem item)
    {
        JsonObject parent = findOrCreate();
        string name = Json.UniqueName(parent.Select(p => p.Key), item.Key);
        if (item.Data.DeepClone() is not JsonObject clone) return null;
        parent[name] = clone;
        return new ListItem(name, clone) { Key = name };
    }

    public override bool Rename(ListItem item, string newName)
    {
        JsonObject? parent = find();
        if (parent == null || newName.Length == 0 || parent.ContainsKey(newName)) return false;
        Json.RenameProperty(parent, item.Key, newName);
        renamed?.Invoke(item.Key, newName);
        return true;
    }
}

// [ { ... }, ... ] entries named by a field of their own (gates by "name", rooms by "room").
internal sealed class ObjectArraySource : ItemSource
{
    private readonly Func<JsonArray?> find;
    private readonly Func<JsonArray> findOrCreate;
    private readonly string nameKey;
    private readonly Func<string, JsonObject> create;
    private readonly bool uniqueNames;

    public ObjectArraySource(Func<JsonArray?> find, Func<JsonArray> findOrCreate, string nameKey, Func<string, JsonObject> create,
        bool uniqueNames = true)
    {
        this.find = find;
        this.findOrCreate = findOrCreate;
        this.nameKey = nameKey;
        this.create = create;
        this.uniqueNames = uniqueNames;
    }

    public override bool CanDuplicate => true;

    public override List<ListItem> Load()
    {
        JsonArray? array = find();
        var items = new List<ListItem>();
        foreach (JsonObject obj in Json.Objects(array)) items.Add(new ListItem(NameOf(obj), obj) { Container = array });
        return items;
    }

    private string NameOf(JsonObject obj)
    {
        string name = Json.Str(obj, nameKey);
        return name.Length > 0 ? name : "(" + L.T("no name") + ")";
    }

    public override string TextOf(ListItem item) => NameOf(item.Data);

    public override ListItem? Add(string suggestedName)
    {
        JsonArray array = findOrCreate();
        string name = uniqueNames ? Json.UniqueName(Json.Objects(array).Select(o => Json.Str(o, nameKey)), suggestedName) : suggestedName;
        JsonObject created = create(name);
        array.Add(created);
        return new ListItem(NameOf(created), created) { Container = array };
    }

    public override void Remove(ListItem item) => item.Container?.Remove(item.Data);

    public override ListItem? Duplicate(ListItem item)
    {
        JsonArray array = findOrCreate();
        if (item.Data.DeepClone() is not JsonObject clone) return null;
        if (uniqueNames) clone[nameKey] = Json.UniqueName(Json.Objects(array).Select(o => Json.Str(o, nameKey)), Json.Str(item.Data, nameKey));
        array.Add(clone);
        return new ListItem(NameOf(clone), clone) { Container = array };
    }
}

// "name": number entries (lines and values). The form edits {"value": n}.
internal sealed class NumberDictionarySource : ItemSource
{
    private readonly Func<JsonObject?> find;
    private readonly Func<JsonObject> findOrCreate;
    private readonly Action<string, string>? renamed;

    public NumberDictionarySource(Func<JsonObject?> find, Func<JsonObject> findOrCreate, Action<string, string>? renamed = null)
    {
        this.find = find;
        this.findOrCreate = findOrCreate;
        this.renamed = renamed;
    }

    public override bool NamedByKey => true;
    public override bool CanDuplicate => true;

    public override List<ListItem> Load()
    {
        JsonObject? parent = find();
        var items = new List<ListItem>();
        if (parent == null) return items;
        foreach (KeyValuePair<string, JsonNode?> pair in parent)
        {
            if (!Json.IsNumber(pair.Value)) continue;
            items.Add(new ListItem(pair.Key, new JsonObject { ["value"] = Json.AsNumber(pair.Value) }) { Key = pair.Key });
        }
        return items;
    }

    public override ListItem? Add(string suggestedName)
    {
        JsonObject parent = findOrCreate();
        string name = Json.UniqueName(parent.Select(p => p.Key), suggestedName);
        parent[name] = 0;
        return new ListItem(name, new JsonObject { ["value"] = 0 }) { Key = name };
    }

    public override void Remove(ListItem item) => find()?.Remove(item.Key);

    public override ListItem? Duplicate(ListItem item)
    {
        JsonObject parent = findOrCreate();
        string name = Json.UniqueName(parent.Select(p => p.Key), item.Key);
        parent[name] = Json.Num(item.Data, "value");
        return new ListItem(name, new JsonObject { ["value"] = Json.Num(item.Data, "value") }) { Key = name };
    }

    public override bool Rename(ListItem item, string newName)
    {
        JsonObject? parent = find();
        if (parent == null || newName.Length == 0 || parent.ContainsKey(newName)) return false;
        Json.RenameProperty(parent, item.Key, newName);
        renamed?.Invoke(item.Key, newName);
        return true;
    }

    public override void Commit(ListItem item)
    {
        JsonObject? parent = find();
        if (parent != null && parent.ContainsKey(item.Key)) parent[item.Key] = Json.Round(Json.Num(item.Data, "value"));
    }
}

// "name": [ {u, v, rot}, ... ] entries (group points and routes). The form edits {"pos": [...]}.
internal sealed class PointListDictionarySource : ItemSource
{
    private readonly Func<JsonObject?> find;
    private readonly Func<JsonObject> findOrCreate;

    public PointListDictionarySource(Func<JsonObject?> find, Func<JsonObject> findOrCreate)
    {
        this.find = find;
        this.findOrCreate = findOrCreate;
    }

    public override bool NamedByKey => true;

    public override List<ListItem> Load()
    {
        JsonObject? parent = find();
        var items = new List<ListItem>();
        if (parent == null) return items;
        foreach (KeyValuePair<string, JsonNode?> pair in parent)
        {
            if (pair.Value is not JsonArray array) continue;
            items.Add(new ListItem(pair.Key, new JsonObject { ["pos"] = array.DeepClone() }) { Key = pair.Key });
        }
        return items;
    }

    public override ListItem? Add(string suggestedName)
    {
        JsonObject parent = findOrCreate();
        string name = Json.UniqueName(parent.Select(p => p.Key), suggestedName);
        parent[name] = new JsonArray();
        return new ListItem(name, new JsonObject { ["pos"] = new JsonArray() }) { Key = name };
    }

    public override void Remove(ListItem item) => find()?.Remove(item.Key);

    public override bool Rename(ListItem item, string newName)
    {
        JsonObject? parent = find();
        if (parent == null || newName.Length == 0 || parent.ContainsKey(newName)) return false;
        Json.RenameProperty(parent, item.Key, newName);
        return true;
    }

    public override void Commit(ListItem item)
    {
        JsonObject? parent = find();
        if (parent == null || !parent.ContainsKey(item.Key)) return;
        parent[item.Key] = item.Data["pos"]?.DeepClone() ?? new JsonArray();
    }
}

// The gates of both lists: "gates" (a row of sub-cells) and "sideGates" (a column).
internal sealed class GateSource : ItemSource
{
    private readonly MapSession session;
    private readonly Func<string> defaultType;

    public GateSource(MapSession session, Func<string> defaultType)
    {
        this.session = session;
        this.defaultType = defaultType;
    }

    private JsonObject Root => session.Document.Root;

    public override bool CanDuplicate => true;

    // A gate of the side corridors: a column of sub-cells ("sideGates").
    public ListItem AddColumn(string suggestedName)
    {
        JsonArray columns = Json.Arr(Root, "sideGates");
        string name = Json.UniqueName(AllNames(), suggestedName);
        var gate = new JsonObject
        {
            ["name"] = name,
            ["type"] = defaultType(),
            ["si"] = 0,
            ["sjFrom"] = 0,
            ["sjTo"] = 3,
            ["damageable"] = true
        };
        columns.Add(gate);
        return Make(gate, columns, true);
    }

    public override List<ListItem> Load()
    {
        var items = new List<ListItem>();
        JsonArray? rows = Json.ArrOrNull(Root, "gates");
        foreach (JsonObject gate in Json.Objects(rows)) items.Add(Make(gate, rows, false));
        JsonArray? columns = Json.ArrOrNull(Root, "sideGates");
        foreach (JsonObject gate in Json.Objects(columns)) items.Add(Make(gate, columns, true));
        return items;
    }

    private static ListItem Make(JsonObject gate, JsonArray? container, bool column)
    {
        string name = Json.Str(gate, "name");
        if (name.Length == 0) name = "(" + L.T("no name") + ")";
        return new ListItem(Label(name, column), gate) { Container = container, Column = column };
    }

    private static string Label(string name, bool column) => (column ? "| " : "- ") + name;

    public override string TextOf(ListItem item)
    {
        string name = Json.Str(item.Data, "name");
        return Label(name.Length > 0 ? name : "(" + L.T("no name") + ")", item.Column);
    }

    public override ListItem? Add(string suggestedName)
    {
        JsonArray rows = Json.Arr(Root, "gates");
        string name = Json.UniqueName(AllNames(), suggestedName);
        var gate = new JsonObject
        {
            ["name"] = name,
            ["type"] = defaultType(),
            ["sj"] = 0,
            ["siFrom"] = 0,
            ["siTo"] = 3,
            ["damageable"] = true
        };
        rows.Add(gate);
        return Make(gate, rows, false);
    }

    public override void Remove(ListItem item) => item.Container?.Remove(item.Data);

    public override ListItem? Duplicate(ListItem item)
    {
        if (item.Data.DeepClone() is not JsonObject clone) return null;
        JsonArray target = item.Column ? Json.Arr(Root, "sideGates") : Json.Arr(Root, "gates");
        clone["name"] = Json.UniqueName(AllNames(), Json.Str(item.Data, "name"));
        if (item.Column) clone["si"] = Json.Int(clone, "si") + MapCoords.SubsPerTile;
        else
        {
            clone["siFrom"] = Json.Int(clone, "siFrom") + MapCoords.SubsPerTile;
            clone["siTo"] = Json.Int(clone, "siTo") + MapCoords.SubsPerTile;
        }
        target.Add(clone);
        return Make(clone, target, item.Column);
    }

    private IEnumerable<string> AllNames()
    {
        return Json.Objects(Json.ArrOrNull(Root, "gates")).Concat(Json.Objects(Json.ArrOrNull(Root, "sideGates")))
            .Select(g => Json.Str(g, "name"));
    }
}

// The groups of enemies of a room or of a wave: { "type", "pos": [...], ... }. Several groups may have the same type.
internal sealed class UnitGroupSource : ItemSource
{
    private readonly Func<JsonArray?> find;
    private readonly Func<JsonArray?> findOrCreate;
    private readonly Func<string> defaultType;

    public UnitGroupSource(Func<JsonArray?> find, Func<JsonArray?> findOrCreate, Func<string> defaultType)
    {
        this.find = find;
        this.findOrCreate = findOrCreate;
        this.defaultType = defaultType;
    }

    public override bool CanDuplicate => true;

    public override List<ListItem> Load()
    {
        JsonArray? array = find();
        return Json.Objects(array).Select(g => new ListItem(Describe(g), g) { Container = array }).ToList();
    }

    public override string TextOf(ListItem item) => Describe(item.Data);

    private static string Describe(JsonObject group)
    {
        string type = Json.Str(group, "type");
        int count = group["pos"] is JsonArray pos ? pos.Count : 0;
        return (type.Length > 0 ? type : "(" + L.T("no type") + ")") + "  x" + count;
    }

    public override ListItem? Add(string suggestedName)
    {
        JsonArray? array = findOrCreate();
        if (array == null) return null;
        var group = new JsonObject { ["type"] = defaultType(), ["pos"] = new JsonArray() };
        array.Add(group);
        return new ListItem(Describe(group), group) { Container = array };
    }

    public override void Remove(ListItem item) => item.Container?.Remove(item.Data);

    public override ListItem? Duplicate(ListItem item)
    {
        JsonArray? array = findOrCreate();
        if (array == null || item.Data.DeepClone() is not JsonObject clone) return null;
        array.Add(clone);
        return new ListItem(Describe(clone), clone) { Container = array };
    }
}

// The phases of a boss: { "belowHealth": 0.66, ... }, in the order of the file.
internal sealed class PhaseSource : ItemSource
{
    private readonly Func<JsonArray?> find;
    private readonly Func<JsonArray?> findOrCreate;

    public PhaseSource(Func<JsonArray?> find, Func<JsonArray?> findOrCreate)
    {
        this.find = find;
        this.findOrCreate = findOrCreate;
    }

    public override bool CanDuplicate => true;

    public override List<ListItem> Load()
    {
        JsonArray? array = find();
        return Json.Objects(array).Select(p => new ListItem(Describe(p), p) { Container = array }).ToList();
    }

    public override string TextOf(ListItem item) => Describe(item.Data);

    private static string Describe(JsonObject phase)
    {
        double below = Json.Num(phase, "belowHealth", 1);
        return L.F("below {0}% health", Math.Round(below * 100).ToString("0"));
    }

    public override ListItem? Add(string suggestedName)
    {
        JsonArray? array = findOrCreate();
        if (array == null) return null;
        var phase = new JsonObject { ["belowHealth"] = 0.5 };
        array.Add(phase);
        return new ListItem(Describe(phase), phase) { Container = array };
    }

    public override void Remove(ListItem item) => item.Container?.Remove(item.Data);

    public override ListItem? Duplicate(ListItem item)
    {
        JsonArray? array = findOrCreate();
        if (array == null || item.Data.DeepClone() is not JsonObject clone) return null;
        array.Add(clone);
        return new ListItem(Describe(clone), clone) { Container = array };
    }
}
