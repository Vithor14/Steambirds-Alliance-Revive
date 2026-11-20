using Lidgren.Network;

// Damage of the poison clouds (Old Faithful "poison", tank "poison blast") to the enemies of the server.
//
// In the original game the cloud (PoisonCloudScript) damages the units that touch it: applyEnemyEffect takes
// "damageMultiplier * effect * deltaTime" health per frame, but only on the instance that is authoritative for the
// touched unit (CloudScript.Colliding -> isCollisionAuthoritativeWithMoveAuthoritative). The enemies of this server are
// simulated by Enemies.cs, not by the client, so the cloud created for the client never reached them and the skill did
// nothing. Here the server does it: every poison cloud becomes a zone and, a few times per second, the enemies inside
// it lose "effect" health per second (effect = "clouds" > name > effect of the configBinary).
//
// Reconstruction: the damage multiplier of the owner's plane (damageMultiplier of the original cloud) is not known on
// the server, so DamageMultiplier is a plain number (1 = the damage written in the cloud config). The host can change it.
public static class CloudDamage
{
    // Multiplies the damage per second of every poison cloud (1 = as in the game data).
    public static float DamageMultiplier = 1f;

    // How often the zones hurt the enemies (seconds).
    private const double TickInterval = 0.2;

    private sealed class Zone
    {
        public NetConnection Owner = null!;
        public string CloudName = "";
        public float X;
        public float Y;
        public float Radius;
        public double Until;
        public double LastTick;
        public float DamagePerSecond;
    }

    private static readonly object gate = new object();
    private static readonly List<Zone> zones = new List<Zone>();

    // Damage per second of a cloud from the configBinary. False if the cloud does not hurt enemies (heal, damage boost...).
    public static bool TryGetCloud(string cloudName, out float damagePerSecond)
    {
        damagePerSecond = 0f;
        if (!ConfigBinary.Loaded || string.IsNullOrEmpty(cloudName)) return false;
        ConfigNode cloud = ConfigBinary.Get("clouds")[cloudName];
        if (cloud.IsMissing) return false;
        string prefab = cloud["cloudPrefab"].AsString("");
        if (!prefab.Contains("poison", StringComparison.OrdinalIgnoreCase)) return false;
        damagePerSecond = cloud["effect"].AsFloat(0f);
        return damagePerSecond > 0f;
    }

    // Called by Game when it creates a cloud. The owner is the connection that used the skill: the enemies of its
    // instance (the shared dungeon instance, with several players) are the ones that take the damage.
    public static void CloudCreated(NetConnection owner, string cloudName, float x, float y, float radius, float lifetime,
        double now, Action<string, string> log)
    {
        if (!TryGetCloud(cloudName, out float perSecond)) return;

        lock (gate)
        {
            zones.Add(new Zone
            {
                Owner = owner,
                CloudName = cloudName,
                X = x,
                Y = y,
                Radius = Math.Max(0.5f, radius),
                Until = now + Math.Max(0.1f, lifetime),
                LastTick = now,
                DamagePerSecond = perSecond * DamageMultiplier
            });
        }

        log("SKILL", "poison '" + cloudName + "' hurts the enemies inside the radius " + radius.ToString("0.#",
            System.Globalization.CultureInfo.InvariantCulture) + ": " + (perSecond * DamageMultiplier).ToString("0.##",
            System.Globalization.CultureInfo.InvariantCulture) + " health per second (applied by the server)");
    }

    // Called by Enemies.Update (several times per second). Applies the damage of the time since the last tick of each zone.
    public static void Update(double now, Action<string, string> log)
    {
        List<(NetConnection Owner, float X, float Y, float Radius, float Damage)> due = new List<(NetConnection, float, float, float, float)>();
        lock (gate)
        {
            for (int i = zones.Count - 1; i >= 0; i--)
            {
                Zone z = zones[i];
                if (z.Owner.Status != NetConnectionStatus.Connected)
                {
                    zones.RemoveAt(i);
                    continue;
                }

                double until = Math.Min(now, z.Until);
                double elapsed = until - z.LastTick;
                if (elapsed >= TickInterval || (z.Until <= now && elapsed > 0.0))
                {
                    z.LastTick = until;
                    due.Add((z.Owner, z.X, z.Y, z.Radius, (float)(z.DamagePerSecond * elapsed)));
                }
                if (z.Until <= now) zones.RemoveAt(i);
            }
        }

        // Outside the lock of this class: Enemies takes its own lock (Game.Lock) to look at and hurt the enemies.
        foreach (var hit in due)
            Enemies.DamageArea(hit.Owner, hit.X, hit.Y, hit.Radius, hit.Damage, now, log);
    }

    public static void Forget(NetConnection connection)
    {
        lock (gate)
        {
            zones.RemoveAll(z => z.Owner == connection);
        }
    }
}
