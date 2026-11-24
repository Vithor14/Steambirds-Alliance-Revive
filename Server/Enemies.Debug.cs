using System.Globalization;
using System.Text;
using Lidgren.Network;

// Enemies (part): what the admin messages of the client ask about units: ServerWatchAI (text of the AI of one unit,
// shown by the client as ClientReceiveAIDebug) and ServerQuery (the client got updates of units it never saw spawn).
public static partial class Enemies
{
    private static Instance? InstanceOf(NetConnection connection)
    {
        if (instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst) && inst.Members.Contains(connection)) return inst;
        return instances.Values.FirstOrDefault(i => i.Members.Contains(connection));
    }

    // One line about the unit the player watches (null = the server has no such enemy).
    public static string? DebugText(NetConnection connection, int shortId)
    {
        lock (Game.Lock)
        {
            Instance? inst = InstanceOf(connection);
            if (inst == null || !inst.Enemies.TryGetValue(shortId, out Enemy? e)) return null;
            CultureInfo c = CultureInfo.InvariantCulture;
            var text = new StringBuilder();
            text.Append(e.TypeKey).Append(" #").Append(shortId).Append('\n');
            text.Append("hp ").Append(e.Health.ToString("0", c)).Append('/').Append(e.MaxHp.ToString("0", c));
            text.Append("  pos ").Append(e.X.ToString("0", c)).Append(',').Append(e.Y.ToString("0", c));
            text.Append("  rot ").Append(e.Rot.ToString("0", c)).Append("  vel ").Append(e.Vel.ToString("0.0", c)).Append('\n');
            text.Append("behavior ").Append(e.Behavior);
            if (e.Ai != null) text.Append("  ai ").Append(e.Ai.Brain.Id).Append(" / ").Append(e.Ai.State.Id);
            text.Append('\n');
            text.Append(e.Aggro ? "aggro" : "calm");
            if (e.Asleep) text.Append(", asleep");
            if (e.Invulnerable) text.Append(", invulnerable");
            if (e.Group.Length > 0) text.Append("  group ").Append(e.Group);
            text.Append("  guns ").Append(e.Guns.Count).Append("  shots ").Append(e.Shots);
            return text.ToString();
        }
    }

    // ServerQuery: tells the player about the enemies they asked for (spawn again). Returns how many were sent.
    public static int Resend(NetConnection connection, IEnumerable<int> shortIds, double now)
    {
        lock (Game.Lock)
        {
            Instance? inst = InstanceOf(connection);
            if (inst == null) return 0;
            var found = new List<Enemy>();
            foreach (int id in shortIds)
                if (inst.Enemies.TryGetValue(id, out Enemy? e)) found.Add(e);
            if (found.Count == 0) return 0;
            if (inst.Filtering)
            {
                HashSet<int> known = KnownOf(inst, connection.RemoteUniqueIdentifier);
                foreach (Enemy e in found) known.Add(e.ShortId);
            }
            SendSpawnBatches(inst, found, now, connection);
            return found.Count;
        }
    }

    // Short id of the enemy closest to the point (0 = none).
    public static int NearestId(NetConnection connection, float x, float y)
    {
        lock (Game.Lock)
        {
            Instance? inst = InstanceOf(connection);
            if (inst == null) return 0;
            int best = 0;
            float bestDistance = float.MaxValue;
            foreach (Enemy e in inst.Enemies.Values)
            {
                float d = (e.X - x) * (e.X - x) + (e.Y - y) * (e.Y - y);
                if (d < bestDistance) { bestDistance = d; best = e.ShortId; }
            }
            return best;
        }
    }
}
