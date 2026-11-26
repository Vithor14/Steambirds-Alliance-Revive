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
    public double Now;
    public float Dt;
    // Time of the last damage the unit took (negative = never).
    public double LastHitAt;
    // How many of the units this one summoned are still alive.
    public int SummonsAlive;
    // The unit that summoned this one, while it is alive (orbit around "summoner").
    public bool HasSummoner;
    public float SummonerX, SummonerY, SummonerRot;
    // siblingsAtMost: how many other units this unit's summoner summoned are alive (argument: only that unit type; "" = any).
    // Null = the unit was not summoned (counts as 0).
    public Func<string, int>? CountSiblings;
}

// A summon the AI asks for: the caller creates "Count" units of the type near the summoner (behind it, or evenly spaced on a
// circle around it when Around is true), Distance units away.
public sealed class AiSummonOrder
{
    public string Type = "";
    public int Count;
    public bool Around;
    public float Distance = 5f;
    // Around: degrees the circle is turned.
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
    // Multiplier of the unit's turn rate in this state (move.turnMult; every mode).
    public float TurnMult = 1f;
    // Top speed of the unit in this state, in units per second (move.topSpeed; 0 = the unit's own).
    public float TopSpeed;
    // Slide: the unit moves straight to (TargetX, TargetY) without turning its nose toward it (AiMotion.Slide). Its body then turns
    // at SpinRate degrees per second, or toward FaceRot (an absolute heading; NaN = not set) when that is set.
    public bool Slide;
    public float SpinRate;
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

    // path: the point the unit is flying to (starts again at 0 when the state starts)
    private int pathIndex;

    // orbit with lock: the unit's place on the circle, in degrees from the summoner's facing (NaN = taken on the first tick).
    // Kept across states, so a guard keeps its place when it changes state.
    private float lockAngle = float.NaN;

    // zigzag: the leg it is flying (a straight dash), which side the next one goes (kept across states) and the legs done in
    // this state (also the points a path reached), for the legsDone condition.
    private bool needLeg = true;
    private float legX, legY, legHeading;
    private double legDeadline;
    private float zigSide = 1f;
    private int legsDone;
    // zigzag with pause: dashes done since the unit was born (the first one has no stop before it), and the stop before the next one.
    private int legsEver;
    private bool pausing, paused;
    private double pauseUntil;
    // orbit with faceForward: the way the summoner turns (1 clockwise, -1 counter-clockwise) and its facing on the last tick.
    private float summonerTurn = 1f;
    private float lastSummonerRot = float.NaN;

    // summons and shield of the current state
    private double nextSummonAt = double.MaxValue;
    private double shieldUntil;

    // 1 clockwise, -1 counter-clockwise (when the move does not say).
    private readonly float defaultSpin;

    public AiRuntime(AiBrain brain, Random rng)
    {
        Brain = brain;
        this.rng = rng;
        defaultSpin = rng.Next(2) == 0 ? -1f : 1f;
        // Units of a wave do not all zigzag to the same side first.
        zigSide = rng.Next(2) == 0 ? -1f : 1f;
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
        needLeg = true;
        legsDone = 0;

        AiCompiledState state = Brain.States[index];
        Out.EnteredStates.Add(state);

        // Shield: for a number of seconds, or for as long as the state lasts.
        shieldUntil = !state.Shield ? 0.0 : state.ShieldSeconds > 0f ? now + state.ShieldSeconds : double.MaxValue;

        // Summons: at once when the state starts (no limit check against "max" when it is 0), then every "every" seconds.
        nextSummonAt = double.MaxValue;
        if (state.SummonType.Length > 0)
        {
            if (state.SummonMax <= 0 || s.SummonsAlive < state.SummonMax) Out.Summons.Add(OrderOf(state));
            if (state.SummonEvery > 0f) nextSummonAt = now + state.SummonEvery;
        }

        Roll(state);
    }

    private static AiSummonOrder OrderOf(AiCompiledState state)
    {
        return new AiSummonOrder { Type = state.SummonType, Count = state.SummonCount, Around = state.SummonAround, Distance = state.SummonDistance, Angle = state.SummonAngle };
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
        if (next.States[index].SummonType.Length == 0) nextSummonAt = double.MaxValue;
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
                result = s.SummonsAlive <= value;
                break;
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
        o.TurnMult = m.TurnMult > 0f ? m.TurnMult : 1f;

        // Periodic summons of the state.
        if (state.SummonType.Length > 0 && state.SummonEvery > 0f && s.Now >= nextSummonAt)
        {
            nextSummonAt = s.Now + state.SummonEvery;
            if (state.SummonMax <= 0 || s.SummonsAlive < state.SummonMax)
                o.Summons.Add(OrderOf(state));
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
            {
                // The center: the player, the summoner (its home when it has none) or home.
                string around = AiCatalog.OrbitCenterOf(m);
                bool hasCenter = around != "player" || s.HasTarget;
                float cx = s.TargetX, cy = s.TargetY;
                if (around == "summoner" && s.HasSummoner)
                {
                    cx = s.SummonerX;
                    cy = s.SummonerY;
                }
                else if (around != "player")
                {
                    cx = s.HomeX;
                    cy = s.HomeY;
                }
                if (m.Lock && around != "player")
                {
                    // A fixed place on the circle, turning with the summoner's body (its home and facing 0 when there is none).
                    float baseRot = around == "summoner" && s.HasSummoner ? s.SummonerRot : 0f;
                    float radius = Math.Max(1f, m.Distance);
                    if (float.IsNaN(lockAngle)) lockAngle = AiMath.NormalizeAngle(AiMath.Heading(cx, cy, s.X, s.Y) - baseRot);
                    float slot = baseRot + lockAngle;
                    o.TargetX = cx + MathF.Sin(slot * AiMath.Rad) * radius;
                    o.TargetY = cy + MathF.Cos(slot * AiMath.Rad) * radius;
                    float gap = AiMath.Distance(s.X, s.Y, o.TargetX, o.TargetY);
                    // Fast while far from its place, then as fast as the place moves (it trails it by a fraction of a unit).
                    o.TargetVel = Math.Min(top * Math.Max(0.1f, m.FarSpeed ?? Math.Max(speed, 0.6f)), gap * 4f);
                    o.Slide = true;
                    if (m.FaceForward)
                    {
                        // Along the circle, the way the summoner turns (kept while it stands still).
                        if (!float.IsNaN(lastSummonerRot))
                        {
                            float turned = AiMath.AngleDifference(lastSummonerRot, baseRot);
                            if (Math.Abs(turned) > 0.001f) summonerTurn = turned > 0f ? 1f : -1f;
                        }
                        lastSummonerRot = baseRot;
                        o.FaceRot = AiMath.NormalizeAngle(slot + 90f * summonerTurn);
                    }
                    else
                    {
                        o.FaceRot = AiMath.NormalizeAngle(slot + m.FaceOffset);
                    }
                }
                else if (hasCenter)
                {
                    float spin = m.Spin > 0 ? 1f : m.Spin < 0 ? -1f : defaultSpin;
                    float radius = Math.Max(2f, m.Distance);
                    float heading = AiMath.Heading(cx, cy, s.X, s.Y);
                    float a = (heading + spin * m.Deviation) * AiMath.Rad;
                    o.TargetX = cx + MathF.Sin(a) * radius;
                    o.TargetY = cy + MathF.Cos(a) * radius;
                    float far = m.FarSpeed ?? speed;
                    float fromCenter = around == "player" ? dist : AiMath.Distance(s.X, s.Y, cx, cy);
                    o.TargetVel = top * (fromCenter > radius * Math.Max(1f, m.FarFactor) ? far : speed);
                }
                else
                {
                    o.TargetX = forwardX;
                    o.TargetY = forwardY;
                    o.TargetVel = top * speed;
                }
                break;
            }

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
                AiPoint? p = points![pathIndex];
                float px = s.HomeX + (p?.X ?? 0f), py = s.HomeY + (p?.Y ?? 0f);
                float left = AiMath.Distance(s.X, s.Y, px, py);
                bool last = !m.Loop && pathIndex == count - 1;
                // Next point once it is close (the reach grows with the speed, so a fast unit does not circle the point).
                // A sliding unit reaches its point exactly, so a small reach keeps small paths (the Ambassador's triangle) in shape.
                float reach = o.Slide ? 0.3f : Math.Max(1.5f, top * speed * 0.35f);
                if (!last && left < reach)
                {
                    legsDone++;
                    pathIndex = (pathIndex + 1) % count;
                    p = points[pathIndex];
                    px = s.HomeX + (p?.X ?? 0f);
                    py = s.HomeY + (p?.Y ?? 0f);
                    left = AiMath.Distance(s.X, s.Y, px, py);
                    last = !m.Loop && pathIndex == count - 1;
                }
                if (last && left < 1f)
                {
                    // Arrived at the end: stays there, facing the player.
                    o.Hold = true;
                    o.TargetX = s.HasTarget ? s.TargetX : forwardX;
                    o.TargetY = s.HasTarget ? s.TargetY : forwardY;
                    o.TargetVel = 0f;
                    break;
                }
                o.TargetX = px;
                o.TargetY = py;
                // Slows down on the way into the last point.
                o.TargetVel = top * speed * (last ? Math.Clamp(left / 4f, 0.25f, 1f) : 1f);
                break;
            }

            case "zigzag":
            {
                // Straight dashes of "distance" units, each "deviation" degrees to one side of the line to the player and then to the
                // other (a hummingbird closing in). Close to the player (under 1.5 legs) the dashes turn wider (100 degrees), so it
                // weaves across instead of ramming. It faces each dash (slides with FaceRot).
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
                // The stop between two dashes: still, turning to face the player, for at least "pause" seconds and until it faces
                // them (at most a second more).
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
                    // Each dash a random angle around deviation (jitter), so its path cannot be read in advance.
                    float jitter = Math.Max(0f, m.Jitter);
                    float off = Math.Clamp(m.Deviation + (float)(rng.NextDouble() * 2.0 - 1.0) * jitter, 0f, 170f);
                    if (s.HasTarget && dist < legLength * 1.5f)
                    {
                        // Close: the zigzag goes around the player (along the circle, one dash a little in, the next a little out).
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
