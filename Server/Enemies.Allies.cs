using Lidgren.Network;

// Units of the player (ServerSpawnGreenTeamAlly) and status effects on the enemies of the server.
//
// Allies: the engineer's turrets ("allyturret*") and the raptor's mines ("allymine*") are units that the original
// server created and ran. The client only sends the request (template, count, position) and spends the energy, so
// until now nothing appeared. Here they are Enemy objects of the green team (teamIndex 1) that never move:
//  - turret: shoots the nearest enemy inside the gun range. The shot goes out as GunFire (visual) and the damage comes
//    off the enemy on the server, like the poison clouds (Skills\CloudDamage.cs).
//  - mine: arms after a second; when an enemy gets close it blows up and hurts the enemies around it (DamageArea).
// Reconstruction: the original lifetimes, limits and blast radius are not in the game data. They are the public
// fields below, so a host can change them.
//
// Stun: the bandit's EMP orb hits with effect Confuse; the client reports it in ServerReportBulletHit (effect,
// effectAmount, effectTime) and the server keeps the enemy still, without shooting, for that time.
public static partial class Enemies
{
    // Living turrets / mines of one player; the oldest goes away when a new one would pass the limit.
    public static int AllyTurretLimit = 4;
    public static int AllyMineLimit = 8;
    // Other allies (anchor...): same rule.
    public static int AllyOtherLimit = 4;

    // A mine that nobody touched disappears after this time (seconds).
    public static float MineLifetimeSeconds = 120f;
    // An enemy this close to the mine (plus its own radius) sets it off.
    public static float MineTriggerRadius = 2.5f;
    // The blast hurts enemies inside this radius.
    public static float MineBlastRadius = 4f;

    // Stun lasts effectTime seconds of the hit; without it, this long. Bosses never stay longer than the boss limit.
    public static float DefaultStunSeconds = 3f;
    public static float MaxStunSeconds = 8f;
    public static float BossStunSeconds = 1.5f;

    private const int AllyTeam = 1;     // Team.greenTeam
    private const int KindTurret = 1;
    private const int KindMine = 2;
    private const int KindOther = 3;
    private const double MineArmingSeconds = 1.0;
    private const double AllySpawnSeconds = 0.3;

    // EffectType values that stop an enemy (Jam, Confuse, Bash).
    private static bool IsStunEffect(int effect) => effect == 2 || effect == 7 || effect == 16;

    private static readonly Dictionary<string, EnemyType> allyTypes = new Dictionary<string, EnemyType>(StringComparer.Ordinal);

    private static int KindOf(string template)
    {
        if (template.StartsWith("allyturret", StringComparison.OrdinalIgnoreCase)) return KindTurret;
        if (template.StartsWith("allymine", StringComparison.OrdinalIgnoreCase)) return KindMine;
        return KindOther;
    }

    private static EnemyType AllyType(string template, int kind)
    {
        if (allyTypes.TryGetValue(template, out EnemyType? ready)) return ready;
        var type = new EnemyType
        {
            Name = template,
            Template = template,
            Behavior = "idle",
            Team = AllyTeam,
            FreeRoam = true,
            // The mine's own health is 1E9 in the data; nothing of the server hurts it anyway.
            Invulnerable = kind == KindMine,
            Health = kind == KindMine ? 100f : 0f
        };
        allyTypes[template] = type;
        return type;
    }

    // Max health of a unit of the catalog (0 = unknown). Used to turn the health fraction of a plane into health points.
    public static float UnitMaxHealth(string name)
    {
        lock (Game.Lock)
        {
            GameUnit? unit = FindInCatalog(name);
            return unit == null ? 0f : unit.MaxHp;
        }
    }

    // True if this unit id is an ally (turret, mine...) that the server created for the player's instance.
    public static bool IsAllyUnit(NetConnection? connection, int shortId)
    {
        if (connection == null) return false;
        lock (Game.Lock)
        {
            return instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst) &&
                inst.Enemies.TryGetValue(shortId, out Enemy? unit) && unit.Team == AllyTeam;
        }
    }

    // ================================================================== spawn

    // ServerSpawnGreenTeamAlly. The position is the owner's plane plus the offset of the item (angle and distance,
    // relative to the plane heading or absolute). Returns how many units were created.
    public static int SpawnAllies(NetConnection connection, int ownerShortId, string template, int count, float originX, float originY,
        float ownerHeading, float spawnAngle, float spawnDistance, bool relativeToHeading, double now, Action<string, string> log)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst))
            {
                log("ENEMY", "ally '" + template + "' not created: this player has no enemy instance here (the Nexus has none)");
                return 0;
            }

            if (FindInCatalog(template) == null)
            {
                log("ENEMY", "ally '" + template + "' not created: it is not in the unit catalog " + CatalogPath());
                return 0;
            }

            long owner = connection.RemoteUniqueIdentifier;
            int kind = KindOf(template);
            EnemyType type = AllyType(template, kind);
            count = Math.Clamp(count, 1, 5);

            // Room for the new ones: the oldest of the same kind of this player goes away.
            int limit = Math.Max(count, kind == KindTurret ? AllyTurretLimit : kind == KindMine ? AllyMineLimit : AllyOtherLimit);
            List<Enemy> mine = inst.Enemies.Values.Where(i => i.AllyKind == kind && i.OwnerPeer == owner)
                .OrderBy(i => i.SpawnedAt).ToList();
            int excess = mine.Count + count - limit;
            if (excess > 0) Remove(inst, mine.Take(excess).ToList(), ReasonVanished, ResultNone, now);

            float angle = (relativeToHeading ? ownerHeading + spawnAngle : spawnAngle) * Rad;
            float centerX = originX + MathF.Sin(angle) * spawnDistance;
            float centerY = originY + MathF.Cos(angle) * spawnDistance;

            var added = new List<Enemy>();
            for (int i = 0; i < count; i++)
            {
                // Several at once stand side by side, across the offset direction.
                float side = (i - (count - 1) / 2f) * 1.5f;
                float x = centerX + MathF.Cos(angle) * side;
                float y = centerY - MathF.Sin(angle) * side;

                Enemy? ally = CreateEnemy(inst, template, type, x, y, false, now, log);
                if (ally == null) continue;
                ally.AllyKind = kind;
                ally.OwnerPeer = owner;
                ally.OwnerShortId = ownerShortId;
                ally.Rot = ownerHeading;
                ally.ActiveAt = now + (kind == KindMine ? MineArmingSeconds : AllySpawnSeconds);
                if (kind == KindMine && MineLifetimeSeconds > 0f) ally.ExpiresAt = now + MineLifetimeSeconds;
                added.Add(ally);
            }

            SendSpawns(inst, added, now);
            log("ENEMY", inst.Role + " " + added.Count + "x ally '" + template + "' created at (" + Num(centerX) + ", " +
                Num(centerY) + ")" + (kind == KindMine ? ", lasts " + Num(MineLifetimeSeconds) + "s" : ""));
            return added.Count;
        }
    }

    // ================================================================== tick

    private static NetConnection? OwnerConnection(Instance inst, long peer)
    {
        foreach (NetConnection member in inst.Members)
        {
            if (member.RemoteUniqueIdentifier == peer && member.Status == NetConnectionStatus.Connected) return member;
        }
        return inst.Connection;
    }

    // Enemies that an ally can hurt: real enemies (AI team) that are up, not walls, chests or invulnerable props.
    private static bool IsHostileTarget(Enemy enemy, double now)
    {
        return enemy.Team == EnemyTeam && !enemy.Invulnerable && !enemy.Model.Wall && enemy.Type.OpensAfterHitSeconds <= 0f &&
            enemy.Health > 0f && now >= enemy.ActiveAt;
    }

    private static void UpdateAllies(Instance inst, double now, Action<string, string> log)
    {
        List<Enemy>? allies = null;
        foreach (Enemy enemy in inst.Enemies.Values)
        {
            if (enemy.AllyKind != 0) (allies ??= new List<Enemy>()).Add(enemy);
        }
        if (allies == null) return;

        var vanished = new List<Enemy>();
        var blown = new List<Enemy>();
        foreach (Enemy ally in allies)
        {
            // Killed meanwhile (an earlier explosion of this same pass).
            if (!inst.Enemies.ContainsKey(ally.ShortId)) continue;
            if (ally.ExpiresAt > 0 && now >= ally.ExpiresAt)
            {
                vanished.Add(ally);
                continue;
            }
            if (now < ally.ActiveAt) continue;

            if (ally.AllyKind == KindTurret)
            {
                TurretFire(inst, ally, now, log);
            }
            else if (ally.AllyKind == KindMine && MineTouched(inst, ally, now))
            {
                ExplodeMine(inst, ally, now, log);
                blown.Add(ally);
            }
        }

        if (vanished.Count > 0) Remove(inst, vanished, ReasonVanished, ResultNone, now);
        if (blown.Count > 0) Remove(inst, blown, ReasonDestroyedByDamage, ResultDamage, now);
    }

    private static void TurretFire(Instance inst, Enemy turret, double now, Action<string, string> log)
    {
        float maxRange = 0f;
        foreach (GunState gun in turret.Guns)
        {
            if (gun.Model.Damage > 0f) maxRange = Math.Max(maxRange, GunRange(gun.Model));
        }
        if (maxRange <= 0f) return;

        Enemy? target = null;
        float best = float.MaxValue;
        foreach (Enemy enemy in inst.Enemies.Values)
        {
            if (!IsHostileTarget(enemy, now)) continue;
            float distance = Distance(turret.X, turret.Y, enemy.X, enemy.Y) - Math.Max(0f, enemy.Model.Radius);
            if (distance <= maxRange && distance < best)
            {
                best = distance;
                target = enemy;
            }
        }
        if (target == null) return;

        turret.Rot = Heading(turret.X, turret.Y, target.X, target.Y);
        NetConnection? owner = OwnerConnection(inst, turret.OwnerPeer);

        foreach (GunState gun in turret.Guns)
        {
            GameGun m = gun.Model;
            if (m.Damage <= 0f || best > GunRange(m)) continue;
            if (gun.Ammo <= 0)
            {
                if (now < gun.ReloadUntil) continue;
                gun.Ammo = Math.Max(1, m.Burst);
            }
            if (now < gun.NextShot) continue;

            SendShot(inst, turret, m, m.Angle, now);
            gun.Ammo--;
            double betweenShots = 1.0 / Math.Max(0.05f, m.FireRate > 0.05f ? m.FireRate : 2f);
            gun.NextShot = now + betweenShots;
            gun.ReloadUntil = now + Math.Max(Math.Max(0f, m.Reload), betweenShots);

            TakeDamage(owner, target.ShortId, m.Damage * Math.Max(1, m.Multi), now, log);
            if (!inst.Enemies.ContainsKey(target.ShortId)) break;
        }
    }

    private static bool MineTouched(Instance inst, Enemy mine, double now)
    {
        foreach (Enemy enemy in inst.Enemies.Values)
        {
            if (!IsHostileTarget(enemy, now)) continue;
            if (Distance(mine.X, mine.Y, enemy.X, enemy.Y) <= MineTriggerRadius + Math.Max(0f, enemy.Model.Radius)) return true;
        }
        return false;
    }

    // The damage of the blast is the gun of "<mine>_explode" in the unit catalog.
    private static void ExplodeMine(Instance inst, Enemy mine, double now, Action<string, string> log)
    {
        GameUnit? blast = FindInCatalog(mine.Model.Name + "_explode");
        float damage = blast?.Guns.Where(g => g.Damage > 0f).Select(g => g.Damage * Math.Max(1, g.Multi)).DefaultIfEmpty(0f).Max() ?? 0f;
        if (damage <= 0f) damage = 35f;

        int hit = DamageArea(OwnerConnection(inst, mine.OwnerPeer), mine.X, mine.Y, MineBlastRadius, damage, now, log);
        log("ENEMY", inst.Role + " " + Describe(mine) + " blew up at (" + Num(mine.X) + ", " + Num(mine.Y) + "): " +
            Num(damage) + " damage to " + hit + " enemy(ies)");
    }

    // ================================================================== stun

    // Effect of a bullet hit reported by the client. Returns a short text for the log, or null if nothing happened.
    public static string? ApplyHitEffect(NetConnection? connection, int target, int effect, float effectAmount,
        float effectTime, double now, Action<string, string> log)
    {
        if (connection == null || !IsStunEffect(effect)) return null;

        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return null;
            if (!inst.Enemies.TryGetValue(target, out Enemy? enemy)) return null;
            if (enemy.Team != EnemyTeam || enemy.Invulnerable || enemy.Model.Wall || enemy.Type.OpensAfterHitSeconds > 0f) return null;

            float seconds = effectTime > 0.05f ? effectTime : DefaultStunSeconds;
            seconds = Math.Clamp(seconds, 0.5f, Math.Max(0.5f, MaxStunSeconds));
            if (enemy.Boss || enemy.Model.Boss) seconds = Math.Min(seconds, BossStunSeconds);

            enemy.StunUntil = Math.Max(enemy.StunUntil, now + seconds);
            enemy.Vel = 0f;
            log("ENEMY", inst.Role + " " + Describe(enemy) + " stunned for " + Num(seconds) + "s (effect " + effect +
                ", amount " + Num(effectAmount) + ", time " + Num(effectTime) + ")");
            return "stunned " + Num(seconds) + "s";
        }
    }
}
