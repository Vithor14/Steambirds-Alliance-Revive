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

    // 1 clockwise, -1 counter-clockwise (when the move does not say).
    private readonly float defaultSpin;

    public AiRuntime(AiBrain brain, Random rng)
    {
        Brain = brain;
        this.rng = rng;
        defaultSpin = rng.Next(2) == 0 ? -1f : 1f;
        Out.State = brain.States[brain.Initial];
    }

    public AiBrain Brain { get; }
    public AiOutput Out { get; } = new AiOutput();
    public AiCompiledState State => Brain.States[stateIndex];
    public double StateTime(double now) => now - enteredAt;

    public AiOutput Step(in AiSense s)
    {
        AiOutput o = Out;
        o.Entered = false;
        o.PreviousState = null;

        if (!started)
        {
            started = true;
            Enter(Brain.Initial, s.Now);
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
            Enter(next, s.Now);
            o.Entered = true;
        }

        Compute(s, dist);
        return o;
    }

    private void Enter(int index, double now)
    {
        stateIndex = index;
        enteredAt = now;
        needPatrol = true;

        AiCompiledState state = Brain.States[index];
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

        float top = Math.Max(0f, s.VelMax);
        float speed = Math.Max(0f, m.Speed);
        float ahead = 8f;
        float forwardX = s.X + MathF.Sin(s.Rot * AiMath.Rad) * ahead;
        float forwardY = s.Y + MathF.Cos(s.Rot * AiMath.Rad) * ahead;

        switch (m.Mode)
        {
            case "orbit":
                if (s.HasTarget)
                {
                    float spin = m.Spin > 0 ? 1f : m.Spin < 0 ? -1f : defaultSpin;
                    float radius = Math.Max(2f, m.Distance);
                    float heading = AiMath.Heading(s.TargetX, s.TargetY, s.X, s.Y);
                    float a = (heading + spin * m.Deviation) * AiMath.Rad;
                    o.TargetX = s.TargetX + MathF.Sin(a) * radius;
                    o.TargetY = s.TargetY + MathF.Cos(a) * radius;
                    float far = m.FarSpeed ?? speed;
                    o.TargetVel = top * (dist > radius * Math.Max(1f, m.FarFactor) ? far : speed);
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
