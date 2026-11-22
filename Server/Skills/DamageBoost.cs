using System.Globalization;
using Lidgren.Network;

// Damage boost of the damage clouds and blasts (walker "Attack Area" = damageboostblast*, damageboost* clouds).
//
// In the original game the boost lives on the client: DamageCloudScript.applyAllyEffect calls
// UnitScript.receiveExternalDamageBoost(effect, bufftime) when the cloud touches an ally plane, the main gun damage
// is multiplied by "effect" for "bufftime" seconds (UnitScript.CalcVisibleDamageModifer) and the client tells the
// server with ServerReportDamageBoost (13949).
//
// On this server the client never applied it: the logs show no ServerReportDamageBoost and the hits after the
// blast keep the same damage (1.74 before and after). So the server keeps the boost itself:
//  - every damage cloud/blast created by Game becomes a zone (position, radius, life time);
//  - a player whose plane is inside a zone (same server role) gets the boost for "bufftime" seconds, refreshed
//    while he stays inside, like OnTriggerStay2D refreshes it on the client;
//  - ServerReportBulletHit damage is multiplied by the boost (Game.TratarAcerto);
//  - if the client does report ServerReportDamageBoost, its own damage numbers are already boosted and the server
//    stops multiplying until that client boost ends (no double boost).
// The owner also gets ClientCloudEffect(damageboost), which plays the "damageHit" effect on his plane.
public static class DamageBoost
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ClientCloudEffect(UnitScript.CloudType cloudType, float physicalDamage, EffectType elementType,
    // float elementalDamage, int shortId, Vector3 position, float bufftime).
    private const ushort MsgClientCloudEffect = 32571;
    private const byte CloudTypeDamageBoost = 2;   // UnitScript.CloudType.damageboost
    private const byte EffectTypeNone = 0;

    // false = do not send ClientCloudEffect (only the server-side boost).
    public static bool SendVisualEffect = true;

    // Extra distance accepted around a zone (plane collider radius and position lag).
    private const float ZoneMargin = 0.6f;

    private sealed class Zone
    {
        public string Role = "";
        public string CloudName = "";
        public float X;
        public float Y;
        public float Radius;
        public double Until;
        public float Factor;
        public float BuffTime;
        // Aura attached to its owner (assault "damageAura"): the zone moves with the owner's plane.
        public long FollowPeer;
    }

    private sealed class PlayerBoost
    {
        public float Factor = 1f;
        public double ServerUntil;
        public double ClientUntil;
        public int BoostedHits;
    }

    private static readonly object gate = new object();
    private static readonly List<Zone> zones = new List<Zone>();
    private static readonly Dictionary<long, PlayerBoost> players = new Dictionary<long, PlayerBoost>();

    // Effect and buff time of a cloud from the configBinary ("clouds"). Only damage clouds/blasts give a boost.
    public static bool TryGetCloud(string cloudName, out float factor, out float buffTime)
    {
        factor = 1f;
        buffTime = 0f;
        if (!ConfigBinary.Loaded || string.IsNullOrEmpty(cloudName)) return false;
        ConfigNode cloud = ConfigBinary.Get("clouds")[cloudName];
        if (cloud.IsMissing) return false;
        string prefab = cloud["cloudPrefab"].AsString("");
        if (prefab != "damageCloud" && prefab != "damageBlast" && prefab != "damageAura") return false;
        factor = cloud["effect"].AsFloat(1f);
        buffTime = cloud["bufftime"].AsFloat(0f);
        return factor > 1f && buffTime > 0f;
    }

    // Called when Game creates a cloud/blast. The owner is inside it right away.
    public static void CloudCreated(NetServer server, NetConnection ownerConnection, long ownerPeer, string role,
        int ownerShortId, string cloudName, float x, float y, float radius, float lifetime, double now,
        Action<string, string> log, bool followOwner = false)
    {
        if (!TryGetCloud(cloudName, out float factor, out float buffTime)) return;

        lock (gate)
        {
            zones.Add(new Zone
            {
                Role = role,
                CloudName = cloudName,
                X = x,
                Y = y,
                Radius = Math.Max(0.5f, radius),
                Until = now + Math.Max(0.1f, lifetime),
                Factor = factor,
                BuffTime = buffTime,
                FollowPeer = followOwner ? ownerPeer : 0
            });
            Grant(ownerPeer, factor, buffTime, now);
        }

        if (SendVisualEffect) SendCloudEffect(server, ownerConnection, ownerShortId, x, y, buffTime);

        log("SKILL", role + " damage bonus of '" + cloudName + "': x" + factor.ToString("0.##", Inv) + " for " +
            buffTime.ToString("0.#", Inv) + "s on allies within the radius " + radius.ToString("0.#", Inv) +
            " (applied by the server on shot hits)");
    }

    // ServerReportDamageBoost from the client: it applied the boost itself, so its damage numbers are boosted.
    public static void ClientReported(long peer, float factor, float clock, double now)
    {
        lock (gate)
        {
            PlayerBoost b = Get(peer);
            b.ClientUntil = Math.Max(b.ClientUntil, now + Math.Max(0f, clock));
        }
    }

    // Damage multiplier for one bullet hit of this player right now.
    public static float Multiplier(long peer, string role, bool hasPosition, float x, float y, double now)
    {
        lock (gate)
        {
            zones.RemoveAll(z => z.Until < now);
            if (hasPosition)
            {
                foreach (Zone z in zones)
                {
                    if (z.Role != role) continue;
                    if (z.FollowPeer != 0 && Game.GetPlane(z.FollowPeer, out float ax, out float ay, out _, out _))
                    {
                        z.X = ax;
                        z.Y = ay;
                    }
                    float dx = x - z.X;
                    float dy = y - z.Y;
                    float r = z.Radius + ZoneMargin;
                    if (dx * dx + dy * dy <= r * r) Grant(peer, z.Factor, z.BuffTime, now);
                }
            }

            if (!players.TryGetValue(peer, out PlayerBoost? b)) return 1f;
            if (b.ClientUntil > now) return 1f;      // client already multiplied
            if (b.ServerUntil <= now) return 1f;
            b.BoostedHits++;
            return b.Factor;
        }
    }

    // Number of hits boosted by the server so far (for the log summary).
    public static int BoostedHits(long peer)
    {
        lock (gate)
        {
            return players.TryGetValue(peer, out PlayerBoost? b) ? b.BoostedHits : 0;
        }
    }

    public static void Forget(long peer)
    {
        lock (gate)
        {
            players.Remove(peer);
        }
    }

    private static void Grant(long peer, float factor, float buffTime, double now)
    {
        PlayerBoost b = Get(peer);
        double until = now + buffTime;
        // TimedFadeEffect.Set: a stronger boost replaces a weaker one; the same strength just refreshes the time.
        if (b.ServerUntil <= now || factor >= b.Factor)
        {
            b.Factor = factor;
            b.ServerUntil = Math.Max(b.ServerUntil <= now ? 0 : b.ServerUntil, until);
        }
    }

    private static PlayerBoost Get(long peer)
    {
        if (!players.TryGetValue(peer, out PlayerBoost? b))
        {
            b = new PlayerBoost();
            players[peer] = b;
        }
        return b;
    }

    // ClientCloudEffect: the client spawns the "damageHit" effect at the position (GameClient.ClientCloudEffect).
    private static void SendCloudEffect(NetServer server, NetConnection connection, int shortId, float x, float y,
        float buffTime)
    {
        if (connection.Status != NetConnectionStatus.Connected) return;
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgClientCloudEffect);
        m.Write(CloudTypeDamageBoost);   // UnitScript.CloudType (one byte)
        m.Write(0f);                     // physicalDamage
        m.Write(EffectTypeNone);         // EffectType (one byte)
        m.Write(0f);                     // elementalDamage
        m.Write(shortId);                // int shortId
        m.Write(x);                      // Vector3 position
        m.Write(y);
        m.Write(0f);
        m.Write(buffTime);               // bufftime
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }
}
