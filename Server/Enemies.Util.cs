using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lidgren.Network;

// Text helpers and math used by the enemy code.
public static partial class Enemies
{
    // ================================================================== texts

    private static string Describe(Enemy enemy)
    {
        return enemy.Type.Name + "/" + enemy.Model.Name;
    }

    private static string ModelText(GameUnit u)
    {
        return u.Name + " health " + Num(u.MaxHp) + " vel " + Num(u.VelMax) + " turn " + Num(u.Turn) +
            " guns " + u.Guns.Count + " tier " + u.Tier + (u.Boss ? " [boss]" : "") + (u.IslandMin >= 0 ? " [island]" : "") +
            (u.Synthetic ? " [no catalog]" : "");
    }

    private static string StateText(Instance inst)
    {
        var sb = new StringBuilder();
        if (inst.Encounter == null) sb.Append("no encounter");
        else
        {
            sb.Append("encounter '").Append(inst.Encounter.Name).Append("', ");
            if (inst.Done) sb.Append("completed");
            else if (inst.WaveScheduled >= 0) sb.Append("wave ").Append(inst.WaveScheduled + 1).Append(" scheduled");
            else sb.Append("wave ").Append(inst.CurrentWave + 1).Append('/').Append(inst.Encounter.Waves.Count);
        }
        sb.Append(", ").Append(inst.Enemies.Count).Append(" enemy(ies) alive, ").Append(inst.Kills).Append(" kill(s)");
        sb.Append(", catalog with ").Append(catalog.Count).Append(" unit(s)");
        return sb.ToString();
    }

    private static string Num(float val)
    {
        return val.ToString("0.##", Inv);
    }

    // ================================================================== math

    private static string NormalizeBehavior(string? behavior)
    {
        string c = (behavior ?? "").Trim().ToLowerInvariant();
        return Behaviors.Contains(c) ? c : "chase";
    }

    // Heading from (x0, y0) to (x1, y1) in the game's convention: 0 = +Y, 90 = +X.
    private static float Heading(float x0, float y0, float x1, float y1)
    {
        return MathF.Atan2(x1 - x0, y1 - y0) / Rad;
    }

    private static float NormalizeAngle(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        if (a < -180f) a += 360f;
        return a;
    }

    private static float AngleDifference(float fromValue, float to)
    {
        return NormalizeAngle(to - fromValue);
    }

    private static float Distance(float x0, float y0, float x1, float y1)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    private static float Approach(float current, float target, float step)
    {
        if (current < target) return Math.Min(target, current + step);
        return Math.Max(target, current - step);
    }

    private static float Positive(float val)
    {
        return val > 0f ? val : 1f;
    }

    // The client's ScaledHelper.WriteScaled: variable integer of floor(value * scale).
    private static void WriteScaled(NetBuffer buf, float val, float scale)
    {
        buf.WriteVariableInt32((int)Math.Floor((double)val * scale));
    }

    private static float Real(string text)
    {
        return float.TryParse(text, NumberStyles.Float, Inv, out float v) && float.IsFinite(v) ? v : 0f;
    }

    private static int Int(string text, int fallback = 0)
    {
        return int.TryParse(text, NumberStyles.Integer, Inv, out int v) ? v : fallback;
    }

    private static short Short(string text)
    {
        return short.TryParse(text, NumberStyles.Integer, Inv, out short v) ? v : (short)0;
    }
}
