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

    // "point:id" uses this formation. "point:formation/id" only matches when the file id is that formation.
    // x is to the right of the leader and y is in front. False when the text is not a point place.
    public static bool TryPointOffset(string place, FormationFile? file, out float x, out float y)
    {
        x = 0f;
        y = 0f;
        if (file == null || place.Length < 7 || !place.StartsWith("point:", StringComparison.OrdinalIgnoreCase)) return false;
        string rest = place.Substring(6);
        int slash = rest.IndexOf('/');
        if (slash >= 0)
        {
            if (!file.Id.Equals(rest.Substring(0, slash), StringComparison.OrdinalIgnoreCase)) return false;
            rest = rest.Substring(slash + 1);
        }
        FormationPoint? point = file.Points?.FirstOrDefault(p => p != null && p.Id.Equals(rest, StringComparison.OrdinalIgnoreCase));
        if (point == null) return false;
        x = point.X;
        y = point.Y;
        return true;
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

    // One place for every kind. points walks the formation's points (a pentagon is patrol: straight to the next vertex,
    // wait and turn, then the next). local points are x right and y in front of the leader.
    public static Pose Place(FormationSlot slot, float leaderX, float leaderY, float leaderRot, float leaderSpeed,
        float spacing, IReadOnlyList<(float X, float Y)> trail, IReadOnlyList<FormationPoint>? points, float time)
    {
        if (slot.Kind == "chain")
        {
            float behind = (slot.ChainIndex + 1) * Math.Max(0.4f, spacing);
            return AlongTrail(leaderX, leaderY, leaderRot, leaderSpeed, trail, behind);
        }
        if (slot.Kind == "ring")
            return Ring(leaderX, leaderY, leaderRot, leaderSpeed, slot.Radius, slot.Angle, slot.Spin, time);
        if (slot.Kind == "points")
            return OnPoints(leaderX, leaderY, slot.FaceLocked ? leaderRot : 0f, slot, points, time);
        if (slot.Kind == "free")
            return Start(slot, leaderX, leaderY, leaderRot, spacing);
        // slot and follow share the offset. follow only uses it as a destination.
        return Slot(leaderX, leaderY, leaderRot, leaderSpeed, slot.X, slot.Y);
    }

    public static Pose OnPoints(float leaderX, float leaderY, float leaderRot, FormationSlot slot,
        IReadOnlyList<FormationPoint>? points, float time)
    {
        var local = new List<(float X, float Y)>();
        if (points != null)
            foreach (FormationPoint point in points)
                if (point != null) local.Add((point.X, point.Y));
        if (local.Count == 0) return Slot(leaderX, leaderY, leaderRot, 0f, 0f, 0f);

        var world = new List<(float X, float Y)>(local.Count);
        foreach ((float x, float y) in local)
        {
            Pose at = Slot(leaderX, leaderY, leaderRot, 0f, x, y);
            world.Add((at.X, at.Y));
        }
        int n = world.Count;
        int index = PointIndex(slot, points, n);
        string motion = slot.Motion.Length == 0 ? "patrol" : slot.Motion;
        float speed = slot.TravelSpeed > 0.05f ? slot.TravelSpeed : 4f;
        if (motion == "fixed") return Still(world[index], Heading(leaderX, leaderY, world[index].X, world[index].Y));
        if (motion == "toward") return Toward(leaderX, leaderY, world[index].X, world[index].Y, speed, time);
        if (motion == "orbit")
        {
            (float cx, float cy) = world[index];
            float radius = slot.OrbitRadius > 0.2f ? slot.OrbitRadius : 2f;
            float spin = Math.Abs(slot.Spin) < 0.01f ? 40f : slot.Spin;
            return Ring(cx, cy, 0f, 0f, radius, slot.RingIndex * (360f / n), spin, time);
        }
        return Patrol(world, index, speed, Math.Max(0f, slot.Pause), slot.Spin, time);
    }

    // Leader-local offset of a world point (x right, y in front).
    public static void ToLocal(float leaderX, float leaderY, float leaderRot, float worldX, float worldY, out float localX, out float localY)
    {
        float rad = leaderRot * AiMath.Rad;
        float dx = worldX - leaderX;
        float dy = worldY - leaderY;
        float rx = MathF.Cos(rad), ry = -MathF.Sin(rad);
        float fx = MathF.Sin(rad), fy = MathF.Cos(rad);
        localX = dx * rx + dy * ry;
        localY = dx * fx + dy * fy;
    }

    private static int PointIndex(FormationSlot slot, IReadOnlyList<FormationPoint>? points, int count)
    {
        if (slot.Point.Length > 0 && points != null)
        {
            for (int i = 0; i < points.Count; i++)
                if (points[i] != null && slot.Point.Equals(points[i].Id ?? "", StringComparison.OrdinalIgnoreCase))
                    return i % count;
        }
        int index = slot.RingIndex;
        if (index < 0) index = 0;
        return index % count;
    }

    private static Pose Toward(float fromX, float fromY, float toX, float toY, float speed, float time)
    {
        float dist = AiMath.Distance(fromX, fromY, toX, toY);
        float travel = speed * Math.Max(0f, time);
        if (dist < 0.05f || travel >= dist) return Still((toX, toY), Heading(fromX, fromY, toX, toY));
        float t = travel / dist;
        float x = fromX + (toX - fromX) * t;
        float y = fromY + (toY - fromY) * t;
        return new Pose { X = x, Y = y, Rot = Heading(fromX, fromY, toX, toY), Speed = speed };
    }

    private static Pose Patrol(List<(float X, float Y)> world, int start, float speed, float pause, float spin, float time)
    {
        int n = world.Count;
        if (n == 1) return Still(world[0], 0f);
        float cycle = 0f;
        var edges = new List<(int From, float Length, float Travel)>(n);
        for (int i = 0; i < n; i++)
        {
            int from = (start + i) % n;
            int to = (from + 1) % n;
            float length = AiMath.Distance(world[from].X, world[from].Y, world[to].X, world[to].Y);
            float travel = length / speed;
            edges.Add((from, length, travel));
            cycle += travel + pause;
        }
        if (cycle < 0.001f) return Still(world[start], 0f);
        float t = time % cycle;
        if (t < 0f) t += cycle;
        foreach ((int from, float length, float travel) in edges)
        {
            int to = (from + 1) % n;
            float face = Heading(world[from].X, world[from].Y, world[to].X, world[to].Y);
            if (t <= travel)
            {
                float u = travel <= 0.0001f ? 1f : t / travel;
                float x = world[from].X + (world[to].X - world[from].X) * u;
                float y = world[from].Y + (world[to].Y - world[from].Y) * u;
                return new Pose { X = x, Y = y, Rot = face, Speed = length <= 0.05f ? 0f : speed };
            }
            t -= travel;
            if (t <= pause)
            {
                float turned = Math.Abs(spin) < 0.01f ? 90f : spin;
                return new Pose
                {
                    X = world[to].X,
                    Y = world[to].Y,
                    Rot = AiMath.NormalizeAngle(face + turned * t),
                    Speed = 0f
                };
            }
            t -= pause;
        }
        return Still(world[start], 0f);
    }

    // Flies toward a slot offset with the unit's own turn. The position is never copied from the target, so a leader
    // that snaps 90 degrees leaves the piece to catch the new point instead of appearing there.
    // The goal is a disk around the slot (Area), not the coordinate. Inside it the piece rides with the leader and
    // does not chase the center. Outside, its velocity is the leader's plus a closing speed that falls to zero at
    // the edge of the disk. If it has passed a nearby slot, it may stop and turn; steering on past the point at the
    // unit's minimum speed is what makes a pod loop all the way around to get behind the leader again.
    public const float Area = 1.25f;

    public static void Follow(ref float x, ref float y, ref float rot, ref float vel, Pose target,
        float leaderSpeed, float velMin, float velMax, float maxTurn, float dt)
    {
        velMax = Math.Max(1f, velMax);
        // A follower may stop. The unit's minimum speed would keep it carving a circle around a point it already passed.
        velMin = 0f;
        maxTurn = Math.Max(40f, maxTurn);
        leaderSpeed = Math.Max(0f, leaderSpeed);

        float dist = AiMath.Distance(x, y, target.X, target.Y);
        float lrad = target.Rot * AiMath.Rad;
        float lvx = MathF.Sin(lrad) * leaderSpeed;
        float lvy = MathF.Cos(lrad) * leaderSpeed;
        float dx = target.X - x;
        float dy = target.Y - y;
        float gap = dist - Area;
        // Closing speed dies at the edge of the area, so the piece arrives instead of flying through the coordinate.
        float close = gap <= 0f ? 0f : Math.Min(velMax, gap * 1.8f);
        float vx = lvx;
        float vy = lvy;
        if (dist > 0.05f && close > 0f)
        {
            vx += dx / dist * close;
            vy += dy / dist * close;
        }
        float want = MathF.Sqrt(vx * vx + vy * vy);
        if (want > velMax && want > 0.001f)
        {
            float scale = velMax / want;
            vx *= scale;
            vy *= scale;
            want = velMax;
        }

        float aim = want < 0.05f ? target.Rot : AiMath.Heading(0f, 0f, vx, vy);
        // Passed it: the slot is behind the nose and still nearby. Stop and rotate. A turn at speed is the full lap.
        if (gap > 0f && dist < Area + 4f && dist > 0.05f)
        {
            float slotDelta = Math.Abs(AiMath.AngleDifference(rot, AiMath.Heading(x, y, target.X, target.Y)));
            if (slotDelta > 100f)
            {
                want = 0f;
                aim = AiMath.Heading(x, y, target.X, target.Y);
            }
        }
        else if (gap <= 0f)
        {
            // In the area. Match the leader. Still pointed well off its heading: stop and turn first, so the
            // leftover speed does not carry the piece out the far side.
            float face = Math.Abs(AiMath.AngleDifference(rot, target.Rot));
            want = face > 55f ? 0f : leaderSpeed;
            aim = target.Rot;
        }

        float ar = aim * AiMath.Rad;
        float aimX = x + MathF.Sin(ar) * 6f;
        float aimY = y + MathF.Cos(ar) * 6f;
        AiMotion.Steer(ref x, ref y, ref rot, ref vel, aimX, aimY, want, velMin, velMax, maxTurn, want < 0.05f, dt);
    }

    // Moves toward the place the formation computed, at the piece's own speed. A rigid piece is placed on the point
    // instead; this is for a point that is only a destination (the Five Star walking its pentagon).
    public static void StepToward(ref float x, ref float y, ref float rot, ref float vel, Pose target, float speed, float dt)
    {
        speed = Math.Max(0.2f, speed);
        float dist = AiMath.Distance(x, y, target.X, target.Y);
        float step = speed * Math.Max(0f, dt);
        if (dist <= Math.Max(0.2f, step))
        {
            x = target.X;
            y = target.Y;
            rot = target.Rot;
            vel = target.Speed;
            return;
        }
        float heading = AiMath.Heading(x, y, target.X, target.Y);
        float rad = heading * AiMath.Rad;
        x += MathF.Sin(rad) * step;
        y += MathF.Cos(rad) * step;
        rot = heading;
        vel = speed;
    }

    private static Pose Still((float X, float Y) at, float rot) => new Pose { X = at.X, Y = at.Y, Rot = rot, Speed = 0f };

    private static float Heading(float x, float y, float tx, float ty) =>
        AiMath.Distance(x, y, tx, ty) > 0.01f ? AiMath.Heading(x, y, tx, ty) : 0f;
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
