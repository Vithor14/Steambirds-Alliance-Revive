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
    // The "behavior" of the unit type (what a builtin move with no name runs on the server).
    public string Builtin = "chase";
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
// The arena holds the edited unit ("Main"), optionally the leader of its group, and the units the AI summons; each one is an AiSimUnit.
public sealed class AiSim
{
    // The most summoned units alive at once (a summon loop would otherwise fill the arena).
    public const int MaxSummons = 40;
    // Seconds a destroyed summon stays on screen (as a wreck) before it is gone.
    private const double WreckSeconds = 1.0;

    private readonly List<string> events = new List<string>();
    private readonly List<AiSimUnit> units = new List<AiSimUnit>();
    private bool leaderEnabled = true;
    internal readonly Random Rng;

    public AiSim(int seed = 12345)
    {
        Rng = new Random(seed);
        Main = new AiSimUnit(this, AiSimUnit.RoleUnit, "");
        units.Add(Main);
    }

    // ---- units
    // The edited unit; the forwarding members below are its.
    public AiSimUnit Main { get; }
    public IReadOnlyList<AiSimUnit> Units => units;
    public AiSimUnit? Leader => units.FirstOrDefault(u => u.Role == AiSimUnit.RoleLeader);

    // How a summoned unit is made (by the name the AI gives): null = the type is not known, a dummy comes instead.
    public Func<string, AiSimSetup?>? SummonResolver { get; set; }

    // Pieces glued to the edited unit. Set before SetBrain / Reset. Null = the unit is alone.
    public FormationFile? Formation { get; set; }

    // Every formation file, so a summon place "point:formation/id" can find a point that is not on Formation.
    public FormationLibrary? KnownFormations { get; set; }
    private readonly List<(float X, float Y)> formationTrail = new List<(float X, float Y)>();
    private readonly HashSet<string> formationSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> formationFired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // The leader of the edited unit's group (null = it has none). Applied by the next Reset.
    public AiSimSetup? LeaderSetup { get; set; }

    // ---- setup
    public AiUnitStats Unit { get => Main.Unit; set => Main.Unit = value; }
    public AiSimTuning Tuning { get => Main.Tuning; set => Main.Tuning = value; }
    public AiBrain? Brain { get => Main.Brain; set => Main.Brain = value; }
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

    // ---- the edited unit
    public float X => Main.X;
    public float Y => Main.Y;
    public float Rot => Main.Rot;
    public float Vel => Main.Vel;
    public float HomeX => Main.HomeX;
    public float HomeY => Main.HomeY;
    public float Health => Main.Health;
    public float MaxHealth => Main.MaxHealth;
    public bool Dead => Main.Dead;
    public bool Exploded => Main.Exploded;
    public float HealthDrainPerSecond { get => Main.HealthDrainPerSecond; set => Main.HealthDrainPerSecond = value; }
    public bool Shielded => Main.Shielded;
    public int TotalSummoned => Main.TotalSummoned;
    public string VisualEffect => Main.VisualEffect;
    public bool Homing => Main.Homing;
    public int ShotsFired => Main.ShotsFired;
    public AiOutput? Last => Main.Last;
    public string StateId => Main.StateId;
    public AiCompiledState? CurrentState => Main.CurrentState;
    public double StateTime => Main.StateTime;
    public float RolledValue(int transition, int condition) => Main.RolledValue(transition, condition);
    public float WeaponRange(AiGunStats m) => Main.WeaponRange(m);
    public IEnumerable<AiGunStats> ActiveGuns() => Main.ActiveGuns();

    // The units the edited one summoned that are still flying.
    public int MinionsAlive => CountSummons(Main);
    public int LegsDone => Main.LegsDone;
    public int Siblings(string unit) => CountSiblings(Main, unit);

    // ---- world
    public double Time { get; private set; }
    public int PlayerHits { get; private set; }
    public readonly List<AiSimBullet> Bullets = new List<AiSimBullet>();
    public IReadOnlyList<string> Events => events;

    public event Action<string>? EventAdded;

    // How many live units were summoned by this one.
    internal int CountSummons(AiSimUnit by) => CountMatching(by, "", "");

    internal int CountMatching(AiSimUnit by, string unit, string role)
    {
        int count = 0;
        foreach (AiSimUnit u in units)
        {
            if (u.Dead || !ReferenceEquals(u.Summoner, by)) continue;
            if (u.Role != AiSimUnit.RoleSummon && u.Role != AiSimUnit.RolePart) continue;
            if (unit.Length > 0 && !u.SummonUnit.Equals(unit, StringComparison.OrdinalIgnoreCase)) continue;
            if (role.Length > 0 && !u.SummonRole.Equals(role, StringComparison.OrdinalIgnoreCase)) continue;
            count++;
        }
        return count;
    }

    internal int CountSiblings(AiSimUnit who, string unit)
    {
        if (who.Summoner == null) return 0;
        int count = 0;
        foreach (AiSimUnit u in units)
        {
            if (u.Dead || ReferenceEquals(u, who) || !ReferenceEquals(u.Summoner, who.Summoner)) continue;
            if (unit.Length > 0 && !u.SummonUnit.Equals(unit, StringComparison.OrdinalIgnoreCase)) continue;
            count++;
        }
        return count;
    }

    // Starts again: the edited unit next to its leader (when it has one), the player in its start place. The brain and the unit are kept.
    public void Reset()
    {
        Time = 0;
        PlayerHits = 0;
        Bullets.Clear();
        events.Clear();
        units.RemoveAll(u => !ReferenceEquals(u, Main));
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

        Main.ResetCounters();
        Main.Leader = null;
        bool withLeader = LeaderSetup != null && leaderEnabled;
        Main.Spawn(withLeader ? 10f : 0f, 0f);
        if (withLeader) AddLeader();
        SpawnFormation();
    }

    public void RespawnFormation()
    {
        units.RemoveAll(u => u.Role == AiSimUnit.RolePart);
        formationTrail.Clear();
        formationSeen.Clear();
        formationFired.Clear();
        SpawnFormation();
    }

    private void SpawnFormation()
    {
        FormationFile? file = Formation;
        if (file == null) return;
        formationTrail.Clear();
        formationSeen.Clear();
        formationFired.Clear();
        foreach (FormationSlot slot in file.Expand())
        {
            AiSimSetup? setup = SummonResolver?.Invoke(slot.Unit);
            var unit = new AiSimUnit(this, AiSimUnit.RolePart, slot.Id)
            {
                Unit = setup?.Unit ?? new AiUnitStats { Name = slot.Unit, MaxHealth = 40f, VelMin = 0f, VelMax = 8f, Radius = 1.2f },
                Tuning = setup?.Tuning ?? new AiSimTuning(),
                Brain = setup?.Brain,
                Summoner = Main,
                Leader = Main,
                Glued = slot.Glued,
                SlotId = slot.Id,
                SummonRole = slot.Role,
                SummonUnit = slot.Unit,
                Slot = slot
            };
            units.Add(unit);
            FormationMotion.Pose pose = FormationMotion.Place(slot, Main.X, Main.Y, Main.Rot, 0f, file.Spacing,
                Array.Empty<(float, float)>(), file.Points, 0f);
            unit.Spawn(pose.X, pose.Y);
            unit.Rot = pose.Rot;
        }
    }

    private void AddLeader()
    {
        AiSimSetup? setup = LeaderSetup;
        if (setup == null || Leader != null) return;
        var leader = new AiSimUnit(this, AiSimUnit.RoleLeader, setup.Label.Length > 0 ? setup.Label : "leader")
        {
            Unit = setup.Unit,
            Tuning = setup.Tuning,
            Brain = setup.Brain
        };
        units.Add(leader);
        leader.Spawn(0f, 0f);
        Main.Leader = leader;
    }

    // Shows or hides the leader of the unit's group (it comes back at the center, or goes away).
    public void SetLeaderEnabled(bool enabled)
    {
        leaderEnabled = enabled;
        AiSimUnit? leader = Leader;
        if (!enabled && leader != null)
        {
            units.Remove(leader);
            Main.Leader = null;
            Log("the leader was taken away");
        }
        else if (enabled && leader == null && LeaderSetup != null)
        {
            AddLeader();
        }
    }

    public bool LeaderEnabled => leaderEnabled;

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
        Main.Brain = brain;
        Reset();
    }

    // The AI was edited: the new version takes over and the simulation goes on from where it is (no Reset). With nothing
    // to take over from (no AI was running) it starts, as SetBrain does.
    public void ReplaceBrain(AiBrain brain)
    {
        if (Main.Brain == null || Main.Last == null)
        {
            SetBrain(brain);
            return;
        }
        Main.ReplaceBrain(brain);
    }

    // Removes the units the edited unit summoned (to test "own summons alive" conditions).
    public void KillMinions()
    {
        int destroyed = 0;
        foreach (AiSimUnit u in units)
        {
            if (u.Role != AiSimUnit.RoleSummon || u.Dead) continue;
            u.Die(false);
            destroyed++;
        }
        if (destroyed > 0) Log(destroyed + " summoned unit(s) destroyed");
    }

    // The edited unit loses (or, negative, gets back) a fraction of its health.
    public void Damage(float fraction) => Main.Damage(fraction);

    // Called when the edited unit comes back: what it summoned before is gone.
    internal void ClearSummons()
    {
        units.RemoveAll(u => u.Role == AiSimUnit.RoleSummon);
    }

    // Creates "count" units the summoner asked for, behind it (they stay in the arena until they are destroyed).
    internal void Summon(AiSimUnit by, AiSummonOrder order)
    {
        if (order.Count <= 0 || string.IsNullOrWhiteSpace(order.Type)) return;
        AiSimSetup? setup = SummonResolver?.Invoke(order.Type);
        int count = Math.Min(30, order.Count);
        for (int i = 0; i < count; i++)
        {
            if (units.Count(u => u.Role == AiSimUnit.RoleSummon && !u.Dead) >= MaxSummons)
            {
                Log("too many summoned units: " + MaxSummons + " is the limit of the simulator");
                return;
            }
            SummonPoint(by, order, i, count, out float x, out float y);
            var unit = new AiSimUnit(this, AiSimUnit.RoleSummon, order.Type)
            {
                Unit = setup?.Unit ?? new AiUnitStats { Name = order.Type, MaxHealth = 20f, VelMin = 0f, VelMax = 0f, Radius = 1.2f },
                Tuning = setup?.Tuning ?? new AiSimTuning(),
                Brain = setup?.Brain,
                Summoner = by,
                Leader = by.Leader ?? by,
                SummonUnit = order.Type,
                SummonRole = order.Role
            };
            if (setup == null && i == 0) Log("the summoned type '" + order.Type + "' is not known here: a dummy that stands still takes its place");
            units.Add(unit);
            unit.Spawn(x, y);
        }
    }

    private void SummonPoint(AiSimUnit by, AiSummonOrder order, int index, int count, out float x, out float y)
    {
        string place = (order.Place ?? "behind").ToLowerInvariant();
        if (place.StartsWith("point:", StringComparison.OrdinalIgnoreCase))
        {
            string rest = place.Substring(6);
            int slash = rest.IndexOf('/');
            FormationFile? file = slash >= 0 ? KnownFormations?.Get(rest.Substring(0, slash)) : Formation;
            file ??= Formation;
            if (FormationMotion.TryPointOffset(place, file, out float localX, out float localY))
            {
                FormationMotion.Pose pose = FormationMotion.Slot(by.X, by.Y, by.Rot, 0f, localX, localY);
                x = pose.X;
                y = pose.Y;
                return;
            }
        }
        float distance = order.Distance <= 0f ? 5f : order.Distance;
        float spread = count <= 1 ? 0f : (index - (count - 1) * 0.5f) * (place == "ring" ? 360f / count : 40f);
        float facing = place switch
        {
            "front" => by.Rot,
            "left" => by.Rot - 90f,
            "right" => by.Rot + 90f,
            "ring" or "around" => order.Angle + (count <= 1 ? 0f : index * 360f / count),
            _ => by.Rot + 180f + spread
        };
        float rad = facing * AiMath.Rad;
        x = by.X + MathF.Sin(rad) * distance;
        y = by.Y + MathF.Cos(rad) * distance;
    }

    private void PlaceFormation(float dt)
    {
        FormationFile? file = Formation;
        if (file == null) return;
        List<AiSimUnit> parts = units.Where(u => u.Role == AiSimUnit.RolePart).ToList();
        if (parts.Count == 0) return;
        if (formationTrail.Count == 0 || AiMath.Distance(formationTrail[^1].X, formationTrail[^1].Y, Main.X, Main.Y) >= 0.5f)
            formationTrail.Add((Main.X, Main.Y));
        float needed = (parts.Count + 1) * Math.Max(0.4f, file.Spacing) + 6f;
        TrimTrail(needed);

        var actors = new List<FormationActor>();
        var byId = new Dictionary<string, AiSimUnit>(StringComparer.OrdinalIgnoreCase) { [ "leader" ] = Main };
        actors.Add(new FormationActor
        {
            Id = "leader", IsLeader = true, Alive = !Main.Dead,
            HealthFraction = Main.MaxHealth > 0f ? Main.Health / Main.MaxHealth : 1f,
            ShieldSuppressed = Main.ShieldSuppressed
        });
        var roleCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (AiSimUnit part in parts)
        {
            string role = part.SummonRole;
            roleCount.TryGetValue(role, out int n);
            roleCount[role] = n + 1;
            byId[part.SlotId] = part;
            actors.Add(new FormationActor
            {
                Id = part.SlotId, Role = role, RoleOrder = n, IsLeader = false, Alive = !part.Dead,
                HealthFraction = part.MaxHealth > 0f ? part.Health / part.MaxHealth : 0f,
                ShieldSuppressed = part.ShieldSuppressed
            });
        }
        var orders = new List<FormationOrder>();
        FormationRules.Evaluate(file, actors, formationSeen, formationFired, orders);
        foreach (FormationOrder order in orders)
        {
            if (!byId.TryGetValue(order.ActorId, out AiSimUnit? who) || who.Dead) continue;
            if (order.ClearShield) { who.ShieldSuppressed = true; who.SetShield(false); }
            if (order.HealFraction > 0f) who.Damage(-order.HealFraction);
            if (order.Kill) { who.ShieldSuppressed = true; who.SetShield(false); who.Damage(2f); }
        }
        ApplyShieldFlags(Main, "leader", orders);
        foreach (AiSimUnit part in parts) ApplyShieldFlags(part, part.SlotId, orders);

        foreach (AiSimUnit part in parts)
        {
            if (part.Dead || part.Slot == null || !part.Glued) continue;
            FormationSlot slot = part.Slot;
            FormationMotion.Pose pose = FormationMotion.Place(slot, Main.X, Main.Y, Main.Rot, Main.Vel, file.Spacing,
                formationTrail, file.Points, (float)Time);
            if (slot.FaceLocked)
            {
                part.X = pose.X;
                part.Y = pose.Y;
                part.Rot = pose.Rot;
                part.Vel = pose.Speed;
            }
            else if (slot.Kind == "follow")
            {
                float x = part.X, y = part.Y, rot = part.Rot, vel = part.Vel;
                float own = part.Unit.VelMax > 1f ? part.Unit.VelMax : 8f;
                float top = Math.Max(own, Main.Vel);
                float turn = part.Unit.Turn > 1f ? part.Unit.Turn : 180f;
                FormationMotion.Follow(ref x, ref y, ref rot, ref vel, pose, Main.Vel, Math.Min(part.Unit.VelMin, top), top, turn, dt);
                part.X = x;
                part.Y = y;
                part.Rot = rot;
                part.Vel = vel;
            }
            else
            {
                float x = part.X, y = part.Y, rot = part.Rot, vel = part.Vel;
                float speed = slot.TravelSpeed > 0.05f ? slot.TravelSpeed : Math.Max(1f, part.Unit.VelMax);
                FormationMotion.StepToward(ref x, ref y, ref rot, ref vel, pose, speed, dt);
                part.X = x;
                part.Y = y;
                part.Rot = rot;
                part.Vel = vel;
            }
        }
    }

    private static void ApplyShieldFlags(AiSimUnit who, string id, List<FormationOrder> orders)
    {
        if (orders.Any(o => o.ClearShield && o.ActorId.Equals(id, StringComparison.OrdinalIgnoreCase)))
            who.ShieldSuppressed = true;
        bool want = !who.ShieldSuppressed && orders.Any(o => o.Shield && o.ActorId.Equals(id, StringComparison.OrdinalIgnoreCase));
        string effect = "";
        if (want)
        {
            foreach (FormationOrder order in orders)
            {
                if (!order.Shield || !order.ActorId.Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
                effect = order.Effect ?? "";
                break;
            }
        }
        // Every tick, including "off". Skipping the off left the last "on" stuck after the link stopped holding.
        who.SetShield(want, effect);
    }

    private void TrimTrail(float needed)
    {
        if (formationTrail.Count == 0) return;
        float length = AiMath.Distance(formationTrail[^1].X, formationTrail[^1].Y, Main.X, Main.Y);
        int keepFrom = formationTrail.Count - 1;
        while (keepFrom > 0 && length < needed)
        {
            length += AiMath.Distance(formationTrail[keepFrom].X, formationTrail[keepFrom].Y, formationTrail[keepFrom - 1].X, formationTrail[keepFrom - 1].Y);
            keepFrom--;
        }
        if (keepFrom > 0) formationTrail.RemoveRange(0, keepFrom);
    }

    internal void Log(string text)
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

        // A copy: a unit that summons adds to the list while it is stepped.
        foreach (AiSimUnit u in units.ToList()) u.Step(dt);
        PlaceFormation(dt);
        units.RemoveAll(u => u.Role == AiSimUnit.RoleSummon && u.Dead && Time - u.DiedAt > WreckSeconds);

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
