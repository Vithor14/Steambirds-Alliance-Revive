using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lidgren.Network;

// Instances per player and encounters: waves, enemy creation and boss phases.
public static partial class Enemies
{
    // ================================================================== instance and encounter

    private static Instance GetInstance(NetServer server, NetConnection connection, string role, double now)
    {
        long peer = connection.RemoteUniqueIdentifier;
        // Same client on a new connection (it switched servers): the old instance belongs to the old server.
        if (!instances.TryGetValue(peer, out Instance? inst) || !inst.Members.Contains(connection))
        {
            inst = new Instance { Peer = peer, Server = server, Connection = connection, Role = "[" + role + "]", LastTick = now };
            inst.Filtering = ViewRadiusOf(inst) > 0f;
            inst.Members.Add(connection);
            instances[peer] = inst;
        }
        return inst;
    }

    private static EncounterConfig? PickEncounter(string role)
    {
        return config.Encounters.FirstOrDefault(e => e.Active && string.Equals(e.Server, role, StringComparison.OrdinalIgnoreCase));
    }

    private static void StartEncounter(Instance inst, EncounterConfig encounter, double now, Action<string, string> log)
    {
        Remove(inst, inst.Enemies.Values.ToList(), ReasonVanished, ResultNone, now);
        inst.Encounter = encounter;
        inst.CurrentWave = -1;
        inst.Done = false;
        inst.WaveScheduled = encounter.Waves.Count > 0 ? 0 : -1;
        inst.WaveScheduledFor = now + Math.Max(0f, encounter.StartAfterSeconds);

        log("ENEMY", inst.Role + " encounter '" + encounter.Name + "' starts in " + Num(encounter.StartAfterSeconds) + "s (" +
            encounter.Waves.Count + " wave(s))");
        if (encounter.Waves.Count > 0)
            Warn(inst, "Enemies arriving in " + Num(encounter.StartAfterSeconds) + " seconds! (" + encounter.Waves.Count + " waves)");
    }

    private static void JumpToWave(Instance inst, EncounterConfig encounter, int index, double now)
    {
        Remove(inst, inst.Enemies.Values.ToList(), ReasonVanished, ResultNone, now);
        inst.Encounter = encounter;
        inst.Done = false;
        inst.CurrentWave = index - 1;
        inst.WaveScheduled = index;
        inst.WaveScheduledFor = now + 1.0;
    }

    // Starts the next wave when the current one was completely destroyed; closes the encounter at the end.
    private static void Steer(Instance inst, double now, Action<string, string> log)
    {
        EncounterConfig? encounter = inst.Encounter;
        if (encounter == null) return;

        if (inst.WaveScheduled >= 0)
        {
            // The wave waits for the player's plane to be in the air.
            if (now >= inst.WaveScheduledFor && inst.HasPlayer && inst.PlayerAlive)
            {
                int index = inst.WaveScheduled;
                inst.WaveScheduled = -1;
                StartWave(inst, encounter, index, now, log);
            }
            return;
        }

        if (inst.Done || inst.CurrentWave < 0) return;
        if (inst.Enemies.Values.Any(i => i.FromWave)) return;

        int next = inst.CurrentWave + 1;
        if (next < encounter.Waves.Count)
        {
            float interval = Math.Max(0f, encounter.Waves[next].DelayBeforeSeconds);
            inst.WaveScheduled = next;
            inst.WaveScheduledFor = now + interval;
            log("ENEMY", inst.Role + " wave " + (inst.CurrentWave + 1) + "/" + encounter.Waves.Count + " cleared; next in " + Num(interval) + "s");
            Warn(inst, "Wave " + (inst.CurrentWave + 1) + " completed! Next in " + Num(interval) + " seconds.");
            return;
        }

        inst.Done = true;
        log("ENEMY", inst.Role + " encounter '" + encounter.Name + "' completed (" + inst.Kills + " kill(s) in the session)");
        Warn(inst, string.IsNullOrWhiteSpace(encounter.FinalMessage) ? "Encounter completed!" : encounter.FinalMessage);

        if (encounter.Repeat)
        {
            inst.Done = false;
            inst.CurrentWave = -1;
            inst.WaveScheduled = 0;
            inst.WaveScheduledFor = now + Math.Max(1f, encounter.RepeatInterval);
            Warn(inst, "The encounter restarts in " + Num(encounter.RepeatInterval) + " seconds.");
        }
    }

    private static void StartWave(Instance inst, EncounterConfig encounter, int index, double now, Action<string, string> log)
    {
        if (index < 0 || index >= encounter.Waves.Count) return;
        WaveConfig wave = encounter.Waves[index];
        inst.CurrentWave = index;

        var added2 = new List<Enemy>();
        foreach (GroupConfig group in wave.Groups)
        {
            int count = Math.Clamp(group.Count, 0, 30);
            if (count == 0) continue;
            EnemyType? type = GetEnemyType(group.Type);
            if (type == null)
            {
                log("ENEMY", inst.Role + " wave " + (index + 1) + ": type '" + group.Type + "' does not exist in enemies.json");
                continue;
            }

            // The group arrives from a single direction: spread in an arc of up to 100 degrees.
            float baseAngle = (float)(rng.NextDouble() * 360.0);
            float arc = count > 1 ? Math.Min(100f, 25f * (count - 1)) : 0f;
            for (int i = 0; i < count; i++)
            {
                float x;
                float y;
                if (group.X.HasValue && group.Y.HasValue)
                {
                    float r = count > 1 ? Math.Max(0f, group.Spread) : 0f;
                    float a = (baseAngle + i * 360f / count) * Rad;
                    x = group.X.Value + MathF.Sin(a) * r;
                    y = group.Y.Value + MathF.Cos(a) * r;
                    ConfineTo(inst.DefaultArea, false, ref x, ref y, DestMargin);
                }
                else
                {
                    float a = baseAngle - arc * 0.5f + (count > 1 ? arc * i / (count - 1) : 0f);
                    PositionNearPlayer(inst, a, Math.Max(4f, group.Distance), out x, out y);
                }

                Enemy? enemy = CreateEnemy(inst, group.Type, type, x, y, true, now, log);
                if (enemy != null) added2.Add(enemy);
            }
        }

        SendSpawns(inst, added2, now);

        string name = string.IsNullOrWhiteSpace(wave.Name) ? "" : ": " + wave.Name;
        log("ENEMY", inst.Role + " wave " + (index + 1) + "/" + encounter.Waves.Count + name + " -> " + added2.Count + " enemy(ies): " +
            string.Join(", ", added2.Select(i => i.ShortId + " " + Describe(i) + " health " + Num(i.MaxHp) + " at (" + Num(i.X) + ", " + Num(i.Y) + ")")));
        if (added2.Count == 0)
            log("ENEMY", inst.Role + " wave " + (index + 1) + " came out empty (types without a template in the catalog?) and is skipped");

        Warn(inst, !string.IsNullOrWhiteSpace(wave.Message)
            ? wave.Message
            : "Wave " + (index + 1) + "/" + encounter.Waves.Count + name);
        foreach (Enemy boss in added2.Where(i => i.Boss)) Warn(inst, "CHEFE: " + boss.Type.Name + "!");
    }

    // ================================================================== enemy creation

    // Creates the enemy in the server state. The SpawnUnitsBatch goes out later, in SendSpawns.
    private static Enemy? CreateEnemy(Instance inst, string key, EnemyType type, float x, float y, bool fromWave, double now,
        Action<string, string> log)
    {
        if (inst.Enemies.Count >= inst.MaxEnemies)
        {
            log("ENEMY", inst.Role + " limit of " + inst.MaxEnemies + " living enemies: '" + key + "' not created");
            return null;
        }

        GameUnit? model = Resolver(type, out string reason);
        if (model == null)
        {
            if (reportedFailures.Add(key + "|" + type.Template + "|" + catalog.Count))
                log("ENEMY", "type '" + key + "' (template '" + type.Template + "') was not created: " + reason);
            return null;
        }

        var enemy = new Enemy
        {
            ShortId = nextShortId++,
            TypeKey = key,
            Type = type,
            Model = model,
            FromWave = fromWave,
            // Wave units come for the player at once (the Arena's waves spawn far from the player and used to wander).
            Aggro = fromWave,
            Boss = type.Boss,
            X = x,
            Y = y,
            HomeX = x,
            HomeY = y,
            Behavior = TypeFlight(type),
            SpawnedAt = now,
            OrbitDirection = rng.Next(2) == 0 ? -1f : 1f,
            PatrolX = x,
            PatrolY = y,
            Team = type.Team == 0 || type.Team == 1 || type.Team == 2 ? type.Team : EnemyTeam,
            Area = inst.DefaultArea,
            FreeRoam = type.FreeRoam,
            Invulnerable = type.Invulnerable,
            Homing = type.HomingBullets
        };

        enemy.Ai = AttachBrain(key, EffectiveAi(type), log);

        // Spawns facing the player.
        enemy.Rot =inst.HasPlayer ? Heading(x, y, inst.PlayerX, inst.PlayerY) : (float)(rng.NextDouble() * 360.0 - 180.0);

        float health = type.Health > 0f ? type.Health : model.MaxHp;
        if (health <= 0f) health = 100f;
        health *= Positive(type.HealthMultiplier) * Positive(config.GlobalHealthMultiplier);
        if (type.MaxHealth > 0f) health = Math.Min(health, type.MaxHealth);
        enemy.MaxHp = Math.Max(1f, health);
        enemy.Health = enemy.MaxHp;

        enemy.VelMax = Math.Max(0f, model.VelMax) * Positive(type.SpeedMultiplier);
        enemy.VelMin = Math.Clamp(model.VelMin * Positive(type.SpeedMultiplier), 0f, enemy.VelMax);
        enemy.MaxTurn = (model.Turn > 1f ? model.Turn : 120f) * Positive(type.TurnMultiplier);

        // With a capsule (useSpawnDuration) the client shows the launch capsule for spawnDuration seconds.
        double spawn = model.Capsule ? Math.Max(0.5f, model.SpawnDuration) : 0.3;
        enemy.ActiveAt = now + spawn;
        enemy.Vel = model.Capsule ? 0f : (enemy.VelMin + enemy.VelMax) * 0.5f;

        foreach (GameGun gun in model.Guns)
            enemy.Guns.Add(new GunState { Model = gun, Ammo = Math.Max(1, gun.Burst) });
        enemy.ActiveGuns = new List<int>(type.Guns ?? new List<int>());
        enemy.BaseEffect = MonoHash.StatefulEffectId(type.VisualEffect);
        enemy.EffectId = enemy.BaseEffect;

        inst.Enemies[enemy.ShortId] = enemy;
        return enemy;
    }

    private static void PositionNearPlayer(Instance inst, float angleDegrees, float distance, out float x, out float y)
    {
        float cx = inst.HasPlayer ? inst.PlayerX : config.Arena.CenterX;
        float cy = inst.HasPlayer ? inst.PlayerY : config.Arena.CenterY;
        if (!inst.HasPlayer && inst.DefaultArea.HasValue)
            Ground.ToWorld(inst.DefaultArea.Value.CenterA, inst.DefaultArea.Value.CenterB, out cx, out cy);
        float a = angleDegrees * Rad;
        x = cx + MathF.Sin(a) * distance;
        y = cy + MathF.Cos(a) * distance;
        ConfineTo(inst.DefaultArea, false, ref x, ref y, DestMargin);
    }

    private static void ConfineToArena(ref float x, ref float y, float margin)
    {
        ArenaConfig arena = config.Arena;
        float limit = Math.Max(5f, arena.Limit - margin);
        x = Math.Clamp(x, arena.CenterX - limit, arena.CenterX + limit);
        y = Math.Clamp(y, arena.CenterY - limit, arena.CenterY + limit);
    }

    // Clamps the point to the area (script room, in tiles rotated 45 degrees) or, without an area, to the arena square.
    // margin in world units (only applies to the area; the square uses 3, as before). freeRoam: does not clamp.
    private static void ConfineTo(Ground.Rect? area, bool freeRoam, ref float x, ref float y, float margin)
    {
        if (freeRoam) return;
        if (!area.HasValue)
        {
            ConfineToArena(ref x, ref y, 3f);
            return;
        }
        Ground.ToGrid(x, y, out float a, out float b);
        area.Value.Confine(ref a, ref b, margin / Ground.CellSize);
        Ground.ToWorld(a, b, out x, out y);
    }

    private static bool Inside(Ground.Rect area, float x, float y, float margin)
    {
        Ground.ToGrid(x, y, out float a, out float b);
        return area.Contains(a, b, margin / Ground.CellSize);
    }

}
