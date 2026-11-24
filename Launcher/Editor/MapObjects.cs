using System.Text.Json.Nodes;

namespace SteambirdsLauncher.Editor;

internal enum ObjectKind
{
    Area,
    Line,
    Gate,
    Route,
    Decor,
    GroupPoint,
    Point,
    Checkpoint,
    Sign,
    Unit,
    Entrance
}

// Something of the map that can be seen, selected and moved on the canvas. It is a view of a JSON object of the map:
// changing a position writes the numbers straight into the file tree.
internal sealed class MapObject
{
    public MapObject(ObjectKind kind, JsonObject data, string name)
    {
        Kind = kind;
        Data = data;
        Name = name;
    }

    public ObjectKind Kind { get; }

    // The JSON object that holds the numbers (the "pos" entry of a unit, the area, the gate...). For a line it is
    // the "lines" object and Name is the key.
    public JsonObject Data { get; }

    // Point name, area name, gate name, unit type...
    public string Name { get; }

    // Room (units), route (route points) or group (group points) this belongs to.
    public string Owner { get; init; } = "";

    // Column gate ("sideGates"): runs along v instead of u.
    public bool Vertical { get; init; }

    // Unit of a wave (drawn lighter): the room is Owner.
    public bool InWave { get; init; }

    // Array that holds Data (to remove it) or the object whose property "Name" is Data.
    public JsonArray? Container { get; init; }
    public JsonObject? Dictionary { get; init; }

    // Units: the group {"type", "pos": [...]} and the array that holds the group (removed when its last unit goes).
    public JsonObject? Group { get; init; }
    public JsonArray? GroupContainer { get; init; }

    public bool HasRotation => Kind is ObjectKind.Entrance or ObjectKind.Point or ObjectKind.GroupPoint or
        ObjectKind.Checkpoint or ObjectKind.Sign or ObjectKind.Unit or ObjectKind.Decor;

    public bool CanMove => true;

    public bool Same(MapObject? other)
    {
        return other != null && Kind == other.Kind && ReferenceEquals(Data, other.Data) && Name == other.Name;
    }

    // ------------------------------------------------------------------ position

    public double Rotation
    {
        get => Json.Num(Data, "rot");
        set
        {
            if (HasRotation) Data["rot"] = Json.Round(MapCoords.NormalizeRotation(value));
        }
    }

    // Center of the object in u and v.
    public void GetPosition(out double u, out double v)
    {
        switch (Kind)
        {
            case ObjectKind.Area:
                u = (Json.Num(Data, "minU") + Json.Num(Data, "maxU")) / 2;
                v = (Json.Num(Data, "minV") + Json.Num(Data, "maxV")) / 2;
                return;
            case ObjectKind.Line:
                u = 0;
                v = Json.Num(Data, Name);
                return;
            case ObjectKind.Gate:
                GateBounds(out double minU, out double maxU, out double minV, out double maxV);
                u = (minU + maxU) / 2;
                v = (minV + maxV) / 2;
                return;
            default:
                u = Json.Num(Data, "u");
                v = Json.Num(Data, "v");
                return;
        }
    }

    // Moves the object by this much (gates move by whole sub-cells).
    public void MoveBy(double du, double dv)
    {
        switch (Kind)
        {
            case ObjectKind.Area:
                Data["minU"] = Json.Round(Json.Num(Data, "minU") + du);
                Data["maxU"] = Json.Round(Json.Num(Data, "maxU") + du);
                Data["minV"] = Json.Round(Json.Num(Data, "minV") + dv);
                Data["maxV"] = Json.Round(Json.Num(Data, "maxV") + dv);
                return;
            case ObjectKind.Line:
                Data[Name] = Json.Round(Json.Num(Data, Name) + dv);
                return;
            case ObjectKind.Gate:
                int dsi = (int)Math.Round(du / MapCoords.Sub);
                int dsj = (int)Math.Round(dv / MapCoords.Sub);
                if (Vertical)
                {
                    Data["si"] = Json.Int(Data, "si") + dsi;
                    Data["sjFrom"] = Json.Int(Data, "sjFrom") + dsj;
                    Data["sjTo"] = Json.Int(Data, "sjTo") + dsj;
                }
                else
                {
                    Data["sj"] = Json.Int(Data, "sj") + dsj;
                    Data["siFrom"] = Json.Int(Data, "siFrom") + dsi;
                    Data["siTo"] = Json.Int(Data, "siTo") + dsi;
                }
                return;
            case ObjectKind.Checkpoint:
                Data["u"] = Json.Round(Json.Num(Data, "u") + du);
                Data["v"] = Json.Round(Json.Num(Data, "v") + dv);
                // The checkpoint starts to count when the plane passes its line.
                if (Json.Has(Data, "activeAtV")) Data["activeAtV"] = Json.Round(Json.Num(Data, "activeAtV") + dv);
                if (Json.Has(Data, "activeAtU")) Data["activeAtU"] = Json.Round(Json.Num(Data, "activeAtU") + du);
                return;
            default:
                Data["u"] = Json.Round(Json.Num(Data, "u") + du);
                Data["v"] = Json.Round(Json.Num(Data, "v") + dv);
                return;
        }
    }

    // Area or gate rectangle in u and v.
    public void GateBounds(out double minU, out double maxU, out double minV, out double maxV)
    {
        double sub = MapCoords.Sub;
        if (Vertical)
        {
            minU = Json.Int(Data, "si") * sub;
            maxU = minU + sub;
            minV = Json.Int(Data, "sjFrom") * sub;
            maxV = (Json.Int(Data, "sjTo") + 1) * sub;
        }
        else
        {
            minV = Json.Int(Data, "sj") * sub;
            maxV = minV + sub;
            minU = Json.Int(Data, "siFrom") * sub;
            maxU = (Json.Int(Data, "siTo") + 1) * sub;
        }
    }

    // ------------------------------------------------------------------ removing

    // Removes the object from the map. A unit that was the last of its group takes the group with it.
    public void Delete()
    {
        if (Container != null) Container.Remove(Data);
        else if (Dictionary != null) Dictionary.Remove(Name);

        if (Kind == ObjectKind.Unit && Group != null && GroupContainer != null)
        {
            if (Json.ArrOrNull(Group, "pos")?.Count == 0) GroupContainer.Remove(Group);
        }
    }
}

// Lists what is on the map, in the order it is drawn (the last one is on top and is hit first).
internal static class MapObjects
{
    public static List<MapObject> All(MapDocument document)
    {
        JsonObject root = document.Root;
        var list = new List<MapObject>();

        JsonObject? areas = Json.ObjOrNull(root, "areas");
        foreach (KeyValuePair<string, JsonObject> pair in Json.ObjectProperties(areas))
            list.Add(new MapObject(ObjectKind.Area, pair.Value, pair.Key) { Dictionary = areas });

        JsonObject? lines = Json.ObjOrNull(root, "lines");
        if (lines != null)
        {
            foreach (string key in lines.Select(p => p.Key).ToList())
            {
                if (Json.IsNumber(lines[key])) list.Add(new MapObject(ObjectKind.Line, lines, key) { Dictionary = lines });
            }
        }

        AddGates(list, Json.ArrOrNull(root, "gates"), false);
        AddGates(list, Json.ArrOrNull(root, "sideGates"), true);

        JsonObject? routes = Json.ObjOrNull(root, "routes");
        if (routes != null)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in routes)
            {
                if (pair.Value is not JsonArray route) continue;
                foreach (JsonObject point in Json.Objects(route))
                    list.Add(new MapObject(ObjectKind.Route, point, pair.Key) { Owner = pair.Key, Container = route });
            }
        }

        JsonArray? ornaments = Json.ArrOrNull(root, "decor");
        foreach (JsonObject item in Json.Objects(ornaments))
            list.Add(new MapObject(ObjectKind.Decor, item, Json.Str(item, "name")) { Container = ornaments });
        JsonArray? buildings = Json.ArrOrNull(root, "buildings");
        foreach (JsonObject item in Json.Objects(buildings))
            list.Add(new MapObject(ObjectKind.Decor, item, Json.Str(item, "name")) { Container = buildings });

        JsonObject? groups = Json.ObjOrNull(root, "groups");
        if (groups != null)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in groups)
            {
                if (pair.Value is not JsonArray group) continue;
                foreach (JsonObject point in Json.Objects(group))
                    list.Add(new MapObject(ObjectKind.GroupPoint, point, pair.Key) { Owner = pair.Key, Container = group });
            }
        }

        JsonObject? points = Json.ObjOrNull(root, "points");
        foreach (KeyValuePair<string, JsonObject> pair in Json.ObjectProperties(points))
            list.Add(new MapObject(ObjectKind.Point, pair.Value, pair.Key) { Dictionary = points });

        JsonArray? checkpoints = Json.ArrOrNull(root, "checkpoints");
        foreach (JsonObject item in Json.Objects(checkpoints))
            list.Add(new MapObject(ObjectKind.Checkpoint, item, Json.Str(item, "name")) { Container = checkpoints });

        JsonArray? signs = Json.ArrOrNull(root, "signs");
        foreach (JsonObject item in Json.Objects(signs))
            list.Add(new MapObject(ObjectKind.Sign, item, Json.Str(item, "key")) { Container = signs });

        JsonArray? rooms = Json.ArrOrNull(root, "encounters");
        foreach (JsonObject room in Json.Objects(rooms))
        {
            string roomName = Json.Str(room, "room");
            JsonArray? fixedUnits = Json.ArrOrNull(room, "enemies");
            AddUnits(list, fixedUnits, roomName, false);
            foreach (JsonObject wave in Json.Objects(Json.ArrOrNull(room, "waves")))
                AddUnits(list, Json.ArrOrNull(wave, "groups"), roomName, true);
        }

        if (root["spawn"] is JsonObject spawn)
            list.Add(new MapObject(ObjectKind.Entrance, spawn, "spawn") { Dictionary = root });

        return list;
    }

    private static void AddGates(List<MapObject> list, JsonArray? gates, bool vertical)
    {
        foreach (JsonObject gate in Json.Objects(gates))
            list.Add(new MapObject(ObjectKind.Gate, gate, Json.Str(gate, "name")) { Container = gates, Vertical = vertical });
    }

    private static void AddUnits(List<MapObject> list, JsonArray? groups, string room, bool inWave)
    {
        foreach (JsonObject group in Json.Objects(groups))
        {
            string type = Json.Str(group, "type");
            JsonArray? positions = Json.ArrOrNull(group, "pos");
            foreach (JsonObject position in Json.Objects(positions))
            {
                list.Add(new MapObject(ObjectKind.Unit, position, type)
                {
                    Owner = room,
                    InWave = inWave,
                    Container = positions,
                    Group = group,
                    GroupContainer = groups
                });
            }
        }
    }
}
