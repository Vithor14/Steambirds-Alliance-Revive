using Lidgren.Network;

public static partial class DungeonServer
{
    // ================================================================== script of the map

    // What the script engine (Scripting\MapScriptRunner.cs) asks of a dungeon instance. The units the script makes are tracked
    // like any other unit of the dungeon (onDeath, barks, sequences...), but they keep the group names the script gave them, so they
    // never count to clear a room.
    private sealed class ScriptHost : IScriptHost
    {
        private readonly Session s;

        public ScriptHost(Session s)
        {
            this.s = s;
        }

        public NetServer Server => s.Server;
        public NetConnection Connection => s.Connection;
        public string Role => DungeonServer.Role;
        public Maps.Map Map => s.Map!;

        public void Log(string text) => logger("DUNGEON", "[dungeon] " + text);

        public void Chat(string text) => DungeonServer.Chat(s, text);

        public void LivePlayers(List<(float X, float Y)> into)
        {
            into.Clear();
            foreach (NetConnection member in s.Members.ToList())
            {
                if (Game.GetPlane(member.RemoteUniqueIdentifier, out float x, out float y, out bool alive, out _) && alive) into.Add((x, y));
            }
        }

        public int CreateUnit(string type, float a, float b, float rotation, string group, Ground.Rect? area, bool? invulnerable, double now)
        {
            // A type of the map ("types") or, failing that, one of enemies.json.
            bool local = s.Data != null && s.Data.HasType(type);
            string key = local ? s.Data!.Key(type) : type;
            if (local && Enemies.IsControllerOnly(key)) return 0;
            Ground.ToWorld(a, b, out float x, out float y);
            RoomState? room = DungeonServer.RoomAt(s, a, b);
            int id = Enemies.CreateFromScript(s.Server, s.Connection, Role, key, x, y, rotation, group, area ?? room?.Area, invulnerable, now, logger);
            if (id != 0 && local && room != null)
            {
                s.Units[id] = new Tracked
                {
                    Id = id,
                    Type = type,
                    Room = room,
                    X = x,
                    Y = y,
                    SpawnedAt = now,
                    NextSummonAt = now + (s.Data!.ExtrasOf(type).PeriodicSummon?.Seconds ?? 0f)
                };
            }
            return id;
        }

        public void Speak(int shortId, string key, string portrait, bool dialog)
        {
            float pick;
            lock (random) pick = (float)random.NextDouble();
            foreach (NetConnection member in s.Members.ToList())
                WorldObjects.SendSpeech(s.Server, member, shortId, key, 4f, dialog ? WorldObjects.SpeechDialog : WorldObjects.SpeechNoChat, portrait, pick);
        }

        private GateState? Find(string name)
        {
            return s.Gates.FirstOrDefault(g => string.Equals(g.Def.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public bool GateOpen(string gate) => Find(gate)?.Open ?? false;

        public bool GateDamageable(string gate)
        {
            GateState? g = Find(gate);
            return g != null && (g.Def.Damageable || g.Unlocked);
        }

        public float GateB(string gate) => Find(gate)?.B ?? 0f;

        public void GateUnlock(string gate, double now)
        {
            GateState? g = Find(gate);
            if (g == null || g.Unlocked) return;
            g.Unlocked = true;
            Enemies.SetInvulnerable(s.Connection, g.Group, false);
        }

        public void GateOpenNow(string gate, bool explode, double now)
        {
            GateState? g = Find(gate);
            if (g != null && !g.Open) OpenGate(s, g, explode, now);
        }

        public int GateSpeaker(string gate)
        {
            GateState? g = Find(gate);
            if (g == null) return 0;
            List<int> alive = g.Pieces.Where(p => p.Id != 0 && Enemies.IsAlive(s.Connection, p.Id)).Select(p => p.Id).ToList();
            return alive.Count > 0 ? alive[alive.Count / 2] : 0;
        }

        public void ExitPortal(string point, float autoExitSeconds, double now)
        {
            CreateExitPortal(s, now, logger, point);
            if (autoExitSeconds > 0f)
                Schedule(s, now + autoExitSeconds, "automatic exit", _ => LeaveToNexus(s, "automatic exit (the player did not enter the carrier)", s.Finished));
        }

        public bool RoomCleared(string room) => DungeonServer.RoomCleared(s, room);

        public bool TypeDead(string type) => s.DeadTypes.Contains(type);
    }

    // The room whose area holds the point (a, b in tiles); the first room if none does.
    private static RoomState? RoomAtPoint(Session s, float a, float b)
    {
        foreach (RoomState room in s.Rooms)
        {
            if (room.Area.Contains(a, b, 0f)) return room;
        }
        return s.Rooms.Count > 0 ? s.Rooms[0] : null;
    }

    private static RoomState? RoomAt(Session s, float a, float b) => RoomAtPoint(s, a, b);
}
