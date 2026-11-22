using System.Globalization;
using System.Net;
using Lidgren.Network;
using Info = MapInfo;

public static partial class DungeonServer
{
    // ================================================================== units

    private static int SpawnAtGrid(Session s, string type, float u, float v, float rotation, RoomState room, bool waiting,
        bool forceRoomGroup, int summonedBy, double now, Action<string, string> log)
    {
        Ground.ToWorld(u / Ground.CellSize, v / Ground.CellSize, out float x, out float y);
        return Spawn(s, type, x, y, rotation, room, waiting, forceRoomGroup, summonedBy, now, log);
    }

    // Creates one unit of a map type. Enemies that must die to clear the room go to the room group; chests, walls,
    // invulnerable and still things go to the extra group. Returns the shortId (0 = not created).
    private static int Spawn(Session s, string type, float x, float y, float rotation, RoomState room, bool waiting,
        bool forceRoomGroup, int summonedBy, double now, Action<string, string> log, bool neverCounts = false)
    {
        if (s.Data == null || !s.Data.HasType(type)) return 0;
        Enemies.EnemyType? t = Enemies.GetEnemyType(s.Data.Key(type));
        if (t == null) return 0;
        DungeonMapData.UnitExtras extras = s.Data.ExtrasOf(type);

        // Invisible unit with no gun = a controller of the original server (trap button, timers...). The client would show
        // an enemy health bar over nothing and the room could never be cleared if it cannot be shot, so it is not created.
        if (Enemies.IsControllerOnly(s.Data.Key(type)))
        {
            if (s.SkippedControllers.Add(type))
                log("DUNGEON", "[dungeon] '" + type + "' is an invisible controller without guns: not created");
            return 0;
        }

        bool counts = !neverCounts && !extras.ExcludeFromRoomClear &&
            (forceRoomGroup || (t.Team == 2 && !t.Invulnerable && t.OpensAfterHitSeconds <= 0f && extras.DpsChest == null &&
            !string.Equals(t.Behavior, "idle", StringComparison.OrdinalIgnoreCase)));
        string group = counts ? room.Group : room.Extra;

        int id = Enemies.CreateFromScript(s.Server, s.Connection, Role, s.Data.Key(type), x, y, rotation, group, room.Area,
            null, now, log);
        if (id == 0) return 0;
        if (waiting) Enemies.SetAsleep(s.Connection, id, true, 0f);
        else if (counts) Enemies.ForceAggro(s.Connection, id);   // waves and summons hunt the player at once

        s.Units[id] = new Tracked
        {
            Id = id,
            Type = type,
            Room = room,
            X = x,
            Y = y,
            SpawnedAt = now,
            SummonedBy = summonedBy,
            NextSummonAt = now + (extras.PeriodicSummon?.Seconds ?? 0f)
        };
        return id;
    }

    // Positions, deaths and the timed things of the tracked units.
    private static void UpdateUnits(Session s, double now, Action<string, string> log)
    {
        foreach (Tracked t in s.Units.Values.ToList())
        {
            if (t.Gone) continue;
            if (Enemies.GetPosition(s.Connection, t.Id, out float x, out float y))
            {
                t.X = x;
                t.Y = y;
                UpdateAlive(s, t, now, log);
                continue;
            }
            t.Gone = true;
            if (!t.Removed) OnUnitDied(s, t, now, log);
        }
        List<int> gone = s.Units.Values.Where(t => t.Gone).Select(t => t.Id).ToList();
        foreach (int id in gone) s.Units.Remove(id);
    }

    private static void UpdateAlive(Session s, Tracked t, double now, Action<string, string> log)
    {
        DungeonMapData.UnitExtras extras = s.Data!.ExtrasOf(t.Type);

        // Sequence eggs and other timed units end by themselves (as destroyed: the sequence goes on).
        if (extras.DurationSeconds > 0f && now >= t.SpawnedAt + extras.DurationSeconds)
        {
            Enemies.RemoveUnit(s.Connection, t.Id, true, now);
            return;
        }

        // DPS chest: the first hit starts the clock.
        if (extras.DpsChest != null)
        {
            if (t.FirstHitAt < 0 && Enemies.GetHealth(s.Connection, t.Id, out float health) && health < 0.999f)
            {
                t.FirstHitAt = now;
                Chat(s, "Break the chest in " + extras.DpsChest.Seconds.ToString("0", Inv) + " seconds!");
            }
            if (t.FirstHitAt >= 0 && now > t.FirstHitAt + extras.DpsChest.Seconds)
            {
                t.Removed = true;
                Enemies.RemoveUnit(s.Connection, t.Id, false, now);
                if (extras.DpsChest.Fail.Length > 0) Spawn(s, extras.DpsChest.Fail, t.X, t.Y, 0f, t.Room, false, false, 0, now, log);
                Chat(s, "Time is up: the chest closed.");
                log("DUNGEON", "[dungeon] DPS chest '" + t.Type + "' was not broken in time");
            }
            return;
        }

        bool asleep = Enemies.IsAsleep(s.Connection, t.Id);

        // Boss phase line: once, when the health passes the first phase.
        if (!t.PhaseBarked && !asleep && extras.Barks != null && extras.Barks.ContainsKey("phase"))
        {
            Enemies.EnemyType? type = Enemies.GetEnemyType(s.Data.Key(t.Type));
            float first = type?.Phases?.Count > 0 ? type.Phases.Max(f => f.BelowHealth) : 0.5f;
            if (Enemies.GetHealth(s.Connection, t.Id, out float health) && health <= first)
            {
                t.PhaseBarked = true;
                Bark(s, t.Id, t.Type, "phase");
            }
        }

        // Periodic summons while awake.
        DungeonMapData.PeriodicSummon? summon = extras.PeriodicSummon;
        if (summon != null && !asleep && now >= t.NextSummonAt && s.Data.HasType(summon.Type))
        {
            t.NextSummonAt = now + Math.Max(0.5f, summon.Seconds);
            if (summon.BelowHealth < 1f && (!Enemies.GetHealth(s.Connection, t.Id, out float health) || health > summon.BelowHealth))
                return;
            int alive = s.Units.Values.Count(u => u.SummonedBy == t.Id && !u.Gone);
            int amount = Math.Min(Math.Max(1, summon.Count), Math.Max(0, summon.Max - alive));
            for (int i = 0; i < amount; i++)
            {
                float angle = (float)(random.NextDouble() * 360.0) * MathF.PI / 180f;
                float sx = t.X + MathF.Sin(angle) * SummonDistance;
                float sy = t.Y + MathF.Cos(angle) * SummonDistance;
                KeepInArea(t.Room.Area, ref sx, ref sy);
                Spawn(s, summon.Type, sx, sy, float.NaN, t.Room, false, true, t.Id, now, log, !summon.CountsForRoomClear);
            }
        }
    }

    private static void OnUnitDied(Session s, Tracked t, double now, Action<string, string> log)
    {
        DungeonMapData.Loaded data = s.Data!;
        DungeonMapData.UnitExtras extras = data.ExtrasOf(t.Type);
        Enemies.EnemyType? type = Enemies.GetEnemyType(data.Key(t.Type));
        s.DeadTypes.Add(t.Type);
        if (type != null && type.Team == 2 && !type.Invulnerable) s.Kills++;
        if (extras.ChainReaction != null) TriggerChainReaction(s, t, extras.ChainReaction, now, log);

        // DPS chest broken in time: the reward appears.
        if (extras.DpsChest != null)
        {
            if (extras.DpsChest.Reward.Length > 0) Spawn(s, extras.DpsChest.Reward, t.X, t.Y, 0f, t.Room, false, false, 0, now, log);
            Chat(s, "Chest broken in time!");
            log("DUNGEON", "[dungeon] DPS chest '" + t.Type + "' broken in time");
            return;
        }

        // Sequence: the next form appears where this one died.
        string root = "";
        int nextIndex = -1;
        if (t.SequenceRoot.Length > 0)
        {
            root = t.SequenceRoot;
            nextIndex = t.SequenceIndex + 1;
        }
        else if (extras.Sequence != null && extras.Sequence.Count > 0)
        {
            root = t.Type;
            nextIndex = 0;
        }
        List<string>? chain = root.Length > 0 ? data.ExtrasOf(root).Sequence : null;
        if (chain != null && nextIndex >= 0 && nextIndex < chain.Count)
        {
            string next = chain[nextIndex];
            int id = Spawn(s, next, t.X, t.Y, float.NaN, t.Room, false, true, 0, now, log);
            if (id != 0 && s.Units.TryGetValue(id, out Tracked? born))
            {
                born.SequenceRoot = root;
                born.SequenceIndex = nextIndex;
                Bark(s, id, next, "onWake");
            }
            log("DUNGEON", "[dungeon] " + t.Type + " fell: next comes " + next + " (" + (nextIndex + 1) + "/" + chain.Count + " of the sequence of " + root + ")");
            return;
        }

        // Wreck or reward where it died, with the last line.
        int barkOn = t.Id;
        if (extras.OnDeath.Length > 0 && data.HasType(extras.OnDeath))
        {
            int id = Spawn(s, extras.OnDeath, t.X, t.Y, float.NaN, t.Room, false, false, 0, now, log);
            if (id != 0)
            {
                barkOn = id;
                if (extras.OnDeath.EndsWith("DEATH", StringComparison.OrdinalIgnoreCase))
                {
                    int wreck = id;
                    Schedule(s, now + WreckSeconds, "wreck", time =>
                    {
                        if (s.Units.TryGetValue(wreck, out Tracked? w)) w.Removed = true;
                        Enemies.RemoveUnit(s.Connection, wreck, false, time);
                    });
                }
            }
        }
        Bark(s, barkOn, t.Type, "onDeath");

        // A boss (or miniboss) takes its periodic summons with it.
        if ((type != null && type.Boss) || extras.Miniboss)
        {
            int taken = 0;
            foreach (Tracked u in s.Units.Values)
            {
                if (u.SummonedBy != t.Id || u.Gone) continue;
                u.Removed = true;
                if (Enemies.RemoveUnit(s.Connection, u.Id, true, now)) taken++;
            }
            log("DUNGEON", "[dungeon] " + (extras.Miniboss ? "miniboss " : "boss ") + t.Type + " defeated in room '" + t.Room.Name + "'" + (taken > 0 ? " (" + taken + " summoned unit(s) exploded)" : ""));
        }
    }

    // Chain reaction (the Bomb Factory's barrels and bombs): when one unit with "chainReaction" goes off, every other unit
    // with "chainReaction" within its radius goes off a moment later; their own blasts set off the next ones, so a whole
    // cluster of bombs fills the room with fire if the player shoots the wrong one.
    private static void TriggerChainReaction(Session s, Tracked source, DungeonMapData.ChainReaction rule, double now,
        Action<string, string> log)
    {
        int set = 0;
        foreach (Tracked u in s.Units.Values.ToList())
        {
            if (u == source || u.Gone || u.Removed || u.ChainQueued) continue;
            if (s.Data!.ExtrasOf(u.Type).ChainReaction == null) continue;
            float dx = u.X - source.X;
            float dy = u.Y - source.Y;
            if (dx * dx + dy * dy > rule.Radius * rule.Radius) continue;
            u.ChainQueued = true;
            int id = u.Id;
            set++;
            Schedule(s, now + Math.Max(0.05f, rule.DelaySeconds), "chain reaction", time =>
            {
                if (s.Units.TryGetValue(id, out Tracked? target) && !target.Gone) Enemies.RemoveUnit(s.Connection, id, true, time);
            });
        }
        if (set > 0)
            log("DUNGEON", "[dungeon] chain reaction: " + source.Type + " set off " + set + " unit(s) within " + Maps.Num(rule.Radius) + " u");
    }

    // ================================================================== end of the dungeon

    private static void CheckExit(Session s, double now, Action<string, string> log)
    {
        if (s.Finished) return;
        DungeonMapData.ExitRule? rule = s.Data!.File.Exit;
        bool done;
        if (rule != null && rule.When.Length > 0)
        {
            done = DungeonMapData.Check(rule.When, n => RoomCleared(s, n), t => s.DeadTypes.Contains(t));
        }
        else
        {
            // Without "exit": when every boss room is clear.
            List<RoomState> bosses = s.Rooms.Where(r => r.Encounter.Boss).ToList();
            done = bosses.Count > 0 && bosses.All(r => r.Cleared);
        }
        if (!done) return;
        Finish(s, now, log);
    }

    private static void Finish(Session s, double now, Action<string, string> log)
    {
        s.Finished = true;

        // What is left of the boss rooms explodes (the original did this when the boss died).
        int taken = 0;
        foreach (RoomState room in s.Rooms.Where(r => r.Encounter.Boss))
        {
            foreach (Tracked t in s.Units.Values.Where(u => u.Room == room && !u.Gone)) t.Removed = true;
            taken += Enemies.RemoveGroup(s.Connection, room.Group, true, now);
            room.Cleared = true;
        }

        Chat(s, s.Entry.Label + " completed! The exit carrier is arriving.");
        log("DUNGEON", "[dungeon] " + s.Entry.Label + " completed in " + (now - s.StartedAt).ToString("0", Inv) + " s" +
            (taken > 0 ? " (" + taken + " remaining unit(s) exploded)" : ""));

        Schedule(s, now + PortalDelay, "exit portal", time => CreateExitPortal(s, time, log));
        float auto = DungeonCatalog.AutoExitSeconds;
        if (auto > 0f)
            Schedule(s, now + PortalDelay + auto, "automatic exit", _ => LeaveToNexus(s, "automatic exit (the player did not enter the carrier)", true));
    }

    private static void CreateExitPortal(Session s, double now, Action<string, string> log)
    {
        if (s.ExitPortal != 0 || s.Map == null) return;
        string pointName = s.Data?.File.Exit?.Point ?? "portal";
        Maps.Point point;
        if (s.Map.Points.TryGetValue(pointName, out Maps.Point found)) point = found;
        else if (s.Map.Checkpoints.Count > 0) point = s.Map.Checkpoints[^1].Point;
        else point = s.Map.Spawn;
        point.World(out float x, out float y);

        int id = WorldObjects.NewShortId();
        foreach (NetConnection member in s.Members.ToList())
            WorldObjects.SendExitPortal(s.Server, member, id, x, y, nexusEndpoint, nexusHostId, "Nexus", gameVersion, nexusTags, now);
        s.ExitPortal = id;
        s.Objects.Add((id, x, y));
        log("DUNGEON", "[dungeon] exit carrier " + id + " at (" + Maps.Num(x) + ", " + Maps.Num(y) + ") -> Nexus " + nexusEndpoint);
    }

    // Everybody in the instance goes back to the Nexus (automatic exit, map error).
    private static void LeaveToNexus(Session s, string reason, bool markFinished)
    {
        if (s.Left || Connected(s).Count == 0) return;
        if (ReturnToNexus == null)
        {
            logger("DUNGEON", "[dungeon] " + reason + ": no link to the Nexus (ReturnToNexus is null)");
            return;
        }
        s.Left = true;
        foreach (NetConnection member in Connected(s)) SendMemberToNexus(s, member, reason, markFinished);
    }

    // One player goes back to the Nexus (/leave); the others stay. Called by the update loop too: a finished dungeon is
    // marked in the account of the player who leaves.
    private static void SendMemberToNexus(Session s, NetConnection member, string reason, bool markFinished)
    {
        if (member.Status != NetConnectionStatus.Connected || ReturnToNexus == null) return;
        using Accounts.Scope accountScope = Accounts.UseFor(member);
        if (markFinished && s.Finished) Accounts.MarkDungeonFinished(s.Entry.Type);
        ReturnToNexus(s.Server, member, "[dungeon] " + reason);
    }

}
