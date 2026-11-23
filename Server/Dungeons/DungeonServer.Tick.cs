using System.Globalization;
using System.Net;
using Lidgren.Network;
using Info = MapInfo;

public static partial class DungeonServer
{
    // ================================================================== tick

    private static void Tick(Session s, double now, Action<string, string> log)
    {
        RunPending(s, now);
        if (!s.Started || s.Map == null || s.Data == null || s.Left) return;

        UpdateUnits(s, now, log);

        // Rooms come up and wake with ANY live player (and the checkpoint is the furthest one reached by any of them).
        foreach (NetConnection member in s.Members.ToList())
        {
            if (Game.GetPlane(member.RemoteUniqueIdentifier, out float px, out float py, out bool alive, out _) && alive)
            {
                Ground.ToGrid(px, py, out float pa, out float pb);
                UpdateCheckpoint(s, pb, log);
                UpdateRooms(s, pa, pb, now, log);
            }
        }

        UpdateConditionalRooms(s, now, log);
        UpdateWaves(s, now, log);
        UpdateClears(s, now, log);
        UpdateGates(s, now, log);
        UpdateDeferred(s, now, log);
        s.Script?.Update(now);
        CheckExit(s, now, log);
    }

    private static void UpdateCheckpoint(Session s, float pb, Action<string, string> log)
    {
        List<Maps.Checkpoint> points = s.Map!.Checkpoints;
        int best = s.Checkpoint;
        for (int i = s.Checkpoint + 1; i < points.Count; i++)
        {
            if (pb >= points[i].ActiveAtB) best = i;
        }
        if (best == s.Checkpoint) return;
        s.Checkpoint = best;
        log("DUNGEON", "[dungeon] checkpoint: " + points[best].Name);
    }

    private static void UpdateRooms(Session s, float pa, float pb, double now, Action<string, string> log)
    {
        foreach (RoomState room in s.Rooms)
        {
            if (!room.Spawned && DistanceToArea(room.Area, pa, pb) <= SpawnAheadTiles) SpawnRoom(s, room, now, log);

            if (!room.Awake && WakesOnEntry(room) && room.Area.Contains(pa, pb, WakeMargin))
            {
                if (!room.Spawned) SpawnRoom(s, room, now, log);
                WakeRoom(s, room, now, log);
            }

            if (room.Awake && !room.Cleared && !room.Closed && room.Encounter.ClosesOnEnter && DungeonCatalog.LockBossRooms &&
                room.Area.Contains(pa, pb, CloseBehindMargin))
                CloseBehind(s, room, now, log);
        }
    }

    // Rooms that wake up when a unit type dies ("wakes": "death:<type>").
    private static void UpdateConditionalRooms(Session s, double now, Action<string, string> log)
    {
        foreach (RoomState room in s.Rooms)
        {
            if (room.Awake) continue;
            string when = room.Encounter.Wakes ?? "";
            if (!when.StartsWith("death:", StringComparison.OrdinalIgnoreCase)) continue;
            if (!DungeonMapData.Check(when, n => RoomCleared(s, n), t => s.DeadTypes.Contains(t))) continue;
            if (!room.Spawned) SpawnRoom(s, room, now, log);
            WakeRoom(s, room, now, log);
        }
    }

    private static bool WakesOnEntry(RoomState room)
    {
        string when = (room.Encounter.Wakes ?? "").Trim().ToLowerInvariant();
        // "script" and "drill" (The Dig drill) are not scripted yet: they also wake up when the player comes in.
        return when.Length == 0 || when == "onenter" || when == "script" || when == "drill";
    }

    // The enemies of the room appear in their places, waiting (they wake up with the room or with a shot).
    private static void SpawnRoom(Session s, RoomState room, double now, Action<string, string> log)
    {
        room.Spawned = true;
        int created = 0;
        foreach (DungeonMapData.PlacedUnits p in room.Encounter.Enemies ?? new List<DungeonMapData.PlacedUnits>())
        {
            if (!string.IsNullOrWhiteSpace(p.Wakes))
            {
                s.Deferred.Add((room, p));
                continue;
            }
            foreach (DungeonMapData.PosDef pos in p.Pos ?? new List<DungeonMapData.PosDef>())
            {
                if (SpawnAtGrid(s, p.Type, pos.U, pos.V, pos.Rot, room, true, false, 0, now, log) != 0) created++;
            }
        }
        if (created > 0 || room.HasWaves)
            log("DUNGEON", "[dungeon] room '" + room.Name + "' set up: " + created + " unit(s) waiting" +
                (room.HasWaves ? ", " + room.Encounter.Waves!.Count + " wave(s) when the player enters" : ""));
    }

    private static void WakeRoom(Session s, RoomState room, double now, Action<string, string> log)
    {
        room.Awake = true;
        int awake = Enemies.WakeGroup(s.Connection, room.Group, now) + Enemies.WakeGroup(s.Connection, room.Extra, now);
        Enemies.SetDefaultArea(s.Server, s.Connection, Role, room.Area, now);

        foreach (Tracked t in s.Units.Values)
        {
            if (t.Room != room || t.Gone) continue;
            Bark(s, t.Id, t.Type, "onWake");
            Enemies.EnemyType? type = Enemies.GetEnemyType(s.Data!.Key(t.Type));
            if (type != null && type.Boss) Chat(s, type.Name + "!");
        }

        if (room.HasWaves)
        {
            room.Wave = -1;
            room.WaveAt = now + Math.Max(0.5f, room.Encounter.Waves![0].DelayBeforeSeconds);
            Chat(s, room.Encounter.Waves.Count + " waves! The first arrives in " +
                room.Encounter.Waves[0].DelayBeforeSeconds.ToString("0", Inv) + " s.");
        }
        log("DUNGEON", "[dungeon] room '" + room.Name + "' woke up (" + awake + " unit(s))" +
            (room.Encounter.Boss ? " [boss]" : room.Encounter.Miniboss ? " [miniboss]" : ""));
    }

    // Doors of a boss room close behind the player until the room is clear.
    private static void CloseBehind(Session s, RoomState room, double now, Action<string, string> log)
    {
        room.Closed = true;
        int closed = 0;
        foreach (GateState gate in s.Gates)
        {
            if (!gate.Open || !string.Equals(gate.Def.Room, room.Name, StringComparison.OrdinalIgnoreCase)) continue;
            SpawnGate(s, gate, true, now, log);
            gate.ClosedBehind = true;
            gate.ReopenRoom = room.Name;
            closed++;
        }
        if (closed > 0)
        {
            Chat(s, "The doors closed behind you!");
            log("DUNGEON", "[dungeon] room '" + room.Name + "': " + closed + " gate(s) closed behind the player");
        }
    }

    private static void UpdateWaves(Session s, double now, Action<string, string> log)
    {
        foreach (RoomState room in s.Rooms)
        {
            if (!room.Awake || !room.HasWaves || room.Cleared) continue;
            List<DungeonMapData.WaveDef> waves = room.Encounter.Waves!;
            if (room.WaveAt >= 0)
            {
                if (now < room.WaveAt) continue;
                room.WaveAt = -1;
                room.Wave++;
                SpawnWave(s, room, waves[room.Wave], now, log);
                continue;
            }
            if (room.Wave + 1 >= waves.Count) continue;
            if (Enemies.CountAlive(s.Connection, room.Group) > 0) continue;
            room.WaveAt = now + Math.Max(0.5f, waves[room.Wave + 1].DelayBeforeSeconds);
        }
    }

    private static void SpawnWave(Session s, RoomState room, DungeonMapData.WaveDef wave, double now, Action<string, string> log)
    {
        int created = 0;
        foreach (DungeonMapData.PlacedUnits p in wave.Groups ?? new List<DungeonMapData.PlacedUnits>())
        {
            foreach (DungeonMapData.PosDef pos in p.Pos ?? new List<DungeonMapData.PosDef>())
            {
                if (SpawnAtGrid(s, p.Type, pos.U, pos.V, pos.Rot, room, false, true, 0, now, log) != 0) created++;
            }
        }
        string message = !string.IsNullOrWhiteSpace(wave.Message) ? wave.Message
            : "Wave " + (room.Wave + 1).ToString(Inv) + " of " + room.Encounter.Waves!.Count.ToString(Inv) + (wave.Name.Length > 0 ? ": " + wave.Name : "");
        Chat(s, message);
        log("DUNGEON", "[dungeon] room '" + room.Name + "' wave " + (room.Wave + 1) + " (" + wave.Name + "): " + created + " unit(s)");
    }

    private static void UpdateClears(Session s, double now, Action<string, string> log)
    {
        foreach (RoomState room in s.Rooms)
        {
            if (!room.Awake || room.Cleared || !room.WavesDone) continue;
            int alive = Enemies.CountAlive(s.Connection, room.Group);
            if (alive > 0)
            {
                // Few units left and nothing happens: say which ones (a gate that "does not open" is usually a small unit
                // somewhere in the room).
                if (alive <= 3)
                {
                    if (room.HintAt < 0) room.HintAt = now + 30.0;
                    else if (now >= room.HintAt)
                    {
                        room.HintAt = now + 30.0;
                        List<string> names = Enemies.AliveNamesInGroup(s.Connection, room.Group);
                        Chat(s, "Still alive in the room: " + string.Join(", ", names.GroupBy(n => n).Select(g => g.Key + (g.Count() > 1 ? " x" + g.Count() : ""))) + ".");
                    }
                }
                else room.HintAt = -1;
                continue;
            }
            room.Cleared = true;
            log("DUNGEON", "[dungeon] room '" + room.Name + "' cleared");
            if (room.Encounter.Miniboss) Chat(s, "Miniboss defeated!");
        }
    }

    private static void UpdateGates(Session s, double now, Action<string, string> log)
    {
        foreach (GateState gate in s.Gates)
        {
            if (gate.Open) continue;
            bool pieceBroken = gate.Pieces.Any(p => p.Id != 0 && !Enemies.IsAlive(s.Connection, p.Id));
            bool noPieces = gate.Pieces.All(p => p.Id == 0);
            string reason = "";

            if (gate.ClosedBehind)
            {
                if (s.RoomsByName.TryGetValue(gate.ReopenRoom, out RoomState? room) && room.Cleared) reason = "room cleared";
            }
            else if (pieceBroken)
            {
                reason = "destroyed";
            }
            else if (gate.Def.OpensWhen.Length > 0 &&
                     DungeonMapData.Check(gate.Def.OpensWhen, n => RoomCleared(s, n), t => s.DeadTypes.Contains(t)))
            {
                reason = "condition " + gate.Def.OpensWhen;
            }
            else if (noPieces && (gate.Def.Damageable || gate.Def.OpensWhen.Length == 0))
            {
                reason = "no pieces";
            }
            if (reason.Length == 0) continue;

            OpenGate(s, gate, reason != "no pieces", now);
            log("DUNGEON", "[dungeon] " + gate.Def.Name + " open (" + reason + ")");
            if (reason.StartsWith("condition", StringComparison.Ordinal) || reason == "room cleared")
                Chat(s, Capitalize(gate.Def.Name) + " opened.");
        }
    }

    private static void OpenGate(Session s, GateState gate, bool explode, double now)
    {
        gate.Open = true;
        gate.ClosedBehind = false;
        gate.ReopenRoom = "";
        Enemies.RemoveGroup(s.Connection, gate.Group, explode, now);
    }

    // Units that only appear when a type dies ("wakes": "death:<type>" in the unit entry): rewards, portals.
    private static void UpdateDeferred(Session s, double now, Action<string, string> log)
    {
        for (int i = s.Deferred.Count - 1; i >= 0; i--)
        {
            (RoomState room, DungeonMapData.PlacedUnits p) = s.Deferred[i];
            if (!DungeonMapData.Check(p.Wakes, n => RoomCleared(s, n), t => s.DeadTypes.Contains(t))) continue;
            s.Deferred.RemoveAt(i);
            foreach (DungeonMapData.PosDef pos in p.Pos ?? new List<DungeonMapData.PosDef>())
                SpawnAtGrid(s, p.Type, pos.U, pos.V, pos.Rot, room, false, false, 0, now, log);
            log("DUNGEON", "[dungeon] '" + p.Type + "' appeared in room '" + room.Name + "' (" + p.Wakes + ")");
        }
    }

}
