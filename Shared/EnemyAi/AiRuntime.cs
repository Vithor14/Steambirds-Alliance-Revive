namespace SteambirdsAi;

// What the AI of one unit sees this tick.
public struct AiSense
{
    public float X, Y, Rot, Vel;
    public float VelMin, VelMax;
    public float HomeX, HomeY;
    public float HealthFraction;
    public bool HasTarget;
    public float TargetX, TargetY, TargetVX, TargetVY;
    // The leader of the unit's group (open world: the leader of a group of enemies), when it has one that is alive.
    public bool HasLeader;
    public float LeaderX, LeaderY;
    public double Now;
    public float Dt;
    // Time of the last damage the unit took (negative = never).
    public double LastHitAt;
    // How many of the units this one summoned are still alive (every kind).
    public int SummonsAlive;
    // (unit, role) -> how many of those summons are alive. Empty unit and empty role means all of them.
    // Null when the caller did not count: a filtered condition then uses SummonsAlive.
    public Func<string, string, int>? CountMatching;
    // The unit that summoned this one, when it is still alive (orbit "around": "summoner").
    public bool HasSummoner;
    public float SummonerX, SummonerY, SummonerRot;
    // siblingsAtMost: other live units this unit's summoner summoned (argument: only that summon type; "" = any).
    public Func<string, int>? CountSiblings;
}

// A summon the AI asks for: the caller creates "Count" units of the type near the summoner.
public sealed class AiSummonOrder
{
    public string Type = "";
    public int Count;
    public string Ai = "";
    public string Role = "";
    public string Place = "behind";
    public float Distance = 5f;
    public float Angle;
}

// What the AI decided this tick. The caller turns toward (TargetX, TargetY), speeds toward TargetVel (AiMotion.Steer) and
// shoots following the Fire* fields.
public sealed class AiOutput
{
    public float TargetX, TargetY, TargetVel;
    public bool Hold;

    public bool FireEnabled = true;
    public int[] Guns = Array.Empty<int>();
    public float RateMult = 1f;
    public float Range;
    // Negative = use the aim lead of the unit type.
    public float AimLead = -1f;

    // The unit must blow up now (contact with the player, or a state with selfDestruct).
    public bool Explode;

    public AiCompiledState State = null!;
    // The state changed this tick: PreviousState is the one it came from.
    public bool Entered;
    public AiCompiledState? PreviousState;
    // Every state entered this tick, in order (more than one when transitions chain: a big hit that crosses two health limits).
    // The caller applies the "on enter" parts of each (message, bark, look, homing bullets).
    public readonly List<AiCompiledState> EnteredStates = new List<AiCompiledState>();

    // The state asks for the unit's built-in flight (mode "builtin"): Builtin is its name ("" = the unit type's own).
    public bool UseBuiltin;
    public string Builtin = "";
    public float SpeedMult = 1f;
    public float TurnMult = 1f;
    // When set, replaces the unit's own top speed for this tick (a unit the game gives speed 0 can still move).
    public float TopSpeed;
    // Slide: moves straight to the destination while the body turns on its own (AiMotion.Slide).
    public bool Slide;
    public float SpinRate;
    // Absolute heading the body turns toward. NaN = spin at SpinRate instead.
    public float FaceRot = float.NaN;

    // The shield of the state is up now (ShieldEffect = the look while it lasts, "" = the default one).
    public bool ShieldOn;
    public string ShieldEffect = "";

    // Units to summon this tick (empty most of the time).
    public readonly List<AiSummonOrder> Summons = new List<AiSummonOrder>();
}

// The running AI of one unit: the state it is in and what it needs to remember. Create one per unit.
public sealed class AiRuntime
{
    private const int MaxChainedTransitions = 8;

    private readonly Random rng;
    private bool started;
    private int stateIndex;
    private double enteredAt;
    private float[][] rolled = Array.Empty<float[]>();

    // wander
    private float patrolX, patrolY;
    private double nextPatrol;
    private bool needPatrol = true;

    // summons and shield of the current state (one clock per summon line)
    private double[] summonAt = Array.Empty<double>();
    private double shieldUntil;
    private int pathIndex;
    // Zigzag dashes finished in this state, and the path points reached, for legsDone.
    private int legsDone;
    public int LegsDone => legsDone;
    private int legsEver;
    private bool needLeg = true;
    private bool pausing;
    private bool paused;
    private double pauseUntil;
    private double legDeadline;
    private float legX, legY, legHeading;
    private float zigSide = 1f;
    // orbit lock: the slot on the circle, kept relative to the summoner's facing.
    private float lockAngle = float.NaN;
    private float lastSummonerRot = float.NaN;
    private float summonerTurn = 1f;

    // 1 clockwise, -1 counter-clockwise (when the move does not say).
    private readonly float defaultSpin;

    public AiRuntime(AiBrain brain, Random rng)
    {
        Brain = brain;
        this.rng = rng;
        defaultSpin = rng.Next(2) == 0 ? -1f : 1f;
        Out.State = brain.States[brain.Initial];
    }

    public AiBrain Brain { get; private set; }
    public AiOutput Out { get; } = new AiOutput();
    public AiCompiledState State => Brain.States[stateIndex];
    public double StateTime(double now) => now - enteredAt;

    // The number a condition of the current state is waiting for (the random time of a timeInState with a max was rolled when the
    // state started). 0 when there is no such condition.
    public float Rolled(int transition, int condition)
    {
        if (transition < 0 || transition >= rolled.Length || condition < 0 || condition >= rolled[transition].Length) return 0f;
        return rolled[transition][condition];
    }

    public AiOutput Step(in AiSense s)
    {
        AiOutput o = Out;
        o.Entered = false;
        o.PreviousState = null;
        o.EnteredStates.Clear();
        o.Summons.Clear();

        if (!started)
        {
            started = true;
            Enter(Brain.Initial, s);
            o.Entered = true;
        }

        float dist = s.HasTarget ? AiMath.Distance(s.X, s.Y, s.TargetX, s.TargetY) : float.MaxValue;

        for (int guard = 0; guard < MaxChainedTransitions; guard++)
        {
            AiCompiledState state = Brain.States[stateIndex];
            int next = -1;
            for (int t = 0; t < state.Transitions.Length && next < 0; t++)
            {
                AiCompiledTransition tr = state.Transitions[t];
                bool all = true;
                for (int c = 0; c < tr.When.Length && all; c++)
                    all = Evaluate(tr.When[c], rolled[t][c], s, dist);
                if (all) next = tr.To;
            }
            if (next < 0) break;
            if (!o.Entered) o.PreviousState = state;
            Enter(next, s);
            o.Entered = true;
        }

        Compute(s, dist);
        return o;
    }

    private void Enter(int index, in AiSense s)
    {
        double now = s.Now;
        stateIndex = index;
        enteredAt = now;
        needPatrol = true;
        pathIndex = 0;
        legsDone = 0;
        legsEver = 0;
        needLeg = true;
        pausing = false;
        paused = false;
        lockAngle = float.NaN;
        lastSummonerRot = float.NaN;
        summonerTurn = 1f;

        AiCompiledState state = Brain.States[index];
        Out.EnteredStates.Add(state);

        // Shield: for a number of seconds, or for as long as the state lasts.
        shieldUntil = !state.Shield ? 0.0 : state.ShieldSeconds > 0f ? now + state.ShieldSeconds : double.MaxValue;

        // Summons: at once when the state starts, then every "every" seconds, each line on its own clock and its own cap.
        summonAt = new double[state.Summons.Length];
        for (int i = 0; i < state.Summons.Length; i++)
        {
            QueueSummon(state.Summons[i], s);
            summonAt[i] = state.Summons[i].Every > 0f ? now + state.Summons[i].Every : double.MaxValue;
        }

        Roll(state);
    }

    private void QueueSummon(AiSummonEntry line, in AiSense s)
    {
        if (line.Unit.Length == 0 || line.Count <= 0) return;
        if (line.Max > 0)
        {
            int alive = s.CountMatching != null ? s.CountMatching(line.Unit, line.Role) : s.SummonsAlive;
            if (alive >= line.Max) return;
        }
        Out.Summons.Add(new AiSummonOrder
        {
            Type = line.Unit,
            Count = line.Count,
            Ai = line.Ai,
            Role = line.Role,
            Place = line.Place.Length == 0 ? "behind" : line.Place,
            Distance = line.Distance <= 0f ? 5f : line.Distance,
            Angle = line.Angle
        });
    }

    // The numbers the conditions of a state wait for (a timeInState with a maximum is a random time between the two).
    private void Roll(AiCompiledState state)
    {
        rolled = new float[state.Transitions.Length][];
        for (int t = 0; t < state.Transitions.Length; t++)
        {
            AiCondition[] when = state.Transitions[t].When;
            rolled[t] = new float[when.Length];
            for (int c = 0; c < when.Length; c++)
            {
                AiCondition cond = when[c];
                rolled[t][c] = cond.Max.HasValue && cond.Max.Value > cond.Value
                    ? cond.Value + (float)rng.NextDouble() * (cond.Max.Value - cond.Value)
                    : cond.Value;
            }
        }
    }

    // The AI was edited while it runs (the simulator of the editor): the new version takes over without starting again.
    // The unit stays in the state it is in, and keeps the time it has been there, when the new version still has a state
    // with that id; the numbers its conditions wait for are rolled again. Without that state it starts from the initial one.
    public void Rebind(AiBrain next)
    {
        string id = started ? Brain.States[stateIndex].Id : "";
        Brain = next;
        int index = id.Length == 0 ? -1 : Array.FindIndex(next.States, s => s.Id == id);
        if (index < 0)
        {
            started = false;
            Out.State = next.States[next.Initial];
            return;
        }
        stateIndex = index;
        Out.State = next.States[index];
        Roll(next.States[index]);
        summonAt = new double[next.States[index].Summons.Length];
        for (int i = 0; i < summonAt.Length; i++) summonAt[i] = double.MaxValue;
        if (!next.States[index].Shield) shieldUntil = 0.0;
    }

    private bool Evaluate(AiCondition c, float value, in AiSense s, float dist)
    {
        bool result;
        switch (c.Type)
        {
            case "always":
                result = true;
                break;
            case "timeInState":
                result = s.Now - enteredAt >= value;
                break;
            case "playerWithin":
                result = s.HasTarget && dist <= value;
                break;
            case "playerBeyond":
                result = s.HasTarget && dist > value;
                break;
            case "hasTarget":
                result = s.HasTarget;
                break;
            case "healthBelow":
                result = s.HealthFraction <= value;
                break;
            case "healthAbove":
                result = s.HealthFraction >= value;
                break;
            case "recentlyHit":
                result = s.LastHitAt >= 0 && s.Now - s.LastHitAt <= value;
                break;
            case "facingTarget":
                result = s.HasTarget &&
                    Math.Abs(AiMath.AngleDifference(s.Rot, AiMath.Heading(s.X, s.Y, s.TargetX, s.TargetY))) <= value;
                break;
            case "summonsAtMost":
            {
                int alive = s.SummonsAlive;
                if ((c.Unit.Length > 0 || c.Role.Length > 0) && s.CountMatching != null) alive = s.CountMatching(c.Unit, c.Role);
                result = alive <= value;
                break;
            }
            case "legsDone":
                result = legsDone >= value;
                break;
            case "siblingsAtMost":
                result = (s.CountSiblings?.Invoke((c.Unit ?? "").Trim()) ?? 0) <= value;
                break;
            case "chance":
                // Probability per second, whatever the tick length.
                result = rng.NextDouble() < 1.0 - Math.Exp(-Math.Max(0f, value) * Math.Max(0f, s.Dt));
                break;
            default:
                result = false;
                break;
        }
        return c.Not ? !result : result;
    }

    // ------------------------------------------------------------------ what the state does

    private static bool OrbitCenter(string around, in AiSense s, out float cx, out float cy)
    {
        switch ((around ?? "").Trim().ToLowerInvariant())
        {
            case "summoner":
                cx = s.SummonerX;
                cy = s.SummonerY;
                return s.HasSummoner;
            case "home":
                cx = s.HomeX;
                cy = s.HomeY;
                return true;
            case "leader":
                cx = s.LeaderX;
                cy = s.LeaderY;
                return s.HasLeader;
            default:
                cx = s.TargetX;
                cy = s.TargetY;
                return s.HasTarget;
        }
    }

    // Flies to a circle of "radius" around (cx, cy) and goes around it; far from the circle it goes at "farSpeed".
    private void Orbit(in AiSense s, AiMove m, float cx, float cy, float radius, float distance, float speed, float top, AiOutput o)
    {
        float spin = m.Spin > 0 ? 1f : m.Spin < 0 ? -1f : defaultSpin;
        float heading = AiMath.Heading(cx, cy, s.X, s.Y);
        float a = (heading + spin * m.Deviation) * AiMath.Rad;
        o.TargetX = cx + MathF.Sin(a) * radius;
        o.TargetY = cy + MathF.Cos(a) * radius;
        float far = m.FarSpeed ?? speed;
        o.TargetVel = top * (distance > radius * Math.Max(1f, m.FarFactor) ? far : speed);
    }

    private void Compute(in AiSense s, float dist)
    {
        AiOutput o = Out;
        AiCompiledState state = Brain.States[stateIndex];
        AiMove m = state.Move;
        AiFire f = state.Fire;

        o.State = state;
        o.Hold = false;
        o.UseBuiltin = false;
        o.Builtin = "";
        o.SpeedMult = 1f;

        // Periodic summons of the state, one clock per line.
        for (int i = 0; i < state.Summons.Length && i < summonAt.Length; i++)
        {
            if (state.Summons[i].Every <= 0f || s.Now < summonAt[i]) continue;
            summonAt[i] = s.Now + state.Summons[i].Every;
            QueueSummon(state.Summons[i], s);
        }
        o.ShieldOn = s.Now < shieldUntil;
        o.ShieldEffect = state.ShieldEffect;

        o.TopSpeed = Math.Max(0f, m.TopSpeed);
        float top = o.TopSpeed > 0f ? o.TopSpeed : Math.Max(0f, s.VelMax);
        o.SpinRate = m.SpinRate;
        o.Slide = m.SpinRate != 0f;
        o.FaceRot = float.NaN;
        float speed = Math.Max(0f, m.Speed);
        float ahead = 8f;
        float forwardX = s.X + MathF.Sin(s.Rot * AiMath.Rad) * ahead;
        float forwardY = s.Y + MathF.Cos(s.Rot * AiMath.Rad) * ahead;

        switch (m.Mode)
        {
            case "orbit":
                if (OrbitCenter(m.Around, s, out float ox, out float oy))
                {
                    if (m.Lock && m.Around.Length > 0 && !m.Around.Equals("player", StringComparison.OrdinalIgnoreCase))
                    {
                        // A fixed place on the circle, turning with the summoner's body.
                        float baseRot = m.Around.Equals("summoner", StringComparison.OrdinalIgnoreCase) && s.HasSummoner ? s.SummonerRot : 0f;
                        float radius = Math.Max(1f, m.Distance);
                        if (float.IsNaN(lockAngle)) lockAngle = AiMath.NormalizeAngle(AiMath.Heading(ox, oy, s.X, s.Y) - baseRot);
                        float slot = baseRot + lockAngle;
                        o.TargetX = ox + MathF.Sin(slot * AiMath.Rad) * radius;
                        o.TargetY = oy + MathF.Cos(slot * AiMath.Rad) * radius;
                        float gap = AiMath.Distance(s.X, s.Y, o.TargetX, o.TargetY);
                        o.TargetVel = Math.Min(top * Math.Max(0.1f, m.FarSpeed ?? Math.Max(speed, 0.6f)), gap * 4f);
                        o.Slide = true;
                        if (m.FaceForward)
                        {
                            if (!float.IsNaN(lastSummonerRot))
                            {
                                float turned = AiMath.AngleDifference(lastSummonerRot, baseRot);
                                if (Math.Abs(turned) > 0.001f) summonerTurn = turned > 0f ? 1f : -1f;
                            }
                            lastSummonerRot = baseRot;
                            o.FaceRot = AiMath.NormalizeAngle(slot + 90f * summonerTurn);
                        }
                        else o.FaceRot = AiMath.NormalizeAngle(slot + (m.FaceOffset == 0f ? 90f : m.FaceOffset));
                    }
                    else Orbit(s, m, ox, oy, Math.Max(2f, m.Distance), AiMath.Distance(s.X, s.Y, ox, oy), speed, top, o);
                }
                else
                {
                    o.TargetX = forwardX;
                    o.TargetY = forwardY;
                    o.TargetVel = top * speed;
                }
                break;

            case "path":
            {
                List<AiPoint>? points = m.Points;
                int count = points?.Count ?? 0;
                if (count == 0)
                {
                    o.Hold = true;
                    o.TargetX = forwardX;
                    o.TargetY = forwardY;
                    o.TargetVel = 0f;
                    break;
                }
                if (pathIndex >= count) pathIndex = m.Loop ? 0 : count - 1;
                AiPoint point = points![pathIndex];
                float px = s.HomeX + point.X;
                float py = s.HomeY + point.Y;
                float left = AiMath.Distance(s.X, s.Y, px, py);
                bool last = !m.Loop && pathIndex == count - 1;
                float reach = o.Slide ? 0.3f : Math.Max(1.5f, top * speed * 0.35f);
                if (!last && left < reach)
                {
                    legsDone++;
                    pathIndex = (pathIndex + 1) % count;
                    point = points[pathIndex];
                    px = s.HomeX + point.X;
                    py = s.HomeY + point.Y;
                    left = AiMath.Distance(s.X, s.Y, px, py);
                    last = !m.Loop && pathIndex == count - 1;
                }
                if (last && left < 1f)
                {
                    o.Hold = true;
                    o.TargetX = s.HasTarget ? s.TargetX : forwardX;
                    o.TargetY = s.HasTarget ? s.TargetY : forwardY;
                    o.TargetVel = 0f;
                    break;
                }
                o.TargetX = px;
                o.TargetY = py;
                o.TargetVel = top * speed * (last ? Math.Clamp(left / 4f, 0.25f, 1f) : 1f);
                break;
            }

            case "zigzag":
            {
                // Straight dashes of "distance" units, each "deviation" degrees off the line to the player, then the other side.
                float legLength = Math.Max(0.5f, m.Distance);
                float pace = Math.Max(0.1f, top * speed);
                bool arrived = !needLeg && AiMath.Distance(s.X, s.Y, legX, legY) < 0.3f;
                if (!needLeg && (arrived || s.Now >= legDeadline))
                {
                    legsDone++;
                    legsEver++;
                    needLeg = true;
                    paused = false;
                }
                if (needLeg && m.Pause > 0f && legsEver > 0 && !paused)
                {
                    if (!pausing)
                    {
                        pausing = true;
                        pauseUntil = s.Now + m.Pause;
                    }
                    float faceTo = s.HasTarget ? AiMath.Heading(s.X, s.Y, s.TargetX, s.TargetY) : s.Rot;
                    bool facing = Math.Abs(AiMath.AngleDifference(s.Rot, faceTo)) < 12f;
                    if (s.Now < pauseUntil || (!facing && s.Now < pauseUntil + 1.0))
                    {
                        o.TargetX = s.X;
                        o.TargetY = s.Y;
                        o.TargetVel = 0f;
                        o.Slide = true;
                        o.FaceRot = faceTo;
                        break;
                    }
                    pausing = false;
                    paused = true;
                }
                if (needLeg)
                {
                    needLeg = false;
                    float toward = s.HasTarget ? AiMath.Heading(s.X, s.Y, s.TargetX, s.TargetY) : s.Rot;
                    float jitter = Math.Max(0f, m.Jitter);
                    float off = Math.Clamp(m.Deviation + (float)(rng.NextDouble() * 2.0 - 1.0) * jitter, 0f, 170f);
                    if (s.HasTarget && dist < legLength * 1.5f)
                    {
                        toward += 90f * defaultSpin;
                        off = Math.Min(off, 35f);
                    }
                    legHeading = AiMath.NormalizeAngle(toward + zigSide * off);
                    zigSide = -zigSide;
                    legX = s.X + MathF.Sin(legHeading * AiMath.Rad) * legLength;
                    legY = s.Y + MathF.Cos(legHeading * AiMath.Rad) * legLength;
                    legDeadline = s.Now + legLength / pace * 2.0 + 0.5;
                }
                o.TargetX = legX;
                o.TargetY = legY;
                o.TargetVel = pace;
                o.Slide = true;
                o.FaceRot = legHeading;
                break;
            }

            case "orbitLeader":
                if (s.HasLeader)
                {
                    // Circles the leader of its group (the escorts of a group in the open world).
                    Orbit(s, m, s.LeaderX, s.LeaderY, Math.Max(2f, m.Distance), AiMath.Distance(s.X, s.Y, s.LeaderX, s.LeaderY), speed, top, o);
                }
                else if (s.HasTarget)
                {
                    // No leader (it died, or the unit has none): goes for the player, on a wider circle than the leader's.
                    Orbit(s, m, s.TargetX, s.TargetY, Math.Max(14f, m.Distance * 2f), dist, speed, top, o);
                }
                else
                {
                    o.TargetX = forwardX;
                    o.TargetY = forwardY;
                    o.TargetVel = top * speed;
                }
                break;

            case "approach":
                if (s.HasTarget)
                {
                    o.TargetX = s.TargetX + s.TargetVX * m.Lead;
                    o.TargetY = s.TargetY + s.TargetVY * m.Lead;
                }
                else
                {
                    o.TargetX = forwardX;
                    o.TargetY = forwardY;
                }
                o.TargetVel = top * speed;
                break;

            case "straight":
                o.TargetX = s.X + MathF.Sin(s.Rot * AiMath.Rad) * 20f;
                o.TargetY = s.Y + MathF.Cos(s.Rot * AiMath.Rad) * 20f;
                o.TargetVel = top * speed;
                break;

            case "wander":
                if (needPatrol || s.Now >= nextPatrol || AiMath.Distance(s.X, s.Y, patrolX, patrolY) < 3f)
                {
                    float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                    float r = Math.Max(1f, m.Distance) * (0.35f + 0.65f * (float)rng.NextDouble());
                    patrolX = s.HomeX + MathF.Sin(angle) * r;
                    patrolY = s.HomeY + MathF.Cos(angle) * r;
                    float interval = Math.Max(0.5f, m.Interval);
                    nextPatrol = s.Now + interval * (0.8 + 0.4 * rng.NextDouble());
                    needPatrol = false;
                }
                o.TargetX = patrolX;
                o.TargetY = patrolY;
                o.TargetVel = top * speed;
                break;

            case "hold":
                o.Hold = true;
                if (m.Face && s.HasTarget)
                {
                    o.TargetX = s.TargetX;
                    o.TargetY = s.TargetY;
                }
                else
                {
                    o.TargetX = forwardX;
                    o.TargetY = forwardY;
                }
                o.TargetVel = 0f;
                break;

            case "flee":
                if (s.HasTarget)
                {
                    float away = AiMath.Heading(s.TargetX, s.TargetY, s.X, s.Y) * AiMath.Rad;
                    o.TargetX = s.X + MathF.Sin(away) * 20f;
                    o.TargetY = s.Y + MathF.Cos(away) * 20f;
                }
                else
                {
                    o.TargetX = forwardX;
                    o.TargetY = forwardY;
                }
                o.TargetVel = top * speed;
                break;

            case "home":
            {
                float toHome = AiMath.Distance(s.X, s.Y, s.HomeX, s.HomeY);
                if (toHome > 3f)
                {
                    o.TargetX = s.HomeX;
                    o.TargetY = s.HomeY;
                }
                else
                {
                    float around = (float)(s.Now * 90.0) * AiMath.Rad;
                    o.TargetX = s.HomeX + MathF.Sin(around) * 3f;
                    o.TargetY = s.HomeY + MathF.Cos(around) * 3f;
                }
                o.TargetVel = top * speed;
                break;
            }

            case "builtin":
                // The server flies it with its own code (Enemies.Think); the values below only say where to look.
                o.UseBuiltin = true;
                o.Builtin = (m.Builtin ?? "").Trim().ToLowerInvariant();
                o.SpeedMult = m.SpeedMult > 0f ? m.SpeedMult : 1f;
                o.TargetX = s.HasTarget ? s.TargetX : forwardX;
                o.TargetY = s.HasTarget ? s.TargetY : forwardY;
                o.TargetVel = top * 0.7f * o.SpeedMult;
                break;

            default: // "hold" is what a broken state falls back to
                o.Hold = true;
                o.TargetX = forwardX;
                o.TargetY = forwardY;
                o.TargetVel = 0f;
                break;
        }

        if (m.Mode != "zigzag")
        {
            if (m.Pause > 0f && (s.Now % (m.Pause * 2f)) < m.Pause) o.TargetVel = 0f;
            if (m.Jitter > 0f)
            {
                float j = m.Jitter;
                o.TargetX += (float)(rng.NextDouble() * 2.0 - 1.0) * j;
                o.TargetY += (float)(rng.NextDouble() * 2.0 - 1.0) * j;
            }
        }
        o.TurnMult = m.TurnMult > 0f ? m.TurnMult : 1f;
        o.SpeedMult = m.SpeedMult > 0f ? m.SpeedMult : o.SpeedMult;

        o.FireEnabled = f.Enabled;
        o.Guns = f.Guns is { Count: > 0 } ? f.Guns.ToArray() : Array.Empty<int>();
        o.RateMult = f.RateMult > 0f ? f.RateMult : 0f;
        o.Range = Math.Max(0f, f.Range);
        o.AimLead = f.AimLead ?? -1f;

        float radiusExplode = state.Contact.ExplodeRadius;
        o.Explode = state.SelfDestruct && o.Entered ||
                    (radiusExplode > 0f && s.HasTarget && dist <= radiusExplode);
    }
}
