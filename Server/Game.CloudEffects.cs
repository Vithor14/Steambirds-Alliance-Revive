using System.Globalization;
using Lidgren.Network;

// Effects of the skill clouds on the players' planes, applied by the server.
//
// In the original game the cloud collided on the server, which healed with IncrementPlayerStats and set the shield and
// resist timers of the plane (UnitScript.healDamageOnOwner, receiveExternalShielded...). On this server the cloud only
// exists for the clients as a submissive copy, and the logs never showed the clients applying its effect. So here every
// heal, shield and resist cloud becomes a zone and the server does what the original did:
//  - healCloud / healAura (medic "Heal", paladin aura): every tick the planes inside get effect x seconds of health with
//    IncrementPlayerStats (the same message the original used). If the client heals itself and reports it
//    (ServerReportCloudHeal), the server stops healing that player for a moment (no double heal).
//  - shieldBlast (dagger knight) / damageResistAura (shieldmaiden): the plane takes damage / effect while inside
//    and for bufftime seconds after. The client decides damage by itself, so the server gives back the part that the
//    shield would have blocked: when the health fraction drops, it heals (1 - 1 / effect) of the drop.
//    Reconstruction: the plane's max health is not known on the server; it is the base health of the class plane in the
//    unit catalog (ShieldHealthScale multiplies it, host setting).
//  - damageAura (assault) goes to Skills\DamageBoost.cs together with the other damage clouds.
public static partial class Game
{
    // Multiplies the estimated max health of a plane when the server gives back shielded damage.
    public static float ShieldHealthScale = 1f;

    private const int ZoneHeal = 1;
    private const int ZoneShield = 2;
    private const double EffectTickSeconds = 0.2;
    private const float EffectZoneMargin = 0.6f;
    private const double ClientHealGrace = 1.0;
    private const double ShieldIgnoreAfterGiveBack = 0.35;

    private sealed class EffectZone
    {
        public string Role = "";
        public string CloudName = "";
        public int Kind;
        public long OwnerPeer;
        public bool Follow;
        public float X;
        public float Y;
        public float Radius;
        public double Until;
        public double LastTick;
        public float Effect;
        public float BuffTime;
        public bool Capped;
    }

    private sealed class ShieldState
    {
        public float Factor = 1f;
        public double Until;
        public float Baseline;
        public bool HasBaseline;
        public double IgnoreUntil;
        public double LastLog = -10;
    }

    private static readonly List<EffectZone> effectZones = new List<EffectZone>();
    private static readonly Dictionary<long, ShieldState> shields = new Dictionary<long, ShieldState>();
    private static double nextEffectTick;

    // Called by CreateCloud for every cloud. Heal and shield clouds become zones.
    private static void CloudEffectCreated(Player owner, Cloud cloud, string name, float x, float y, float duration, double now,
        Action<string, string> log)
    {
        int kind;
        if (cloud.Prefab == "healCloud" || cloud.Prefab == "healAura") kind = ZoneHeal;
        else if (cloud.Prefab == "shieldBlast" || cloud.Prefab == "damageResistAura") kind = ZoneShield;
        else return;

        if (!ConfigBinary.Loaded) return;
        ConfigNode node = ConfigBinary.Get("clouds")[name];
        if (node.IsMissing) return;
        float effect = node["effect"].AsFloat(0f);
        float buffTime = node["bufftime"].AsFloat(0f);
        if (effect <= 0f) return;
        if (kind == ZoneShield && (effect <= 1f || buffTime <= 0f)) return;

        effectZones.Add(new EffectZone
        {
            Role = owner.Role,
            CloudName = name,
            Kind = kind,
            OwnerPeer = owner.Peer,
            Follow = cloud.AttachedToOwner,
            X = x,
            Y = y,
            Radius = Math.Max(0.5f, cloud.FinalRadius),
            Until = now + Math.Max(0.1f, duration),
            LastTick = now,
            Effect = effect,
            BuffTime = buffTime,
            Capped = node["isCappedEffect"].AsBool(false)
        });

        log("SKILL", owner.Role + " '" + name + "' (" + cloud.Prefab + ") " + (kind == ZoneHeal
            ? "heals " + effect.ToString("0.##", CultureInfo.InvariantCulture) + " health per second"
            : "shield x" + effect.ToString("0.##", CultureInfo.InvariantCulture) + " for " +
              buffTime.ToString("0.#", CultureInfo.InvariantCulture) + "s") +
            " to the planes inside the radius " + cloud.FinalRadius.ToString("0.#", CultureInfo.InvariantCulture) +
            " (applied by the server)");
    }

    // Called from Handle on every message: only works every EffectTickSeconds. Only the zones of this server's role.
    private static void TickCloudEffects(NetServer server, string roleName, double now, Action<string, string> log)
    {
        if (effectZones.Count == 0 && shields.Count == 0) return;
        if (now < nextEffectTick) return;
        nextEffectTick = now + EffectTickSeconds;

        string role = "[" + roleName + "]";

        for (int i = effectZones.Count - 1; i >= 0; i--)
        {
            EffectZone z = effectZones[i];
            if (z.Role != role) continue;

            double until = Math.Min(now, z.Until);
            float elapsed = (float)Math.Max(0.0, until - z.LastTick);
            z.LastTick = until;

            float cx = z.X;
            float cy = z.Y;
            if (z.Follow && players.TryGetValue(z.OwnerPeer, out Player? owner) && owner.HasPosition)
            {
                cx = owner.X;
                cy = owner.Y;
            }

            if (elapsed > 0f)
            {
                float reach = z.Radius + EffectZoneMargin;
                foreach (Player p in players.Values)
                {
                    if (p.Role != role || !p.HasPosition || !p.PlaneAlive || p.PlaneShortId == 0) continue;
                    float dx = p.X - cx;
                    float dy = p.Y - cy;
                    if (dx * dx + dy * dy > reach * reach) continue;

                    if (z.Kind == ZoneHeal) HealPlayer(server, p, z, elapsed, now);
                    else GrantShield(p, z.Effect, z.BuffTime, now);
                }
            }

            if (z.Until <= now) effectZones.RemoveAt(i);
        }

        GiveBackShieldedDamage(server, role, now, log);
    }

    private static NetConnection? ConnectionOf(NetServer server, long peer)
    {
        foreach (NetConnection c in server.Connections)
        {
            if (c.RemoteUniqueIdentifier == peer && c.Status == NetConnectionStatus.Connected) return c;
        }
        return null;
    }

    private static void HealPlayer(NetServer server, Player p, EffectZone z, float elapsed, double now)
    {
        if (now - p.LastClientHeal < ClientHealGrace) return;      // the client heals itself
        if (p.HasHealth && p.Health >= 0.999f) return;
        float amount = z.Effect * elapsed;
        if (amount <= 0f) return;

        NetConnection? connection = ConnectionOf(server, p.Peer);
        if (connection == null) return;
        ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteIncrementPlayerStats(
            m, p.Peer, p.PlaneShortId, amount, 0f, p.X, p.Y, z.Capped));
    }

    private static void GrantShield(Player p, float factor, float buffTime, double now)
    {
        if (!shields.TryGetValue(p.Peer, out ShieldState? s))
        {
            s = new ShieldState();
            shields[p.Peer] = s;
        }
        // The same rule as TimedFadeEffect.Set: a stronger shield replaces a weaker one, the same one refreshes the time.
        if (s.Until <= now || factor >= s.Factor)
        {
            s.Factor = factor;
            s.Until = Math.Max(s.Until <= now ? 0 : s.Until, now + buffTime);
        }
    }

    // Health the shield blocked: heals (1 - 1 / factor) of each drop of the health fraction.
    private static void GiveBackShieldedDamage(NetServer server, string role, double now, Action<string, string> log)
    {
        if (shields.Count == 0) return;
        List<long> ended = new List<long>();
        foreach (KeyValuePair<long, ShieldState> pair in shields)
        {
            ShieldState s = pair.Value;
            if (!players.TryGetValue(pair.Key, out Player? p))
            {
                ended.Add(pair.Key);
                continue;
            }
            if (p.Role != role) continue;
            if (s.Until <= now || !p.PlaneAlive)
            {
                ended.Add(pair.Key);
                continue;
            }
            if (!p.HasHealth) continue;

            if (!s.HasBaseline || now < s.IgnoreUntil)
            {
                s.Baseline = p.Health;
                s.HasBaseline = true;
                continue;
            }

            float drop = s.Baseline - p.Health;
            if (drop <= 0.002f)
            {
                s.Baseline = p.Health;
                continue;
            }

            float maxHealth = EstimatedMaxHealth(p) * Math.Max(0.01f, ShieldHealthScale);
            float giveBackFraction = drop * (1f - 1f / Math.Max(1f, s.Factor));
            float giveBack = giveBackFraction * maxHealth;
            NetConnection? connection = ConnectionOf(server, p.Peer);
            if (connection == null || giveBack <= 0f) continue;

            ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteIncrementPlayerStats(
                m, p.Peer, p.PlaneShortId, giveBack, 0f, 0f, 0f, false));
            s.Baseline = Math.Min(1f, p.Health + giveBackFraction);
            s.IgnoreUntil = now + ShieldIgnoreAfterGiveBack;
            if (now - s.LastLog > 2.0)
            {
                s.LastLog = now;
                log("SKILL", p.Role + " shield x" + s.Factor.ToString("0.##", CultureInfo.InvariantCulture) + " gave back " +
                    giveBack.ToString("0.##", CultureInfo.InvariantCulture) + " health (drop " +
                    (drop * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "% of the plane)");
            }
        }
        foreach (long peer in ended) shields.Remove(peer);
    }

    // Base health of the plane of the class in the unit catalog (the real max health also has upgrades the server
    // does not track). 15 when unknown.
    private static float EstimatedMaxHealth(Player p)
    {
        float health = Enemies.UnitMaxHealth(PlayerPlaneTemplates.ForClass(p.Class).Name);
        return health > 0f && health < 1000f ? health : 15f;
    }
}
