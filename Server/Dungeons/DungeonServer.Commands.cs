using System.Globalization;
using System.Net;
using Lidgren.Network;
using Info = MapInfo;

public static partial class DungeonServer
{
    // ================================================================== commands

    // /room N and /boss: what is before the room disappears (counted as cleared), the doors on the way open and the
    // plane goes to the checkpoint of the room.
    private static void SkipTo(Session s, int index, double now, Action<string, string> log)
    {
        if (!s.Started)
        {
            Chat(s, "Wait for the plane to spawn to jump rooms.");
            return;
        }
        Maps.Checkpoint target = s.Map!.Checkpoints[index];
        float targetB = target.ActiveAtB;

        int rooms = 0;
        foreach (RoomState room in s.Rooms)
        {
            if (room.Area.MaxB > targetB + 0.01f || room.Cleared) continue;
            foreach (Tracked t in s.Units.Values.Where(u => u.Room == room)) t.Removed = true;
            Enemies.RemoveGroup(s.Connection, room.Group, false, now);
            Enemies.RemoveGroup(s.Connection, room.Extra, false, now);
            foreach (DungeonMapData.PlacedUnits p in DungeonMapData.AllUnits(room.Encounter)) s.DeadTypes.Add(p.Type);
            room.Spawned = room.Awake = room.Cleared = true;
            room.Wave = room.Encounter.Waves?.Count - 1 ?? -1;
            room.WaveAt = -1;
            s.Deferred.RemoveAll(d => d.Room == room);
            rooms++;
        }
        int gates = 0;
        foreach (GateState gate in s.Gates)
        {
            if (gate.Open) continue;
            bool before = gate.Side
                ? s.RoomsByName.TryGetValue(gate.Def.Room, out RoomState? sideRoom) && sideRoom.Cleared
                : gate.B < targetB;
            if (!before) continue;
            OpenGate(s, gate, false, now);
            gates++;
        }

        s.Checkpoint = Math.Max(s.Checkpoint, index);
        target.Point.World(out float x, out float y);
        WorldObjects.SendTeleport(s.Server, s.Connection, x, y, target.Point.Rotation, 0.2f);
        Chat(s, "Going to: " + target.Name + " (" + rooms + " room(s) and " + gates + " gate(s) behind).");
        log("DUNGEON", "[dungeon] jump to '" + target.Name + "': " + rooms + " room(s) and " + gates + " gate(s) released");
    }

    // /restart: everything goes, the dungeon starts again from the entrance.
    private static void Restart(Session s, double now, Action<string, string> log)
    {
        int units = Enemies.RemoveAll(s.Connection, now);
        foreach ((int id, float x, float y) in s.Objects)
            WorldObjects.SendRemoval(s.Server, s.Connection, id, x, y, WorldObjects.ReasonFizzled, now);

        Session fresh = NewSession(s.Server, s.Connection, s.Entry, now, log);
        sessions[fresh.Peer] = fresh;
        log("DUNGEON", "[dungeon] /restart: " + units + " unit(s) and " + s.Objects.Count + " object(s) removed");

        if (fresh.Map != null && Game.GetPlane(fresh.Peer, out _, out _, out bool alive, out _) && alive)
        {
            SpawnPoint(fresh.Connection, out float x, out float y, out float rotation);
            WorldObjects.SendTeleport(fresh.Server, fresh.Connection, x, y, rotation, 0.2f);
            StartDungeon(fresh, now, log);
        }
    }

}
