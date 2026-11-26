namespace SteambirdsAi;

// Math of the enemy AI, in the game's convention: angle 0 = +Y, 90 = +X (clockwise), direction = (sin r, cos r).
// This folder is compiled into the server AND the launcher (the AI tab simulator), so what you see in the simulator
// is the code the server runs.
public static class AiMath
{
    public const float Rad = MathF.PI / 180f;

    public static float Heading(float x0, float y0, float x1, float y1)
    {
        return MathF.Atan2(x1 - x0, y1 - y0) / Rad;
    }

    public static float NormalizeAngle(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        if (a < -180f) a += 360f;
        return a;
    }

    public static float AngleDifference(float from, float to)
    {
        return NormalizeAngle(to - from);
    }

    public static float Distance(float x0, float y0, float x1, float y1)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    public static float Approach(float current, float target, float step)
    {
        if (current < target) return Math.Min(target, current + step);
        return Math.Max(target, current - step);
    }
}

// Plane flight: turns toward the destination at most maxTurn degrees per second and speeds up or slows down toward the
// wanted speed. Same rule the server used for its built-in behaviors.
public static class AiMotion
{
    public static void Steer(ref float x, ref float y, ref float rot, ref float vel, float targetX, float targetY, float targetVel,
        float velMin, float velMax, float maxTurn, bool hold, float dt)
    {
        float desired = AiMath.Heading(x, y, targetX, targetY);
        float delta = AiMath.AngleDifference(rot, desired);
        float turn = maxTurn * dt;
        rot = AiMath.NormalizeAngle(rot + Math.Clamp(delta, -turn, turn));

        // A sharp turn slows the plane down.
        if (!hold && Math.Abs(delta) > 100f) targetVel = Math.Max(velMin, targetVel * 0.7f);
        float accel = Math.Max(3f, velMax * 1.5f) * dt;
        vel = AiMath.Approach(vel, targetVel, accel);

        if (vel > 0f)
        {
            x += MathF.Sin(rot * AiMath.Rad) * vel * dt;
            y += MathF.Cos(rot * AiMath.Rad) * vel * dt;
        }
    }
}

// Where one gun points for its next shot (the game's FireMode patterns). Same logic as the client's GunMountInfo.
public static class AiGunAim
{
    // "relative" = angle from the unit's front to the player. Returns false when the player is outside the arc.
    // "rotation" is the direction of the shot relative to the unit's front.
    //
    // "anyDirection" (see IsAnyDirection): the mount shoots all around, turning a little between shots, like a sprinkler.
    public static bool TryAim(int mode, float center, float arc, int burst, int ammo, ref float turn, bool freeAim, float relative,
        double now, out float rotation, bool anyDirection = false)
    {
        float halfArc = arc > 0.5f ? arc * 0.5f : 30f;
        float deviation = AiMath.AngleDifference(center, relative);
        if (anyDirection) mode = 5;

        switch (mode)
        {
            case 5: // Sprinkler: spiral, no aiming
            case 6: // SprinklerTurnback
                turn = AiMath.NormalizeAngle(turn + 23f);
                rotation = center + turn;
                return true;

            case 3: // Hose: sweeps the arc from side to side
                rotation = center + halfArc * MathF.Sin((float)(now * 3.0));
                return Math.Abs(deviation) <= halfArc + 30f;

            case 1: // ArcRight: crosses the arc during the charge
            case 2: // ArcLeft
            {
                int shots = Math.Max(1, burst);
                float progress = shots <= 1 ? 0.5f : (float)(shots - ammo) / (shots - 1);
                float direction = mode == 1 ? 1f : -1f;
                rotation = center - direction * halfArc + direction * 2f * halfArc * progress;
                return Math.Abs(deviation) <= halfArc + 20f;
            }

            case 7: // JustShootDammit: shoots where the gun points
                rotation = center;
                // 360-degree arc (bullet rings): shoots in any direction, without needing to face it.
                return arc >= 359f || Math.Abs(deviation) <= 60f;
        }

        // Aimed
        if (freeAim || arc >= 359f)
        {
            rotation = relative;
            return true;
        }
        if (arc > 0.5f)
        {
            if (Math.Abs(deviation) > halfArc + 5f)
            {
                rotation = 0f;
                return false;
            }
            rotation = center + Math.Clamp(deviation, -halfArc, halfArc);
            return true;
        }

        // No arc: a gun fixed forward, with a small correction.
        if (Math.Abs(deviation) > 35f)
        {
            rotation = 0f;
            return false;
        }
        rotation = center + Math.Clamp(deviation, -8f, 8f);
        return true;
    }

    // The guns the game names "anydir" and files as JustShootDammit with a 360 degree arc (the Five Star, the Solaris planets and rings,
    // the Meowza mounts) shoot in every direction: on the original server the unit told the mount where to point, and no build has that
    // code. Reconstruction: they turn between shots like a sprinkler. Guns of the same mode with other names keep shooting where they point.
    public static bool IsAnyDirection(int mode, float arc, string name)
    {
        return mode == 7 && arc >= 359f && name.StartsWith("anydir", StringComparison.OrdinalIgnoreCase);
    }

    // How far, to each side of the direction the mount points, the player can be for the mount to fire (the same limits as TryAim).
    // 180 or more = all around.
    public static float FiringHalfWidth(int mode, float arc, bool freeAim)
    {
        float halfArc = arc > 0.5f ? arc * 0.5f : 30f;
        switch (mode)
        {
            case 5:
            case 6:
                return 180f;
            case 3:
                return halfArc + 30f;
            case 1:
            case 2:
                return halfArc + 20f;
            case 7:
                return arc >= 359f ? 180f : 60f;
        }
        if (freeAim || arc >= 359f) return 180f;
        if (arc > 0.5f) return halfArc + 5f;
        return 35f;
    }

    // Name and one-line explanation of a FireMode of the game (the numbers of units_config.txt).
    public static string ModeName(int mode) => mode switch
    {
        0 => "Aimed",
        1 => "ArcRight",
        2 => "ArcLeft",
        3 => "Hose",
        5 => "Sprinkler",
        6 => "SprinklerTurnback",
        7 => "JustShootDammit",
        _ => "Mode " + mode
    };

    public static string ModeHelp(int mode) => mode switch
    {
        0 => "turns toward the player inside its firing arc",
        1 => "sweeps the arc from left to right during each burst",
        2 => "sweeps the arc from right to left during each burst",
        3 => "sweeps the arc back and forth like a hose",
        5 => "spirals around without aiming",
        6 => "spirals around without aiming and turns back",
        7 => "shoots where the mount points, aiming or not",
        _ => "unknown pattern"
    };

    // How far a bullet of this gun goes.
    public static float Range(float speed, float duration, float range)
    {
        float flight = speed * duration;
        float result = range > 0.1f ? range : flight;
        if (flight > 0.1f) result = Math.Min(result, flight * 1.1f);
        return result > 0.5f ? result : 18f;
    }
}
