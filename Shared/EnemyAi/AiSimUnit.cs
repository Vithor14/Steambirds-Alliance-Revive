namespace SteambirdsAi;

// How a unit of the simulator other than the edited one (the leader of its group, a summoned unit) is made: its game unit, the settings of
// its type and the AI it runs. The launcher builds it (it knows the unit catalog and the AI files).
public sealed class AiSimSetup
{
    public string Label = "";
    public AiUnitStats Unit = new AiUnitStats();
    public AiSimTuning Tuning = new AiSimTuning();
    public AiBrain? Brain;
}

// Who flies a simulated unit now: its AI, a point the user sent it to, or nobody (it stays where it is, still shooting).
public enum AiSimControl
{
    Ai,
    Goal,
    Still
}

// One flying unit of the simulator: the edited unit, the leader of its group, or a unit the AI summoned. It runs its own AI and guns
// against the scripted player (the AiSim it belongs to). Headless: the launcher only draws it.
public sealed class AiSimUnit
{
    private sealed class SimGun
    {
        public AiGunStats Model = null!;
        public int Ammo;
        public double NextShot;
        public double ReloadUntil;
        public float Turn;
    }

    // "unit" (the edited one), "leader", "summon".
    public const string RoleUnit = "unit";
    public const string RoleLeader = "leader";
    public const string RoleSummon = "summon";
    public const string RolePart = "part";

    private readonly AiSim world;
    private readonly List<SimGun> guns = new List<SimGun>();
    private AiRuntime? runtime;
    private double respawnAt = -1;
    private double bornAt;
    private double lastHitAt = -1;

    internal AiSimUnit(AiSim world, string role, string label)
    {
        this.world = world;
        Role = role;
        Label = label;
    }

    public string Role { get; }
    public string Label { get; set; }
    public bool IsMain => Role == RoleUnit;

    public AiUnitStats Unit { get; set; } = new AiUnitStats();
    public AiSimTuning Tuning { get; set; } = new AiSimTuning();
    public AiBrain? Brain { get; set; }

    // The leader of the group this unit escorts (an AI with the "orbitLeader" move circles it) and the unit that summoned this one.
    public AiSimUnit? Leader { get; set; }
    public AiSimUnit? Summoner { get; set; }
    // A formation piece. Glued ones are placed by the formation every tick (their AI only shoots).
    public bool Glued;
    public string SlotId = "";
    public string SummonRole = "";
    public string SummonUnit = "";
    public FormationSlot? Slot;
    public bool ShieldSuppressed;

    // Where the unit starts, and where it comes back when it dies.
    public float SpawnX, SpawnY;

    // The user takes over the flight: toward a point, or standing still. The AI keeps running (its states, its shooting).
    public AiSimControl Control { get; set; } = AiSimControl.Ai;
    public float GoalX, GoalY;

    public float X, Y, Rot, Vel;
    public float HomeX, HomeY;
    public float Health, MaxHealth;
    public bool Dead;
    public bool Exploded;
    public float HealthDrainPerSecond;
    public double DiedAt = -1;
    public int ShotsFired;

    public bool Shielded { get; private set; }

    public void SetShield(bool on)
    {
        if (ShieldSuppressed) on = false;
        Shielded = on;
    }
    public string VisualEffect { get; private set; } = "";
    public bool Homing { get; private set; }
    public int TotalSummoned { get; private set; }

    // Starts the counters again (the arena was reset).
    internal void ResetCounters()
    {
        TotalSummoned = 0;
        ShotsFired = 0;
    }

    public AiOutput? Last => runtime?.Out;
    public string StateId => runtime == null ? "" : runtime.State.Id;
    public AiCompiledState? CurrentState => runtime?.State;
    public double StateTime => runtime == null ? 0 : runtime.StateTime(world.Time);
    public float RolledValue(int transition, int condition) => runtime == null ? 0f : runtime.Rolled(transition, condition);

    // How far a gun reaches now (the state's range, the unit type's, or the gun's own).
    public float WeaponRange(AiGunStats m)
    {
        AiOutput? o = Last;
        return o != null && o.Range > 0f ? o.Range : (Tuning.ShotRange > 0f ? Tuning.ShotRange : AiGunAim.Range(m.Speed, m.Duration, m.Range));
    }

    // The guns the current state lets fire (all of the unit's guns with damage when the state does not pick some).
    public IEnumerable<AiGunStats> ActiveGuns()
    {
        AiOutput? o = Last;
        if (o == null || !o.FireEnabled) yield break;
        foreach (AiGunStats m in Unit.Guns)
        {
            if (m.Damage <= 0f) continue;
            if (o.Guns.Length > 0 && Array.IndexOf(o.Guns, m.Index) < 0) continue;
            yield return m;
        }
    }

    // ------------------------------------------------------------------ life

    // Puts the unit (alive, full health, a new AI run) at a point.
    public void Spawn(float x, float y)
    {
        X = x;
        Y = y;
        SpawnX = x;
        SpawnY = y;
        HomeX = x;
        HomeY = y;
        Rot = AiMath.Heading(x, y, world.PlayerX, world.PlayerY);
        MaxHealth = Math.Max(1f, Unit.MaxHealth * Math.Max(0.01f, Tuning.HealthMult));
        Health = MaxHealth;
        Dead = false;
        Exploded = false;
        DiedAt = -1;
        respawnAt = -1;
        lastHitAt = -1;
        bornAt = world.Time;
        Shielded = false;
        VisualEffect = "";
        Homing = false;
        Vel = (Unit.VelMin + Unit.VelMax) * 0.5f * Math.Max(0.01f, Tuning.SpeedMult);

        guns.Clear();
        foreach (AiGunStats g in Unit.Guns) guns.Add(new SimGun { Model = g, Ammo = Math.Max(1, g.Burst) });
        runtime = Brain == null ? null : new AiRuntime(Brain, world.Rng);
        if (runtime != null) Log("start in state \"" + runtime.State.Id + "\"");
    }

    // The AI was edited: the new version takes over and the unit goes on from where it is.
    public void ReplaceBrain(AiBrain brain)
    {
        if (Brain == null || runtime == null)
        {
            Brain = brain;
            Spawn(SpawnX, SpawnY);
            return;
        }
        Brain = brain;
        runtime.Rebind(brain);
    }

    // A fraction of the maximum health (negative = heal). A shield ignores damage, as it does on the server.
    public void Damage(float fraction)
    {
        if (Dead) return;
        if (Shielded && fraction > 0f)
        {
            if (fraction >= 0.02f) Log("hit ignored: shield");
            return;
        }
        Health = Math.Clamp(Health - MaxHealth * fraction, 0f, MaxHealth);
        if (fraction > 0f) lastHitAt = world.Time;
        if (Health <= 0f) Die(false);
    }

    internal void Die(bool exploded)
    {
        Dead = true;
        Exploded = exploded;
        Vel = 0f;
        DiedAt = world.Time;
        respawnAt = world.Time + 1.5;
        Log(exploded ? "exploded" : "destroyed");
    }

    private void Log(string text) => world.Log(IsMain ? text : Label + ": " + text);

    // ------------------------------------------------------------------ step

    internal void Step(float dt)
    {
        if (Dead)
        {
            if (world.AutoRespawn && !IsSummon && respawnAt >= 0 && world.Time >= respawnAt)
            {
                Spawn(SpawnX, SpawnY);
                if (IsMain)
                {
                    world.ClearSummons();
                    world.RespawnFormation();
                }
            }
            return;
        }
        if (HealthDrainPerSecond > 0f) Damage(HealthDrainPerSecond * dt);
        if (!Dead) StepBody(dt);
    }

    private bool IsSummon => Role == RoleSummon;

    private void StepBody(float dt)
    {
        bool hasTarget = world.PlayerAlive && world.PlayerVisible;
        float speedMult = Math.Max(0.01f, Tuning.SpeedMult);
        float velMax = Unit.VelMax * speedMult;
        float velMin = Math.Min(Unit.VelMin * speedMult, velMax);
        float maxTurn = (Unit.Turn > 1f ? Unit.Turn : 120f) * Math.Max(0.01f, Tuning.TurnMult);

        AiOutput? o = null;
        if (runtime != null)
        {
            bool hasLeader = Leader != null && !Leader.Dead;
            var sense = new AiSense
            {
                X = X, Y = Y, Rot = Rot, Vel = Vel, VelMin = velMin, VelMax = velMax,
                HomeX = HomeX, HomeY = HomeY, HealthFraction = Health / MaxHealth,
                HasTarget = hasTarget, TargetX = world.PlayerX, TargetY = world.PlayerY, TargetVX = world.PlayerVX, TargetVY = world.PlayerVY,
                HasLeader = hasLeader, LeaderX = hasLeader ? Leader!.X : 0f, LeaderY = hasLeader ? Leader!.Y : 0f,
                Now = world.Time, Dt = dt, LastHitAt = lastHitAt, SummonsAlive = world.CountSummons(this),
                CountMatching = (unit, role) => world.CountMatching(this, unit, role),
                HasSummoner = Summoner != null && !Summoner.Dead,
                SummonerX = Summoner != null ? Summoner.X : 0f, SummonerY = Summoner != null ? Summoner.Y : 0f
            };
            o = runtime.Step(sense);
            if (o.Entered)
            {
                Log((o.PreviousState == null ? "state \"" : "state \"" + o.PreviousState.Id + "\" -> \"") + o.State.Id + "\"" +
                    (o.State.BehaviorId.Length > 0 ? "  (behavior " + o.State.BehaviorId + ")" : ""));
                foreach (AiCompiledState entered in o.EnteredStates)
                {
                    if (entered.Message.Length > 0) Log("message: " + entered.Message);
                    if (entered.Bark.Length > 0) Log("bark: " + entered.Bark);
                    if (entered.Effect.Length > 0) VisualEffect = entered.Effect;
                    if (entered.Homing.HasValue) Homing = entered.Homing.Value;
                }
            }
            foreach (AiSummonOrder order in o.Summons)
            {
                Log("summons " + order.Count + " x " + order.Type);
                TotalSummoned += order.Count;
                world.Summon(this, order);
            }
            if (ShieldSuppressed) o.ShieldOn = false;
            if (o.ShieldOn != Shielded)
            {
                Shielded = o.ShieldOn;
                Log(Shielded ? "shield up" + (o.ShieldEffect.Length > 0 ? " (" + o.ShieldEffect + ")" : "") : "shield down");
            }
            if (o.Explode)
            {
                Die(true);
                return;
            }
            if (o.UseBuiltin) ApproximateBuiltin(o, velMax, hasTarget);

            // The user flies the unit (the leader): to a point, or not at all. The AI still decides the state and the shooting.
            if (Control == AiSimControl.Goal)
            {
                o.Hold = false;
                o.TargetX = GoalX;
                o.TargetY = GoalY;
                o.TargetVel = AiMath.Distance(X, Y, GoalX, GoalY) < 1.5f ? 0f : velMax * 0.8f;
            }
            else if (Control == AiSimControl.Still)
            {
                o.Hold = true;
                o.TargetVel = 0f;
            }
        }

        if (o != null && !Glued)
            AiMotion.Steer(ref X, ref Y, ref Rot, ref Vel, o.TargetX, o.TargetY, o.TargetVel, velMin, velMax, maxTurn * Math.Max(0.05f, o.TurnMult), o.Hold, dt);

        Fire(o, hasTarget);
    }

    // The server's built-in flights are not in the shared core: the simulator only draws something close, so a state that
    // uses one can still be tried (circle and chase orbit, charge goes straight at the player, turrets stand still).
    private void ApproximateBuiltin(AiOutput o, float velMax, bool hasTarget)
    {
        string flight = o.Builtin.Length > 0 ? o.Builtin : Tuning.Builtin;
        o.Hold = false;
        switch (flight)
        {
            case "turret":
            case "fixed":
            case "idle":
            case "mine":
                o.Hold = true;
                o.TargetVel = 0f;
                break;
            case "charge":
            case "kamikaze":
                o.TargetVel = velMax * o.SpeedMult;
                break;
            case "circle":
            case "chase":
            default:
            {
                if (hasTarget)
                {
                    float heading = AiMath.Heading(world.PlayerX, world.PlayerY, X, Y);
                    float a = (heading + (flight == "circle" ? 75f : 30f)) * AiMath.Rad;
                    o.TargetX = world.PlayerX + MathF.Sin(a) * 12f;
                    o.TargetY = world.PlayerY + MathF.Cos(a) * 12f;
                }
                o.TargetVel = velMax * 0.75f * o.SpeedMult;
                break;
            }
        }
    }

    // Same rules as Enemies.Fire of the server: burst of shots at the fire rate, then the reload.
    private void Fire(AiOutput? o, bool hasTarget)
    {
        if (o == null || !o.FireEnabled || !hasTarget) return;
        double now = world.Time;
        if (now < bornAt + 0.3 + Math.Max(0f, Tuning.FirstShotDelay)) return;

        float rateMult = Tuning.FireRateMult > 0f ? Tuning.FireRateMult : 1f;
        rateMult *= o.RateMult > 0f ? o.RateMult : 0.0001f;
        float lead = o.AimLead >= 0f ? o.AimLead : Tuning.AimLead;

        foreach (SimGun gun in guns)
        {
            AiGunStats m = gun.Model;
            if (o.Guns.Length > 0 && Array.IndexOf(o.Guns, m.Index) < 0) continue;
            if (m.Damage <= 0f) continue;

            if (gun.Ammo <= 0)
            {
                if (now < gun.ReloadUntil) continue;
                gun.Ammo = Math.Max(1, m.Burst);
            }
            if (now < gun.NextShot) continue;

            float tx = world.PlayerX;
            float ty = world.PlayerY;
            float dist = AiMath.Distance(X, Y, tx, ty);
            float range = o.Range > 0f ? o.Range : (Tuning.ShotRange > 0f ? Tuning.ShotRange : AiGunAim.Range(m.Speed, m.Duration, m.Range));
            if (dist > range) continue;

            if (lead > 0f && m.Speed > 0.5f)
            {
                float t = dist / m.Speed * lead;
                tx += world.PlayerVX * t;
                ty += world.PlayerVY * t;
            }
            float relative = AiMath.AngleDifference(Rot, AiMath.Heading(X, Y, tx, ty));
            if (!AiGunAim.TryAim(m.Mode, m.Angle, m.Arc, m.Burst, gun.Ammo, ref gun.Turn, Tuning.FreeAim, relative, now, out float rotation,
                    AiGunAim.IsAnyDirection(m.Mode, m.Arc, m.Name)))
                continue;

            int bullets = Math.Max(1, m.Multi);
            for (int i = 0; i < bullets; i++)
            {
                float spread = (i - (bullets - 1) * 0.5f) * 6f;
                float heading = (Rot + rotation + spread) * AiMath.Rad;
                world.Bullets.Add(new AiSimBullet
                {
                    X = X, Y = Y,
                    VX = MathF.Sin(heading) * Math.Max(1f, m.Speed),
                    VY = MathF.Cos(heading) * Math.Max(1f, m.Speed),
                    Life = m.Duration > 0.05f ? m.Duration : 1.5f,
                    Gun = m.Index
                });
            }
            ShotsFired++;
            gun.Ammo--;

            float fireRate = (m.FireRate > 0.05f ? m.FireRate : 2f) * rateMult;
            double between = 1.0 / Math.Max(0.05f, fireRate);
            gun.NextShot = now + between;
            double reload = Math.Max(0f, m.Reload) / Math.Max(0.05f, rateMult);
            gun.ReloadUntil = now + Math.Max(reload, between);
        }
    }
}
