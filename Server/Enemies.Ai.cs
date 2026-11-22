using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lidgren.Network;
using SteambirdsAi;

// Enemy AI: flight, aim and guns of each tick (called 20 times per second).
public static partial class Enemies
{
    // ================================================================== tick: IA

    private static void UpdateInstance(Instance inst, float dt, double now, Action<string, string> log)
    {
        ReadPlayer(inst, now);

        // The player's plane fell: the current wave disappears and comes back when they spawn again.
        if (inst.PlayerWasAlive && !inst.PlayerAlive)
        {
            log("ENEMY", inst.Role + " player's plane fell with " + inst.Enemies.Count + " enemy(ies) alive");
            EncounterConfig? encounter = inst.Encounter;
            if (encounter != null && encounter.RestartWaveOnDeath && !inst.Done && inst.CurrentWave >= 0 && inst.WaveScheduled < 0)
            {
                Remove(inst, inst.Enemies.Values.Where(i => i.FromWave).ToList(), ReasonVanished, ResultNone, now);
                inst.WaveScheduled = inst.CurrentWave;
                inst.WaveScheduledFor = now + 4.0;
                Warn(inst, "You fell! The wave " + (inst.CurrentWave + 1) + " restarts when you come back.");
            }
        }
        inst.PlayerWasAlive = inst.PlayerAlive;

        Steer(inst, now, log);

        if (inst.Enemies.Count == 0) return;

        // Shields that ended: the visual effect goes back to the normal one of the unit.
        foreach (Enemy enemy in inst.Enemies.Values)
        {
            if (enemy.ShieldUntil <= 0 || now < enemy.ShieldUntil) continue;
            enemy.ShieldUntil = 0;
            if (enemy.EffectId != enemy.BaseEffect)
            {
                enemy.EffectId = enemy.BaseEffect;
                enemy.EffectChanged = true;
            }
        }

        // Chests that were hit: they open (explode) when time passes.
        List<Enemy>? open = null;
        foreach (Enemy enemy in inst.Enemies.Values)
        {
            if (enemy.Type.OpensAfterHitSeconds > 0f && enemy.HitAt >= 0 && now >= enemy.HitAt + enemy.Type.OpensAfterHitSeconds)
                (open ??= new List<Enemy>()).Add(enemy);
        }
        if (open != null)
        {
            Remove(inst, open, ReasonDestroyedByDamage, ResultDamage, now);
            foreach (Enemy enemy in open)
            {
                log("ENEMY", inst.Role + " " + Describe(enemy) + " opened at (" + Num(enemy.X) + ", " + Num(enemy.Y) + ")");
                // Chest opened: its loot (the tutorial crate uses loot_tables\tutorial_walker_chest.json).
                foreach (NetConnection member in inst.Members.ToList())
                    LootDrops.EnemyKilled(inst.Server, member, inst.Role.Trim('[', ']'), enemy.Model.Name, enemy.Model.Tier,
                        false, enemy.X, enemy.Y, now, log);
            }
            if (inst.Enemies.Count == 0) return;
        }

        List<Enemy>? exploded = null;
        foreach (Enemy enemy in inst.Enemies.Values)
        {
            // Stunned (EMP): stands still and does not shoot.
            if (now < enemy.StunUntil)
            {
                enemy.Vel = 0f;
                continue;
            }

            FocusNearest(inst, enemy);
            Think(inst, enemy, dt, now, log);
            Fire(inst, enemy, now);

            // Bombs ("kamikaze"): touching the player blows them up (no XP, no loot: the bomb used itself).
            if (enemy.Ai == null && enemy.Behavior == "kamikaze" && !enemy.Asleep && enemy.Aggro && inst.HasPlayer && inst.PlayerAlive &&
                Distance(enemy.X, enemy.Y, inst.PlayerX, inst.PlayerY) <= Math.Max(0.5f, enemy.Type.ContactRadius))
                (exploded ??= new List<Enemy>()).Add(enemy);
            // The same, for an AI file: a state with "contact.explodeRadius" or "selfDestruct" asks for it.
            else if (enemy.Ai != null && !enemy.Asleep && now >= enemy.ActiveAt && enemy.Ai.Out.Explode)
                (exploded ??= new List<Enemy>()).Add(enemy);
        }
        if (exploded != null)
        {
            Remove(inst, exploded, ReasonDestroyedByDamage, ResultDamage, now);
            foreach (Enemy enemy in exploded)
                log("ENEMY", inst.Role + " " + Describe(enemy) + " exploded on the player at (" + Num(enemy.X) + ", " + Num(enemy.Y) + ")");
        }

        // Turrets and mines of the players.
        UpdateAllies(inst, now, log);

        if (now - inst.LastSend >= SendInterval)
        {
            inst.LastSend = now;
            SendUpdates(inst, now);
        }
    }

    // Position and velocity of the planes of the players of the instance (the velocity is estimated from samples, for
    // the lead aim). Afterwards inst.Player* describes the first live player; with several players, every enemy looks at
    // the nearest one (FocusNearest).
    private static void ReadPlayer(Instance inst, double now)
    {
        PlayerTrack? main = null;
        PlayerTrack? firstWithPlane = null;
        foreach (NetConnection member in inst.Members.ToList())
        {
            long peer = member.RemoteUniqueIdentifier;
            if (!inst.Tracks.TryGetValue(peer, out PlayerTrack? t))
            {
                t = new PlayerTrack { Peer = peer };
                inst.Tracks[peer] = t;
            }

            if (!Game.GetPlane(peer, out float x, out float y, out bool alive, out int shortId))
            {
                t.Has = false;
                t.Alive = false;
                continue;
            }

            t.Has = true;
            t.Alive = alive;
            t.ShortId = shortId;

            if (!t.HasSample)
            {
                t.HasSample = true;
                t.SampleX = x;
                t.SampleY = y;
                t.SampleAt = now;
                t.VX = 0f;
                t.VY = 0f;
            }
            else if (now - t.SampleAt >= 0.1)
            {
                float interval = (float)(now - t.SampleAt);
                float vx = (x - t.SampleX) / interval;
                float vy = (y - t.SampleY) / interval;
                if (vx * vx + vy * vy > 60f * 60f)
                {
                    // A big jump (teleport or respawn): it is not velocity.
                    vx = 0f;
                    vy = 0f;
                }
                t.VX += (vx - t.VX) * 0.5f;
                t.VY += (vy - t.VY) * 0.5f;
                t.SampleX = x;
                t.SampleY = y;
                t.SampleAt = now;
            }

            t.X = x;
            t.Y = y;
            firstWithPlane ??= t;
            if (alive) main ??= t;
        }

        PlayerTrack? pick = main ?? firstWithPlane;
        if (pick == null)
        {
            inst.HasPlayer = false;
            inst.PlayerAlive = false;
            return;
        }
        Focus(inst, pick);
        inst.PlayerAlive = main != null;
    }

    private static void Focus(Instance inst, PlayerTrack t)
    {
        inst.HasPlayer = true;
        inst.PlayerAlive = t.Alive;
        inst.PlayerShortId = t.ShortId;
        inst.FocusPeer = t.Peer;
        inst.PlayerX = t.X;
        inst.PlayerY = t.Y;
        inst.PlayerVX = t.VX;
        inst.PlayerVY = t.VY;
    }

    // With several players: the enemy looks at the nearest live plane (the one it chases, aims at and can blow up on).
    private static void FocusNearest(Instance inst, Enemy enemy)
    {
        if (inst.Members.Count < 2) return;
        PlayerTrack? best = null;
        float bestDistance = float.MaxValue;
        foreach (PlayerTrack t in inst.Tracks.Values)
        {
            if (!t.Has || !t.Alive) continue;
            float d = Distance(enemy.X, enemy.Y, t.X, t.Y);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = t;
            }
        }
        if (best != null) Focus(inst, best);
    }

    // The plane of this connection spawned again: the estimated velocity restarts from zero (lead aim).
    private static void ResetSample(Instance inst, NetConnection connection)
    {
        if (inst.Tracks.TryGetValue(connection.RemoteUniqueIdentifier, out PlayerTrack? t)) t.HasSample = false;
    }

    // Plane flight: picks a destination point according to the behavior, turns at most MaxTurn degrees
    // per second and accelerates between the minimum and maximum speed. Rotation 0 = +Y, positive = clockwise.
    private static void Think(Instance inst, Enemy enemy, float dt, double now, Action<string, string> log)
    {
        if (now < enemy.ActiveAt) return;

        // Walls, chests and wrecks: they do not move or turn. (A unit with an AI file decides by itself.)
        if (enemy.Behavior == "idle" && enemy.Ai == null)
        {
            enemy.Vel = 0f;
            return;
        }

        bool validTarget = inst.HasPlayer && inst.PlayerAlive;
        float dx = inst.PlayerX - enemy.X;
        float dy = inst.PlayerY - enemy.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        // Waiting in place (pre-placed by the script): does not move, turn or aim.
        if (enemy.Asleep)
        {
            if (!(validTarget && enemy.WakeRadius > 0f && dist <= enemy.WakeRadius))
            {
                enemy.Vel = 0f;
                return;
            }
            WakeGroupInternal(inst, enemy, now);
        }

        // A unit with an AI file: the AI decides when it fights (aggroRadius does not apply).
        if (enemy.Ai != null) enemy.Aggro = validTarget;
        else if (!validTarget) enemy.Aggro = false;
        else if (!enemy.Aggro && (enemy.Type.AggroRadius <= 0f || dist <= enemy.Type.AggroRadius)) enemy.Aggro = true;

        // Fixed cannon: does not move or turn; it fires straight where it points (see Fire).
        if (enemy.Behavior == "fixed" && enemy.Ai == null)
        {
            enemy.Vel = 0f;
            return;
        }

        float velMax = enemy.VelMax * enemy.PhaseSpeedMult;
        float velMin = Math.Min(enemy.VelMin * enemy.PhaseSpeedMult, velMax);
        string mode = enemy.Behavior == "escort" ? "escort" : (enemy.Aggro ? enemy.Behavior : "patrol");
        if (enemy.Ai != null) mode = "brain";
        else if (velMax <= 0.05f) mode = "turret";
        float preferred = Math.Max(2f, enemy.Type.PreferredDistance);

        float targetX;
        float targetY;
        float targetVel;
        bool brainHold = false;
        switch (mode)
        {
            case "brain":
                BrainThink(inst, enemy, now, dt, velMin, velMax, validTarget, log, out targetX, out targetY, out targetVel, out brainHold);
                break;

            case "turret":
                targetX = validTarget ? inst.PlayerX : enemy.X + MathF.Sin(enemy.Rot * Rad);
                targetY = validTarget ? inst.PlayerY : enemy.Y + MathF.Cos(enemy.Rot * Rad);
                targetVel = 0f;
                break;

            case "circle":
                OrbitPoint(inst, enemy, preferred, 75f, out targetX, out targetY);
                targetVel = Lerp(velMin, velMax, 0.75f);
                break;

            case "kamikaze":
                // Bomb: goes straight for the player (a little ahead of where the plane is going) at 60% of its top
                // speed, so it can be outrun and shot down; it explodes on contact (see Update).
                targetX = validTarget ? inst.PlayerX + inst.PlayerVX * 0.25f : enemy.X + MathF.Sin(enemy.Rot * Rad);
                targetY = validTarget ? inst.PlayerY + inst.PlayerVY * 0.25f : enemy.Y + MathF.Cos(enemy.Rot * Rad);
                targetVel = velMax * 0.6f;
                break;

            case "charge":
                if (enemy.Charging)
                {
                    // Dives at the player...
                    targetX = inst.PlayerX + inst.PlayerVX * 0.4f;
                    targetY = inst.PlayerY + inst.PlayerVY * 0.4f;
                    targetVel = velMax;
                    if (dist < Math.Max(3f, preferred * 0.35f))
                    {
                        enemy.Charging = false;
                        enemy.ManeuverChangeAt = now + 1.4;
                    }
                }
                else
                {
                    // ...passes straight through and turns around.
                    targetX = enemy.X + MathF.Sin(enemy.Rot * Rad) * 20f;
                    targetY = enemy.Y + MathF.Cos(enemy.Rot * Rad) * 20f;
                    targetVel = velMax;
                    if (now >= enemy.ManeuverChangeAt) enemy.Charging = true;
                }
                break;

            case "patrol":
                if (now >= enemy.NextPatrol || Distance(enemy.X, enemy.Y, enemy.PatrolX, enemy.PatrolY) < 3f)
                {
                    float a = (float)(rng.NextDouble() * Math.PI * 2.0);
                    float r = 4f + (float)rng.NextDouble() * 8f;
                    enemy.PatrolX = enemy.HomeX + MathF.Sin(a) * r;
                    enemy.PatrolY = enemy.HomeY + MathF.Cos(a) * r;
                    enemy.NextPatrol = now + 4.0 + rng.NextDouble() * 3.0;
                }
                targetX = enemy.PatrolX;
                targetY = enemy.PatrolY;
                targetVel = Math.Max(velMin, velMax * 0.45f);
                break;

            case "escort":
                if (enemy.HasDest)
                {
                    // Goes to the destination and circles there (shows the way to the player).
                    float until = Distance(enemy.X, enemy.Y, enemy.DestX, enemy.DestY);
                    if (until > 3f)
                    {
                        targetX = enemy.DestX;
                        targetY = enemy.DestY;
                    }
                    else
                    {
                        float around = (float)(now * 90.0) * Rad;
                        targetX = enemy.DestX + MathF.Sin(around) * 3f;
                        targetY = enemy.DestY + MathF.Cos(around) * 3f;
                    }
                    targetVel = velMax;
                }
                else if (validTarget)
                {
                    // Follows the player closely.
                    OrbitPoint(inst, enemy, preferred, 40f, out targetX, out targetY);
                    targetVel = dist > preferred * 2f ? velMax : Lerp(velMin, velMax, 0.5f);
                }
                else
                {
                    targetX = enemy.X + MathF.Sin(enemy.Rot * Rad) * 5f;
                    targetY = enemy.Y + MathF.Cos(enemy.Rot * Rad) * 5f;
                    targetVel = velMin;
                }
                break;

            default: // chase: gets close and circles at the preferred distance
                OrbitPoint(inst, enemy, preferred, 30f, out targetX, out targetY);
                targetVel = dist > preferred * 1.6f ? velMax : Lerp(velMin, velMax, 0.55f);
                break;
        }

        // Separation: enemies do not stack.
        foreach (Enemy outro in inst.Enemies.Values)
        {
            if (outro == enemy) continue;
            float ox = enemy.X - outro.X;
            float oy = enemy.Y - outro.Y;
            float d2 = ox * ox + oy * oy;
            if (d2 >= 16f || d2 < 0.0001f) continue;
            float d = MathF.Sqrt(d2);
            float push = (4f - d) * 3f;
            targetX += ox / d * push;
            targetY += oy / d * push;
        }

        // Area: outside it, goes back to the center; the destination is never outside.
        // With an area (script room) the room's rotated rectangle applies; without one, the arena square.
        ArenaConfig arena = config.Arena;
        float limit = Math.Max(5f, arena.Limit);
        if (enemy.FreeRoam)
        {
            // No limit (an ally that passes through the rooms).
        }
        else if (enemy.Area.HasValue)
        {
            Ground.Rect room = enemy.Area.Value;
            if (!Inside(room, enemy.X, enemy.Y, HardMargin))
                Ground.ToWorld(room.CenterA, room.CenterB, out targetX, out targetY);
            ConfineTo(room, false, ref targetX, ref targetY, DestMargin);
        }
        else
        {
            if (Math.Abs(enemy.X - arena.CenterX) > limit || Math.Abs(enemy.Y - arena.CenterY) > limit)
            {
                targetX = arena.CenterX;
                targetY = arena.CenterY;
            }
            targetX = Math.Clamp(targetX, arena.CenterX - limit, arena.CenterX + limit);
            targetY = Math.Clamp(targetY, arena.CenterY - limit, arena.CenterY + limit);
        }

        // Turn toward the destination and speed up or slow down (Shared\EnemyAi\AiMath.cs, the same code as the launcher's simulator).
        AiMotion.Steer(ref enemy.X, ref enemy.Y, ref enemy.Rot, ref enemy.Vel, targetX, targetY, targetVel,
            velMin, velMax, enemy.MaxTurn, mode == "turret" || brainHold, dt);

        if (enemy.FreeRoam) return;
        if (enemy.Area.HasValue)
        {
            // Never crosses the room's wall.
            ConfineTo(enemy.Area, false, ref enemy.X, ref enemy.Y, HardMargin);
            return;
        }
        float hard = limit + 4f;
        enemy.X = Math.Clamp(enemy.X, arena.CenterX - hard, arena.CenterX + hard);
        enemy.Y = Math.Clamp(enemy.Y, arena.CenterY - hard, arena.CenterY + hard);
    }

    // Point on the circle of radius "radius" around the player, ahead by "deviation" degrees in the orbit direction.
    // Far from the player the point is almost on the line to them (the enemy approaches); close, it goes around.
    private static void OrbitPoint(Instance inst, Enemy enemy, float radius, float deviation, out float x, out float y)
    {
        float heading = Heading(inst.PlayerX, inst.PlayerY, enemy.X, enemy.Y);
        float a = (heading + enemy.OrbitDirection * deviation) * Rad;
        x = inst.PlayerX + MathF.Sin(a) * radius;
        y = inst.PlayerY + MathF.Cos(a) * radius;
    }

    // Guns: a charge (burst) fired at the fire rate (rof); after the charge, it waits for the reload (cooldown).
    // Same logic as the client's GunMountInfo.FireBullet/MaybeReload.
    private static void Fire(Instance inst, Enemy enemy, double now)
    {
        // Allies and idle objects do not shoot the player.
        if (enemy.Team != EnemyTeam || enemy.Behavior == "idle") return;
        if (now < enemy.ActiveAt + Math.Max(0f, enemy.Type.FirstShotDelay)) return;
        bool canAim = enemy.Aggro && inst.HasPlayer && inst.PlayerAlive;
        float fireRateMult = Positive(enemy.Type.FireRateMultiplier) * enemy.PhaseFireRateMult;

        // An AI file decides, state by state, whether the unit shoots, with which guns, how fast, how far and how it aims.
        AiOutput? brain = enemy.Ai?.Out;
        float aimLead = enemy.Type.AimLead;
        float rangeOverride = 0f;
        if (brain != null)
        {
            if (!brain.FireEnabled) return;
            fireRateMult *= brain.RateMult;
            rangeOverride = brain.Range;
            if (brain.AimLead >= 0f) aimLead = brain.AimLead;
        }
        fireRateMult *= DungeonFireScale(inst, enemy, fireRateMult);

        foreach (GunState gun in enemy.Guns)
        {
            GameGun m = gun.Model;
            if (!GunAllowed(enemy, m.Index)) continue;
            if (m.Damage <= 0f) continue;

            if (gun.Ammo <= 0)
            {
                if (now < gun.ReloadUntil) continue;
                gun.Ammo = Math.Max(1, m.Burst);
            }
            if (!canAim || now < gun.NextShot) continue;

            float tx = inst.PlayerX;
            float ty = inst.PlayerY;
            float dist = Distance(enemy.X, enemy.Y, tx, ty);
            float range = rangeOverride > 0f ? rangeOverride : enemy.Type.ShotRange > 0f ? enemy.Type.ShotRange : GunRange(m);
            if (dist > range) continue;

            float gunRotation;
            if (enemy.Behavior == "fixed" && enemy.Ai == null)
            {
                // Fixed cannon: the burst goes straight out of the gun's mouth, without looking for the player.
                gunRotation = m.Angle;
            }
            else
            {
                if (aimLead > 0f && m.Speed > 0.5f)
                {
                    float t = dist / m.Speed * aimLead;
                    tx += inst.PlayerVX * t;
                    ty += inst.PlayerVY * t;
                }

                float relative = AngleDifference(enemy.Rot, Heading(enemy.X, enemy.Y, tx, ty));
                if (!AimGun(gun, enemy.Type.FreeAim, relative, now, out gunRotation)) continue;
            }

            SendShot(inst, enemy, m, gunRotation, now);
            gun.Ammo--;

            float fireRate = (m.FireRate > 0.05f ? m.FireRate : 2f) * fireRateMult;
            double betweenShots = 1.0 / Math.Max(0.05f, fireRate);
            gun.NextShot = now + betweenShots;
            double reload = Math.Max(0f, m.Reload) / Math.Max(0.05f, fireRateMult);
            gun.ReloadUntil = now + Math.Max(reload, betweenShots);
        }
    }

    // Dungeons only: the original units picked ONE gun pattern at a time with a state machine that is not in any client
    // build, while this server fires every gun of the template together (the Slicer has four guns and put out ~70 bullets
    // per second). Host settings (dungeons.json): "fireRateScale" multiplies every fire rate and "maxBulletsPerSecond" limits
    // what one unit puts out (the fire rate of all its active guns goes down together until it fits; 0 = no limit).
    private static float DungeonFireScale(Instance inst, Enemy enemy, float fireRateMult)
    {
        if (!string.Equals(inst.Role.Trim('[', ']'), DungeonServer.Role, StringComparison.Ordinal)) return 1f;

        float scale = Positive(DungeonCatalog.FireRateScale);
        float cap = DungeonCatalog.MaxBulletsPerSecond;
        if (cap <= 0f) return scale;

        float perSecond = 0f;
        foreach (GunState gun in enemy.Guns)
        {
            GameGun m = gun.Model;
            if (m.Damage <= 0f) continue;
            if (!GunAllowed(enemy, m.Index)) continue;
            float rate = (m.FireRate > 0.05f ? m.FireRate : 2f) * fireRateMult * scale;
            float bullets = rate * Math.Max(1, m.Multi);
            if (m.Burst > 1 && m.Burst < 1000 && m.Reload > 0f)
            {
                // A charge of "burst" shots and then the reload: the average over the whole cycle.
                float charge = m.Burst / Math.Max(0.05f, rate);
                float reload = m.Reload / Math.Max(0.05f, fireRateMult * scale);
                bullets *= charge / (charge + reload);
            }
            perSecond += bullets;
        }
        return perSecond > cap ? scale * cap / perSecond : scale;
    }

    // Angle of the gun (relative to the unit's front) for this shot. False = the player is outside the arc.
    // The patterns live in Shared\EnemyAi\AiMath.cs (AiGunAim), shared with the launcher's simulator.
    private static bool AimGun(GunState gun, bool freeAim, float relative, double now, out float rotation)
    {
        GameGun m = gun.Model;
        return AiGunAim.TryAim(m.Mode, m.Angle, m.Arc, m.Burst, gun.Ammo, ref gun.Turn, freeAim, relative, now, out rotation);
    }

    private static float GunRange(GameGun m)
    {
        return AiGunAim.Range(m.Speed, m.Duration, m.Range);
    }

}
