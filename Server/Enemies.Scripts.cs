using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lidgren.Network;

// Scripts for the walker tutorial and other scripted rooms: create units by script, group wake-up and removal.
public static partial class Enemies
{
    // ================================================================== scripts (Tutorial)

    // Registers a type in the code. enemies.json, if it has a type with the same key, takes precedence.
    public static void RegisterDefaultType(string key, EnemyType type)
    {
        lock (Game.Lock)
        {
            if (string.IsNullOrWhiteSpace(type.Name)) type.Name = key;
            type.Template ??= "auto:weak";
            type.Behavior = NormalizeBehavior(type.Behavior);
            type.Guns ??= new List<int>();
            type.Phases = (type.Phases ?? new List<BossPhase>()).Where(f => f != null).OrderByDescending(f => f.BelowHealth).ToList();
            defaultTypes[key] = type;
        }
    }

    // Type by key: first enemies.json, then the ones registered in the code.
    public static EnemyType? GetEnemyType(string key)
    {
        lock (Game.Lock)
        {
            if (config.Types.TryGetValue(key, out EnemyType? fromFile)) return fromFile;
            return defaultTypes.TryGetValue(key, out EnemyType? fallback) ? fallback : null;
        }
    }

    // Creates a script unit at (x, y) and sends the SpawnUnitsBatch at once.
    // rotation: degrees (0 = +Y); NaN = facing the player. invulnerable: null = the type's value.
    // Returns the shortId, or 0 if it could not (type without a template in the catalog, unit limit...).
    public static int CreateFromScript(NetServer server, NetConnection connection, string role, string key, float x, float y,
        float rotation, string group, Ground.Rect? area, bool? invulnerable, double now, Action<string, string> log)
    {
        lock (Game.Lock)
        {
            EnemyType? type = GetEnemyType(key);
            if (type == null)
            {
                if (reportedFailures.Add("script|" + key))
                    log("ENEMY", "script: the type '" + key + "' does not exist (neither in enemies.json nor in the code)");
                return 0;
            }

            Instance inst = GetInstance(server, connection, role, now);
            Enemy? enemy = CreateEnemy(inst, key, type, x, y, false, now, log);
            if (enemy == null) return 0;

            enemy.Group = group ?? "";
            enemy.Area = area;
            if (invulnerable.HasValue) enemy.Invulnerable = invulnerable.Value;
            if (!float.IsNaN(rotation)) enemy.Rot = NormalizeAngle(rotation);

            SendSpawns(inst, new List<Enemy> { enemy }, now);
            return enemy.ShortId;
        }
    }

    // How many units of the group still exist on this connection.
    public static int CountAlive(NetConnection connection, string group)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return 0;
            return inst.Enemies.Values.Count(i => i.Group == group);
        }
    }

    public static bool IsAlive(NetConnection connection, int shortId)
    {
        if (shortId == 0) return false;
        lock (Game.Lock)
        {
            return instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst) && inst.Enemies.ContainsKey(shortId);
        }
    }

    public static bool GetPosition(NetConnection connection, int shortId, out float x, out float y)
    {
        lock (Game.Lock)
        {
            if (instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst) &&
                inst.Enemies.TryGetValue(shortId, out Enemy? enemy))
            {
                x = enemy.X;
                y = enemy.Y;
                return true;
            }
            x = 0f;
            y = 0f;
            return false;
        }
    }

    // Turns the invulnerability of a whole group on or off (walls locked until the room is cleared).
    public static void SetInvulnerable(NetConnection connection, string group, bool val)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return;
            foreach (Enemy enemy in inst.Enemies.Values)
            {
                if (enemy.Group == group) enemy.Invulnerable = val;
            }
        }
    }

    // ------------------------------------------------------------------ one unit (dungeon server)

    // Health of one unit as a fraction (0..1). False if the unit does not exist (anymore) for this connection.
    public static bool GetHealth(NetConnection connection, int shortId, out float fraction)
    {
        lock (Game.Lock)
        {
            fraction = 0f;
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return false;
            if (!inst.Enemies.TryGetValue(shortId, out Enemy? enemy)) return false;
            fraction = enemy.MaxHp > 0f ? Math.Clamp(enemy.Health / enemy.MaxHp, 0f, 1f) : 0f;
            return true;
        }
    }

    public static void SetUnitInvulnerable(NetConnection connection, int shortId, bool val)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return;
            if (inst.Enemies.TryGetValue(shortId, out Enemy? enemy)) enemy.Invulnerable = val;
        }
    }

    // Visual effect of one unit (statefulEffects name; null or empty turns it off). Goes in the next UpdateUnitsBatch.
    public static void SetVisualEffect(NetConnection connection, int shortId, string? effect)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return;
            if (!inst.Enemies.TryGetValue(shortId, out Enemy? enemy)) return;
            enemy.BaseEffect = MonoHash.StatefulEffectId(effect);
            if (enemy.ShieldUntil > 0) return;   // the shield effect stays until the shield ends
            enemy.EffectId = enemy.BaseEffect;
            enemy.EffectChanged = true;
        }
    }

    // Removes one unit. destroyed = true: explodes (DestroyedFromDamage, no XP); false: disappears (Fizzled).
    public static bool RemoveUnit(NetConnection connection, int shortId, bool destroyed, double now)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return false;
            if (!inst.Enemies.TryGetValue(shortId, out Enemy? enemy)) return false;
            Remove(inst, new List<Enemy> { enemy }, destroyed ? ReasonDestroyedByDamage : ReasonVanished,
                destroyed ? ResultDamage : ResultNone, now);
            return true;
        }
    }

    // The unit hunts the player from now on, however far away it is (waves and summons: they must not wander off until the
    // player comes within the type's aggro radius).
    public static void ForceAggro(NetConnection connection, int shortId)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return;
            if (!inst.Enemies.TryGetValue(shortId, out Enemy? enemy)) return;
            if (!enemy.Asleep) enemy.Aggro = true;
        }
    }

    // True if the type is only a "controller" of the original server: invisible art and no armed gun. The client draws
    // nothing for it but still shows a health bar and lets it count as an enemy, so the dungeon server does not create it.
    public static bool IsControllerOnly(string key)
    {
        lock (Game.Lock)
        {
            EnemyType? type = GetEnemyType(key);
            if (type == null) return false;
            GameUnit? model = Resolver(type, out _);
            if (model == null) return false;
            return model.Art.StartsWith("!EnemyInvisible", StringComparison.OrdinalIgnoreCase) && !model.Armed;
        }
    }

    // Names of the living units of a group (the dungeon tells the player what is still holding a room).
    public static List<string> AliveNamesInGroup(NetConnection connection, string group)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return new List<string>();
            return inst.Enemies.Values.Where(e => e.Group == group).Select(e => e.Type.Name).ToList();
        }
    }

    // True if the unit is still waiting (pre-placed and not woken up yet).
    public static bool IsAsleep(NetConnection connection, int shortId)
    {
        lock (Game.Lock)
        {
            return instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst) &&
                inst.Enemies.TryGetValue(shortId, out Enemy? enemy) && enemy.Asleep;
        }
    }

    // Pre-placed: the unit waits idle, with no target. wakeRadius > 0: wakes up when the player gets
    // to that distance (units); 0: only by the script (WakeGroup) or with a hit.
    public static void SetAsleep(NetConnection connection, int shortId, bool asleep, float wakeRadius)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return;
            if (!inst.Enemies.TryGetValue(shortId, out Enemy? enemy)) return;
            enemy.Asleep = asleep;
            enemy.WakeRadius = Math.Max(0f, wakeRadius);
            if (asleep)
            {
                enemy.Aggro = false;
                enemy.Vel = 0f;
            }
        }
    }

    // Wakes the whole group (they start aiming at the player). Returns how many woke up now.
    public static int WakeGroup(NetConnection connection, string group, double now)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return 0;
            int awake = 0;
            foreach (Enemy enemy in inst.Enemies.Values)
            {
                if (enemy.Group != group || !enemy.Asleep) continue;
                Wake(enemy, now);
                awake++;
            }
            return awake;
        }
    }

    // Wakes one unit (the script wakes the hangar turrets one after another).
    public static void WakeUnit(NetConnection connection, int shortId, double now)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return;
            if (inst.Enemies.TryGetValue(shortId, out Enemy? enemy) && enemy.Asleep) Wake(enemy, now);
        }
    }

    // True if anyone in the group already woke up (or if the group has no one left waiting).
    public static bool GroupAwake(NetConnection connection, string group)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return true;
            bool any = false;
            foreach (Enemy enemy in inst.Enemies.Values)
            {
                if (enemy.Group != group) continue;
                if (!enemy.Asleep) return true;
                any = true;
            }
            return !any;
        }
    }

    // Whoever was waiting and got provoked (too close or hit) wakes their group.
    private static void WakeGroupInternal(Instance inst, Enemy provoked, double now)
    {
        if (string.IsNullOrEmpty(provoked.Group))
        {
            Wake(provoked, now);
            return;
        }
        foreach (Enemy enemy in inst.Enemies.Values)
        {
            if (enemy.Group == provoked.Group && enemy.Asleep) Wake(enemy, now);
        }
    }

    private static void Wake(Enemy enemy, double now)
    {
        enemy.Asleep = false;
        enemy.Aggro = true;
        // The first shot delay counts from now (nobody fires the instant they wake up).
        if (enemy.ActiveAt < now) enemy.ActiveAt = now;
    }

    // Destination of an "escort" unit (the ally shows the way). active = false: goes back to following the player.
    public static void SetDestination(NetConnection connection, int shortId, float x, float y, bool active)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return;
            if (!inst.Enemies.TryGetValue(shortId, out Enemy? enemy)) return;
            enemy.HasDest = active;
            enemy.DestX = x;
            enemy.DestY = y;
        }
    }

    // Removes a whole group. destroyed = true: explodes (DestroyedFromDamage); false: disappears (Fizzled).
    public static int RemoveGroup(NetConnection connection, string group, bool destroyed, double now)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return 0;
            List<Enemy> ofGroup = inst.Enemies.Values.Where(i => i.Group == group).ToList();
            Remove(inst, ofGroup, destroyed ? ReasonDestroyedByDamage : ReasonVanished, destroyed ? ResultDamage : ResultNone, now);
            return ofGroup.Count;
        }
    }

    // Removes everything of this connection (the script restarted). Waves in progress stop.
    public static int RemoveAll(NetConnection connection, double now)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return 0;
            int total = inst.Enemies.Count;
            Remove(inst, inst.Enemies.Values.ToList(), ReasonVanished, ResultNone, now);
            inst.Encounter = null;
            inst.CurrentWave = -1;
            inst.WaveScheduled = -1;
            inst.Done = false;
            return total;
        }
    }

    // Most living enemies of this instance at once (default 140: a normal dungeon only has the rooms near the player alive).
    // A map that wants more (a catalog of every enemy, for example) says so in its "values": {"maxEnemies": 1000}.
    public static void SetMaxEnemies(NetServer server, NetConnection connection, string role, int count, double now)
    {
        lock (Game.Lock)
        {
            Instance inst = GetInstance(server, connection, role, now);
            inst.MaxEnemies = Math.Clamp(count, 1, 5000);
        }
    }

    // The player's current room: waves and commands (/spawn, /enemy) use this area.
    public static void SetDefaultArea(NetServer server, NetConnection connection, string role, Ground.Rect? area, double now)
    {
        lock (Game.Lock)
        {
            Instance inst = GetInstance(server, connection, role, now);
            inst.DefaultArea = area;
        }
    }

    // The player's plane spawned again: the estimated velocity restarts from zero (lead aim).
    public static void PlayerRespawned(NetConnection connection)
    {
        lock (Game.Lock)
        {
            if (instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) ResetSample(inst, connection);
        }
    }

    // A second (third...) player joins the instance of "primary": from now on everything of the instance goes to them too.
    // Creates the instance if the first player has not made any unit yet.
    public static void AddMember(NetServer server, NetConnection primary, NetConnection joining, string role, double now)
    {
        lock (Game.Lock)
        {
            Instance inst = GetInstance(server, primary, role, now);
            if (!inst.Members.Contains(joining)) inst.Members.Add(joining);
            instances[joining.RemoteUniqueIdentifier] = inst;
        }
    }

    // The new player gets what already exists in the instance (units, gate pieces, boss counter): the others have it.
    public static void SendStateTo(NetConnection connection, double now)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return;
            SendSpawns(inst, inst.Enemies.Values.ToList(), now, connection);
            SendBossCountTo(inst, connection);
        }
    }

    // Program.cs reports it when the connection drops: this player's enemies are discarded.
    public static void Disconnected(NetConnection? connection, Action<string, string> log)
    {
        if (connection == null) return;
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return;
            if (!inst.Members.Contains(connection)) return;   // older connection of the same client (it already moved to another server)
            instances.Remove(connection.RemoteUniqueIdentifier);
            inst.Members.Remove(connection);
            inst.Tracks.Remove(connection.RemoteUniqueIdentifier);
            inst.Known.Remove(connection.RemoteUniqueIdentifier);
            if (inst.Members.Count > 0)
            {
                // Other players stay: the instance goes on with the first of them.
                if (inst.Connection == connection)
                {
                    inst.Connection = inst.Members[0];
                    inst.Peer = inst.Connection.RemoteUniqueIdentifier;
                }
                log("ENEMY", inst.Role + " a player left the instance (" + inst.Members.Count + " left)");
                return;
            }
            log("ENEMY", inst.Role + " session ended: " + inst.Kills + " enemy(ies) killed, " +
                inst.Enemies.Count + " still alive discarded");
        }
    }

}
