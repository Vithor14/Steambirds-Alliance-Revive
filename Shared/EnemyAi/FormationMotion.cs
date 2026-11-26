namespace SteambirdsAi;

// Where a formation piece stands, and what the links ask for this tick. The server and the simulator both call this,
// so a train, a ring and a shield link behave the same in the editor and in the game.
public static class FormationMotion
{
    public struct Pose
    {
        public float X, Y, Rot, Speed;
    }

    public static Pose Start(FormationSlot slot, float leaderX, float leaderY, float leaderRot, float spacing)
    {
        if (slot.Kind == "chain")
        {
            float behind = (slot.ChainIndex + 1) * Math.Max(0.4f, spacing);
            float rad = leaderRot * AiMath.Rad;
            return new Pose
            {
                X = leaderX - MathF.Sin(rad) * behind,
                Y = leaderY - MathF.Cos(rad) * behind,
                Rot = leaderRot,
                Speed = 0f
            };
        }
        if (slot.Kind == "ring")
        {
            return Ring(leaderX, leaderY, leaderRot, 0f, slot.Radius, slot.Angle, 0f, 0f);
        }
        if (slot.Kind == "free")
        {
            float rad = (leaderRot + 180f + slot.RingIndex * 25f) * AiMath.Rad;
            return new Pose { X = leaderX + MathF.Sin(rad) * 6f, Y = leaderY + MathF.Cos(rad) * 6f, Rot = leaderRot, Speed = 0f };
        }
        return Slot(leaderX, leaderY, leaderRot, 0f, slot.X, slot.Y);
    }

    public static Pose Slot(float leaderX, float leaderY, float leaderRot, float leaderSpeed, float x, float y)
    {
        float rad = leaderRot * AiMath.Rad;
        float fx = MathF.Sin(rad), fy = MathF.Cos(rad);
        float rx = MathF.Cos(rad), ry = -MathF.Sin(rad);
        return new Pose
        {
            X = leaderX + rx * x + fx * y,
            Y = leaderY + ry * x + fy * y,
            Rot = leaderRot,
            Speed = leaderSpeed
        };
    }

    // World-space ring around the leader. Spin is degrees per second. The speed includes the leader's own velocity so the
    // client, which extrapolates along the facing, does not leave the light behind.
    public static Pose Ring(float leaderX, float leaderY, float leaderRot, float leaderSpeed, float radius, float angleDeg, float spinDeg, float time)
    {
        float a = (angleDeg + spinDeg * time) * AiMath.Rad;
        float x = leaderX + MathF.Sin(a) * radius;
        float y = leaderY + MathF.Cos(a) * radius;
        float omega = spinDeg * AiMath.Rad;
        float lrad = leaderRot * AiMath.Rad;
        float vx = MathF.Sin(lrad) * leaderSpeed + MathF.Cos(a) * omega * radius;
        float vy = MathF.Cos(lrad) * leaderSpeed - MathF.Sin(a) * omega * radius;
        float speed = MathF.Sqrt(vx * vx + vy * vy);
        float rot = speed > 0.05f ? AiMath.Heading(0f, 0f, vx, vy) : AiMath.Heading(leaderX, leaderY, x, y);
        return new Pose { X = x, Y = y, Rot = rot, Speed = speed };
    }

    // Point "distance" units behind the leader along the path it flew (oldest sample first is the far end).
    public static Pose AlongTrail(float leaderX, float leaderY, float leaderRot, float leaderSpeed,
        IReadOnlyList<(float X, float Y)> trail, float distance)
    {
        float px = leaderX, py = leaderY;
        float remaining = distance;
        for (int i = trail.Count - 1; i >= 0; i--)
        {
            (float tx, float ty) = trail[i];
            float segment = AiMath.Distance(px, py, tx, ty);
            if (segment >= remaining && segment > 0.0001f)
            {
                float t = remaining / segment;
                float x = px + (tx - px) * t;
                float y = py + (ty - py) * t;
                float rot = AiMath.Distance(x, y, px, py) > 0.01f ? AiMath.Heading(x, y, px, py) : leaderRot;
                return new Pose { X = x, Y = y, Rot = rot, Speed = leaderSpeed };
            }
            remaining -= segment;
            px = tx;
            py = ty;
        }
        float rad = leaderRot * AiMath.Rad;
        return new Pose
        {
            X = px - MathF.Sin(rad) * remaining,
            Y = py - MathF.Cos(rad) * remaining,
            Rot = leaderRot,
            Speed = leaderSpeed
        };
    }
}

// One living (or just-dead) piece the link rules can see.
public sealed class FormationActor
{
    public string Id = "";
    public string Role = "";
    public int RoleOrder;
    public bool Alive = true;
    public bool IsLeader;
    public float HealthFraction = 1f;
    // Set by a previous "unshield": a shield link no longer covers this piece.
    public bool ShieldSuppressed;
}

// What to do to one piece this tick.
public sealed class FormationOrder
{
    public string ActorId = "";
    public bool Shield;
    public string Effect = "";
    public bool ClearShield;
    public bool Kill;
    public float HealFraction;
}

public static class FormationRules
{
    public static void Evaluate(FormationFile file, IReadOnlyList<FormationActor> actors, HashSet<string> seenAlive,
        HashSet<string> fired, List<FormationOrder> into)
    {
        foreach (FormationActor actor in actors)
            if (actor.Alive) seenAlive.Add(actor.Id);

        var shield = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (FormationLink link in file.Links ?? new List<FormationLink>())
        {
            if (link.Shield == null || link.Shield.Count == 0 || !Holds(link, actors)) continue;
            foreach (FormationActor actor in Targets(link.Shield, actors, null))
            {
                if (!actor.Alive || actor.ShieldSuppressed) continue;
                shield[actor.Id] = (link.Effect ?? "").Trim();
            }
        }
        foreach (KeyValuePair<string, string> pair in shield)
            into.Add(new FormationOrder { ActorId = pair.Key, Shield = true, Effect = pair.Value });

        int linkIndex = 0;
        foreach (FormationLink link in file.Links ?? new List<FormationLink>())
        {
            int index = linkIndex++;
            if (link.When == null || link.Do == null || link.Do.Kind.Length == 0) continue;
            FormationActor? cause = Cause(link.When, actors, seenAlive);
            if (cause == null) continue;
            string key = index + "|" + cause.Id;
            if (!fired.Add(key)) continue;
            Apply(link.Do, cause, actors, into);
        }
    }

    private static bool Holds(FormationLink link, IReadOnlyList<FormationActor> actors)
    {
        if (!string.IsNullOrWhiteSpace(link.WhileAlive))
        {
            FormationActor? who = Find(actors, link.WhileAlive!);
            return who != null && who.Alive;
        }
        if (link.WhileAnyAlive != null && link.WhileAnyAlive.Count > 0)
            return link.WhileAnyAlive.Any(id => { FormationActor? who = Find(actors, id); return who != null && who.Alive; });
        if (!string.IsNullOrWhiteSpace(link.WhileRole))
            return actors.Any(a => a.Alive && a.Role.Equals(link.WhileRole, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    private static FormationActor? Cause(FormationWhen when, IReadOnlyList<FormationActor> actors, HashSet<string> seenAlive)
    {
        if (!string.IsNullOrWhiteSpace(when.Died))
        {
            FormationActor? who = Find(actors, when.Died!);
            if (who != null && !who.Alive && seenAlive.Contains(who.Id)) return who;
            return null;
        }
        if (when.HealthBelow.HasValue)
        {
            float line = when.HealthBelow.Value > 1f ? when.HealthBelow.Value / 100f : when.HealthBelow.Value;
            IEnumerable<FormationActor> pool = actors.Where(a => a.Alive);
            if (!string.IsNullOrWhiteSpace(when.Member)) pool = pool.Where(a => a.Id.Equals(when.Member, StringComparison.OrdinalIgnoreCase));
            else if (!string.IsNullOrWhiteSpace(when.Role)) pool = pool.Where(a => a.Role.Equals(when.Role, StringComparison.OrdinalIgnoreCase));
            FormationActor? first = pool.OrderBy(a => a.RoleOrder).FirstOrDefault();
            if (first != null && first.HealthFraction <= line) return first;
        }
        return null;
    }

    private static void Apply(FormationDo action, FormationActor cause, IReadOnlyList<FormationActor> actors, List<FormationOrder> into)
    {
        string kind = action.Kind.Trim().ToLowerInvariant();
        if (kind == "killall")
        {
            foreach (FormationActor actor in actors)
            {
                if (!actor.Alive || actor.Id.Equals(cause.Id, StringComparison.OrdinalIgnoreCase)) continue;
                into.Add(new FormationOrder { ActorId = actor.Id, Kill = true, ClearShield = true });
            }
            return;
        }
        foreach (FormationActor actor in Targets(new[] { action.Target }, actors, cause))
        {
            if (kind == "unshield") into.Add(new FormationOrder { ActorId = actor.Id, ClearShield = true });
            else if (kind == "heal" && actor.Alive)
            {
                float amount = action.Amount <= 0f ? 0.25f : action.Amount > 1f ? action.Amount / 100f : action.Amount;
                into.Add(new FormationOrder { ActorId = actor.Id, HealFraction = amount });
            }
        }
    }

    // "leader", an id, "next" (needs the piece that triggered the link), or "role:<name>".
    private static IEnumerable<FormationActor> Targets(IEnumerable<string> names, IReadOnlyList<FormationActor> actors, FormationActor? cause)
    {
        foreach (string raw in names)
        {
            string name = (raw ?? "").Trim();
            if (name.Length == 0) continue;
            if (name.Equals("next", StringComparison.OrdinalIgnoreCase) && cause != null)
            {
                FormationActor? next = actors.Where(a => a.Alive && a.Role.Equals(cause.Role, StringComparison.OrdinalIgnoreCase) && a.RoleOrder > cause.RoleOrder)
                    .OrderBy(a => a.RoleOrder).FirstOrDefault();
                if (next != null) yield return next;
                continue;
            }
            if (name.StartsWith("role:", StringComparison.OrdinalIgnoreCase))
            {
                string role = name.Substring(5);
                foreach (FormationActor actor in actors.Where(a => a.Role.Equals(role, StringComparison.OrdinalIgnoreCase))) yield return actor;
                continue;
            }
            FormationActor? found = Find(actors, name);
            if (found != null) yield return found;
        }
    }

    private static FormationActor? Find(IReadOnlyList<FormationActor> actors, string id)
    {
        if (id.Equals("leader", StringComparison.OrdinalIgnoreCase)) return actors.FirstOrDefault(a => a.IsLeader);
        return actors.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }
}
