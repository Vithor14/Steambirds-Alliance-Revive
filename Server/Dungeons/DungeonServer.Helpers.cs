using System.Globalization;
using System.Net;
using Lidgren.Network;
using Info = MapInfo;

public static partial class DungeonServer
{
    // ================================================================== helpers

    private static bool RoomCleared(Session s, string name)
    {
        return s.RoomsByName.TryGetValue(name, out RoomState? room) && room.Cleared;
    }

    // Distance (in tiles) from the point to the rectangle; 0 inside.
    private static float DistanceToArea(Ground.Rect area, float a, float b)
    {
        float dA = Math.Max(0f, Math.Max(area.MinA - a, a - area.MaxA));
        float db = Math.Max(0f, Math.Max(area.MinB - b, b - area.MaxB));
        return MathF.Sqrt(dA * dA + db * db);
    }

    private static void KeepInArea(Ground.Rect area, ref float x, ref float y)
    {
        Ground.ToGrid(x, y, out float a, out float b);
        area.Confine(ref a, ref b, 0.4f);
        Ground.ToWorld(a, b, out x, out y);
    }

    // Bark of the unit type for this moment (barks of the map), on the unit shortId: speech bubble and chat line.
    private static void Bark(Session s, int unitId, string type, string moment)
    {
        DungeonMapData.UnitExtras extras = s.Data!.ExtrasOf(type);
        if (extras.Barks == null || !extras.Barks.TryGetValue(moment, out string? key) || string.IsNullOrWhiteSpace(key)) return;
        if (key.Contains("..", StringComparison.Ordinal) || key.EndsWith("_", StringComparison.Ordinal)) return;   // ranges and prefixes: documentation only
        float pick;
        lock (random) pick = (float)random.NextDouble();
        WorldObjects.SendSpeech(s.Server, s.Connection, unitId, key, 5f, WorldObjects.SpeechBig, "", pick);
    }

    private static void Chat(Session s, string text)
    {
        WorldObjects.SendChat(s.Server, s.Connection, text);
    }

    private static void Schedule(Session s, double when, string name, Action<double> act)
    {
        s.Pending.Add(new PendingAction { When = when, Name = name, Act = act });
    }

    private static void RunPending(Session s, double now)
    {
        if (s.Pending.Count == 0) return;
        List<PendingAction> due = s.Pending.Where(p => p.When <= now).OrderBy(p => p.When).ToList();
        if (due.Count == 0) return;
        s.Pending.RemoveAll(p => p.When <= now);
        foreach (PendingAction p in due) p.Act(now);
    }

    private static string StateText(Session s)
    {
        if (s.Map == null) return s.Entry.Label + " (map did not load: " + s.LoadError + ")";
        string checkpoint = s.Map.Checkpoints.Count > 0
            ? s.Map.Checkpoints[Math.Clamp(s.Checkpoint, 0, s.Map.Checkpoints.Count - 1)].Name
            : "-";
        return s.Entry.Label + ": " + s.Rooms.Count(r => r.Cleared) + "/" + s.Rooms.Count + " rooms cleared, checkpoint '" +
            checkpoint + "', " + s.Gates.Count(g => !g.Open) + " gate(s) closed" + (s.Finished ? ", completed" : "");
    }

    private static string Capitalize(string text)
    {
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }

    // ClientMapInfo of each dungeon terrain: the original of the client (if it has one) plus one event per floor sign
    // of the map chosen in dungeons.json. Registered again when the catalog gets new dungeons.
    private static void RegisterMapInfos()
    {
        foreach (DungeonCatalog.Entry entry in DungeonCatalog.All())
        {
            if (!mapInfoTerrains.Add(entry.Type)) continue;
            string terrain = entry.Type;
            Info.RegisterMap(terrain, original => BuildMapInfo(terrain, original));
        }
    }

    private static Info.MapInfoData BuildMapInfo(string terrain, Info.MapInfoData? original)
    {
        Info.MapInfoData info = original ?? new Info.MapInfoData();
        DungeonCatalog.Entry? entry = DungeonCatalog.Find(terrain);
        if (entry == null) return info;
        try
        {
            Maps.AddSigns(info, Maps.Get(entry.Map, logger));
        }
        catch (Exception e)
        {
            logger("MAPINFO", "signs of " + terrain + " (" + entry.Map + ") were left out: " + e.Message);
        }
        return info;
    }
}
