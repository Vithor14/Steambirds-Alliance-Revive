using System.Globalization;
using System.Net;
using Lidgren.Network;
using Info = MapInfo;

public static partial class DungeonServer
{
    // ================================================================== commands

    // /room N and /boss: what is before the room disappears (counted as cleared), the doors on the way open and the
    // plane goes to the checkpoint of the room.
    private static void SkipTo(Session s, NetConnection who, int index, double now, Action<string, string> log)
    {
        if (!s.Started)
        {
            Chat(s, "Wait for the plane to spawn to jump rooms.");
            return;
        }
        Maps.Checkpoint target = s.Map!.Checkpoints[index];
        float targetB = target.RoomB;

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
        WorldObjects.SendTeleport(s.Server, who, x, y, target.Point.Rotation, 0.2f);   // only who asked: the others stay where they are
        Chat(s, "Going to: " + target.Name + " (" + rooms + " room(s) and " + gates + " gate(s) behind).");
        log("DUNGEON", "[dungeon] jump to '" + target.Name + "': " + rooms + " room(s) and " + gates + " gate(s) released");
    }

    // /restart: everything goes, the dungeon starts again from the entrance.
    private static void Restart(Session s, double now, Action<string, string> log)
    {
        int units = Enemies.RemoveAll(s.Connection, now);
        foreach (NetConnection member in Connected(s))
        {
            foreach ((int id, float x, float y) in s.Objects)
                WorldObjects.SendRemoval(s.Server, member, id, x, y, WorldObjects.ReasonFizzled, now);
        }

        // The same players, the same relay group, a dungeon from scratch.
        Session fresh = NewSession(s.Server, s.Connection, s.Entry, now, log);
        fresh.Id = s.Id;
        fresh.Members.Clear();
        fresh.Members.AddRange(s.Members);
        foreach (NetConnection member in fresh.Members)
        {
            sessions[member.RemoteUniqueIdentifier] = fresh;
            fresh.Synced.Add(member.RemoteUniqueIdentifier);   // StartDungeon sends everything to all of them
        }
        log("DUNGEON", "[dungeon] /restart: " + units + " unit(s) and " + s.Objects.Count + " object(s) removed");

        if (fresh.Map != null && fresh.Members.Any(c => Game.GetPlane(c.RemoteUniqueIdentifier, out _, out _, out bool planeAlive, out _) && planeAlive))
        {
            foreach (NetConnection member in Connected(fresh))
            {
                if (!Game.GetPlane(member.RemoteUniqueIdentifier, out _, out _, out bool alive, out _) || !alive) continue;
                SpawnPoint(member, out float x, out float y, out float rotation);
                WorldObjects.SendTeleport(fresh.Server, member, x, y, rotation, 0.2f);
            }
            StartDungeon(fresh, now, log);
        }
    }

}
