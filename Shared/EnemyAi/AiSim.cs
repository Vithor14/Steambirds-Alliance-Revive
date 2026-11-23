namespace SteambirdsAi;

public sealed class AiGunStats
{
    public int Index;
    public string Name = "";
    public int Mode;
    public float Angle;
    public float Arc;
    public float FireRate;
    public int Burst = 1;
    public float Reload;
    public float Damage;
    public float Speed;
    public float Duration;
    public float Range;
    public int Multi = 1;

    // Extra data of the mount for the launcher (not used by the simulation): turn speed of the mount, where it sits on the
    // art, the gun group of the client, the bullet's own turn rate and the name of the gun type (shot type) of the game.
    public float SlewRate;
    public float OffsetX;
    public float OffsetY;
    public int Group;
    public float BulletTurn;
    public string GunType = "";
}

// What the simulator needs to know about a game unit (the launcher reads it from units_config.txt).
public sealed class AiUnitStats
{
    public string Name = "";
    public string Art = "";
    public float MaxHealth = 100f;
    public float VelMin = 4f;
    public float VelMax = 9f;
    public float Turn = 120f;
    public float Radius = 1f;
    public List<AiGunStats> Guns = new List<AiGunStats>();
}

// Settings of the unit type that matter to the AI (what enemies.json / the map "types" say).
public sealed class AiSimTuning
{
    public float SpeedMult = 1f;
    public float TurnMult = 1f;
    public float FireRateMult = 1f;
    public float HealthMult = 1f;
    public float AimLead;
    public float ShotRange;
    public float FirstShotDelay = 1.5f;
    public bool FreeAim;
}

public enum AiPlayerMode
{
    // Flies to the point the user clicked and stays there.
    Goal,
    // Flies in a circle around a point.
    Circle,
    // Does not move.
    Still
}

public sealed class AiSimBullet
{
    public float X, Y, VX, VY;
    public float Life;
    public bool FromPlayer;

    // Index of the gun that fired it (AiGunStats.Index), so the launcher can color the shots by gun.
    public int Gun;
}

// A small flight arena where an AI runs against a scripted player plane. Headless: the launcher only draws it.
// The movement and the AI are the server's code (AiMotion, AiRuntime); the guns repeat Enemies.Fire (burst, reload, arcs).
public sealed class AiSim
{
    private sealed class SimGun
    {
        public AiGunStats Model = null!;
        public int Ammo;
        public double NextShot;
        public double ReloadUntil;
        public float Turn;
    }

    private readonly Random rng;
    private readonly List<SimGun> guns = new List<SimGun>();
    private readonly List<string> events = new List<string>();
    private AiRuntime? runtime;
    private double respawnAt = -1;
    private double bornAt;

    public AiSim(int seed = 12345)
    {
        rng = new Random(seed);
    }

    // ---- setup
    public AiUnitStats Unit { get; set; } = new AiUnitStats();
    public AiSimTuning Tuning { get; set; } = new AiSimTuning();
    public AiBrain? Brain { get; set; }
    public bool AutoRespawn { get; set; } = true;

    // ---- player
    public AiPlayerMode PlayerMode { get; private set; } = AiPlayerMode.Circle;
    public float PlayerSpeed { get; set; } = 10f;
    public float PlayerX, PlayerY, PlayerVX, PlayerVY, PlayerRot;
    public float GoalX, GoalY;
    public float CircleX, CircleY, CircleRadius = 22f;
    private float circleAngle;
    public bool PlayerAlive { get; set; } = true;
    public bool PlayerVisible { get; set; } = true;

    // ---- enemy
    public float X, Y, Rot, Vel;
    public float HomeX, HomeY;
    public float Health, MaxHealth;
    public bool Dead;
    public bool Exploded;
    public float HealthDrainPerSecond;
    private double lastHitAt = -1;

    // ---- world
    public double Time { get; private set; }
    public int PlayerHits { get; private set; }
    public int ShotsFired { get; private set; }
    public readonly List<AiSimBullet> Bullets = new List<AiSimBullet>();
    public IReadOnlyList<string> Events => events;
    public AiOutput? Last => runtime?.Out;
    public string StateId => runtime == null ? "" : runtime.State.Id;
    public double StateTime => runtime == null ? 0 : runtime.StateTime(Time);

    public event Action<string>? EventAdded;

    // Starts again: the enemy at the center, the player in its start place. The brain and the unit are kept.
    public void Reset()
    {
        Time = 0;
        PlayerHits = 0;
        ShotsFired = 0;
        Bullets.Clear();
        events.Clear();
        respawnAt = -1;
        PlayerAlive = true;

        circleAngle = 200f;
        CircleX = 0f;
        CircleY = 0f;
        PlayerX = CircleX + MathF.Sin(circleAngle * AiMath.Rad) * CircleRadius;
        PlayerY = CircleY + MathF.Cos(circleAngle * AiMath.Rad) * CircleRadius;
        PlayerVX = 0f;
        PlayerVY = 0f;
        PlayerRot = 0f;
        GoalX = PlayerX;
        GoalY = PlayerY;
        if (PlayerMode == AiPlayerMode.Goal)
        {
            PlayerX = 0f;
            PlayerY = -28f;
            GoalX = PlayerX;
            GoalY = PlayerY;
        }

        SpawnEnemy(0f, 0f);
    }

    private void SpawnEnemy(float x, float y)
    {
        X = x;
        Y = y;
        HomeX = x;
        HomeY = y;
        Rot = AiMath.Heading(x, y, PlayerX, PlayerY);
        MaxHealth = Math.Max(1f, Unit.MaxHealth * Math.Max(0.01f, Tuning.HealthMult));
        Health = MaxHealth;
        Dead = false;
        Exploded = false;
        lastHitAt = -1;
        bornAt = Time;
        Vel = (Unit.VelMin + Unit.VelMax) * 0.5f * Math.Max(0.01f, Tuning.SpeedMult);

        guns.Clear();
        foreach (AiGunStats g in Unit.Guns) guns.Add(new SimGun { Model = g, Ammo = Math.Max(1, g.Burst) });
        runtime = Brain == null ? null : new AiRuntime(Brain, rng);
        if (runtime != null) Log("start in state \"" + runtime.State.Id + "\"");
    }

    // Changes how the player plane moves, keeping it where it is (a circle is drawn around the place it is now).
    public void SetPlayerMode(AiPlayerMode mode)
    {
        PlayerMode = mode;
        GoalX = PlayerX;
        GoalY = PlayerY;
        if (mode == AiPlayerMode.Circle)
        {
            circleAngle = 200f;
            CircleX = PlayerX - MathF.Sin(circleAngle * AiMath.Rad) * CircleRadius;
            CircleY = PlayerY - MathF.Cos(circleAngle * AiMath.Rad) * CircleRadius;
        }
    }

    public void SetBrain(AiBrain? brain)
    {
        Brain = brain;
        Reset();
    }

    public void Damage(float fraction)
    {
        if (Dead) return;
        Health = Math.Clamp(Health - MaxHealth * fraction, 0f, MaxHealth);
        if (fraction > 0f) lastHitAt = Time;
        if (Health <= 0f) Die(false);
    }

    private void Die(bool exploded)
    {
        Dead = true;
        Exploded = exploded;
        Vel = 0f;
        respawnAt = Time + 1.5;
        Log(exploded ? "exploded" : "destroyed");
    }

    private void Log(string text)
    {
        string line = Time.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).PadLeft(5) + "s  " + text;
        events.Add(line);
        if (events.Count > 300) events.RemoveRange(0, 100);
        EventAdded?.Invoke(line);
    }

    // ------------------------------------------------------------------ step

    public void Step(float dt)
    {
        dt = Math.Clamp(dt, 0f, 0.1f);
        if (dt <= 0f) return;
        Time += dt;

        StepPlayer(dt);

        if (Dead)
        {
            if (AutoRespawn && respawnAt >= 0 && Time >= respawnAt) SpawnEnemy(0f, 0f);
        }
        else
        {
            if (HealthDrainPerSecond > 0f) Damage(HealthDrainPerSecond * dt);
            if (!Dead) StepEnemy(dt);
        }

        StepBullets(dt);
    }

    private void StepPlayer(float dt)
    {
        float oldX = PlayerX;
        float oldY = PlayerY;
        switch (PlayerMode)
        {
            case AiPlayerMode.Goal:
            {
                float dx = GoalX - PlayerX;
                float dy = GoalY - PlayerY;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > 0.4f)
                {
                    float step = Math.Min(d, PlayerSpeed * dt);
                    PlayerX += dx / d * step;
                    PlayerY += dy / d * step;
                }
                break;
            }
            case AiPlayerMode.Circle:
            {
                float radius = Math.Max(3f, CircleRadius);
                circleAngle += PlayerSpeed / radius / AiMath.Rad * dt;
                PlayerX = CircleX + MathF.Sin(circleAngle * AiMath.Rad) * radius;
                PlayerY = CircleY + MathF.Cos(circleAngle * AiMath.Rad) * radius;
                break;
            }
        }
        PlayerVX = (PlayerX - oldX) / dt;
        PlayerVY = (PlayerY - oldY) / dt;
        if (PlayerVX * PlayerVX + PlayerVY * PlayerVY > 1f) PlayerRot = AiMath.Heading(0f, 0f, PlayerVX, PlayerVY);
    }

    private void StepEnemy(float dt)
    {
        bool hasTarget = PlayerAlive && PlayerVisible;
        float speedMult = Math.Max(0.01f, Tuning.SpeedMult);
        float velMax = Unit.VelMax * speedMult;
        float velMin = Math.Min(Unit.VelMin * speedMult, velMax);
        float maxTurn = (Unit.Turn > 1f ? Unit.Turn : 120f) * Math.Max(0.01f, Tuning.TurnMult);

        AiOutput? o = null;
        if (runtime != null)
        {
            var sense = new AiSense
            {
                X = X, Y = Y, Rot = Rot, Vel = Vel, VelMin = velMin, VelMax = velMax,
                HomeX = HomeX, HomeY = HomeY, HealthFraction = Health / MaxHealth,
                HasTarget = hasTarget, TargetX = PlayerX, TargetY = PlayerY, TargetVX = PlayerVX, TargetVY = PlayerVY,
                Now = Time, Dt = dt, LastHitAt = lastHitAt
            };
            o = runtime.Step(sense);
            if (o.Entered)
            {
                Log((o.PreviousState == null ? "state \"" : "state \"" + o.PreviousState.Id + "\" -> \"") + o.State.Id + "\"" +
                    (o.State.BehaviorId.Length > 0 ? "  (behavior " + o.State.BehaviorId + ")" : ""));
                if (o.State.Message.Length > 0) Log("message: " + o.State.Message);
                if (o.State.Bark.Length > 0) Log("bark: " + o.State.Bark);
            }
            if (o.Explode)
            {
                Die(true);
                return;
            }
        }

        if (o != null)
            AiMotion.Steer(ref X, ref Y, ref Rot, ref Vel, o.TargetX, o.TargetY, o.TargetVel, velMin, velMax, maxTurn, o.Hold, dt);

        Fire(o, hasTarget);
    }

    // Same rules as Enemies.Fire of the server: burst of shots at the fire rate, then the reload.
    private void Fire(AiOutput? o, bool hasTarget)
    {
        if (o == null || !o.FireEnabled || !hasTarget) return;
        if (Time < bornAt + 0.3 + Math.Max(0f, Tuning.FirstShotDelay)) return;

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
                if (Time < gun.ReloadUntil) continue;
                gun.Ammo = Math.Max(1, m.Burst);
            }
            if (Time < gun.NextShot) continue;

            float tx = PlayerX;
            float ty = PlayerY;
            float dist = AiMath.Distance(X, Y, tx, ty);
            float range = o.Range > 0f ? o.Range : (Tuning.ShotRange > 0f ? Tuning.ShotRange : AiGunAim.Range(m.Speed, m.Duration, m.Range));
            if (dist > range) continue;

            if (lead > 0f && m.Speed > 0.5f)
            {
                float t = dist / m.Speed * lead;
                tx += PlayerVX * t;
                ty += PlayerVY * t;
            }
            float relative = AiMath.AngleDifference(Rot, AiMath.Heading(X, Y, tx, ty));
            if (!AiGunAim.TryAim(m.Mode, m.Angle, m.Arc, m.Burst, gun.Ammo, ref gun.Turn, Tuning.FreeAim, relative, Time, out float rotation))
                continue;

            int bullets = Math.Max(1, m.Multi);
            for (int i = 0; i < bullets; i++)
            {
                float spread = (i - (bullets - 1) * 0.5f) * 6f;
                float heading = (Rot + rotation + spread) * AiMath.Rad;
                Bullets.Add(new AiSimBullet
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
            gun.NextShot = Time + between;
            double reload = Math.Max(0f, m.Reload) / Math.Max(0.05f, rateMult);
            gun.ReloadUntil = Time + Math.Max(reload, between);
        }
    }

    private void StepBullets(float dt)
    {
        for (int i = Bullets.Count - 1; i >= 0; i--)
        {
            AiSimBullet b = Bullets[i];
            b.X += b.VX * dt;
            b.Y += b.VY * dt;
            b.Life -= dt;
            bool remove = b.Life <= 0f;
            if (!remove && PlayerAlive && AiMath.Distance(b.X, b.Y, PlayerX, PlayerY) < 1.2f)
            {
                PlayerHits++;
                remove = true;
            }
            if (remove) Bullets.RemoveAt(i);
        }
    }
}
