using Lidgren.Network;

// Enemies: what each client is told about ("potentially visible set").
//
// The client draws an arrow at the edge of the screen for every enemy it knows, at any distance, so an instance that tells every
// player about every enemy fills the screen with arrows. The original server did not: the client's own PVS code (PVSCollection,
// EntityDescriptor.visibleRadius, GlobalConfig.networkCutoff) only sends a unit to the players whose viewpoint is within its
// radius, and takes it away again when they leave. This is the same idea for the roles that ask for it (the open world:
// world.json "enemies.viewRadius"); the dungeons and the tutorial keep sending everything to everybody.
//
// A unit enters a player's view when it comes within the radius and leaves it beyond the radius plus a margin (so one that
// hovers at the edge does not flicker). It is told to the client with the normal spawn message and taken away with a kill message
// of the "vanished" kind, so the client forgets it (and its arrow) without an explosion. The units of a player (turrets, mines)
// always stay known to their owner.
public static partial class Enemies
{
    // Radius (units) by role, as the instance names it ("[world]"). A role that is not here sends everything to everybody.
    private static readonly Dictionary<string, float> viewRadius = new Dictionary<string, float>();

    // Sets how far (units) a player of this role sees enemies; 0 or less = all of them. The open world calls it every tick with
    // the value of world.json, so a change applies at once.
    public static void SetViewRadius(string role, float radius)
    {
        lock (Game.Lock)
        {
            viewRadius["[" + role + "]"] = Math.Max(0f, radius);
        }
    }

    private static float ViewRadiusOf(Instance inst) => viewRadius.TryGetValue(inst.Role, out float r) ? r : 0f;

    // Beyond this distance a unit that the player knows is taken away from them (a little more than the radius that brings it in).
    private static float LeaveRadius(float radius) => radius + Math.Max(8f, radius * 0.15f);

    private static HashSet<int> KnownOf(Instance inst, long peer)
    {
        if (!inst.Known.TryGetValue(peer, out HashSet<int>? set)) inst.Known[peer] = set = new HashSet<int>();
        return set;
    }

    // Does this player's client know the unit? Always true when the instance sends everything.
    private static bool IsKnownTo(Instance inst, NetConnection member, Enemy enemy)
    {
        if (!inst.Filtering) return true;
        return inst.Known.TryGetValue(member.RemoteUniqueIdentifier, out HashSet<int>? set) && set.Contains(enemy.ShortId);
    }

    // Each tick: tells every player about the units that came within their radius and takes away the ones that left it.
    private static void UpdateInterest(Instance inst, double now)
    {
        float radius = ViewRadiusOf(inst);
        if ((radius > 0f) != inst.Filtering) SwitchInterest(inst, radius > 0f, now);
        if (!inst.Filtering) return;

        float enter2 = radius * radius;
        float leave = LeaveRadius(radius);
        float leave2 = leave * leave;

        foreach (NetConnection member in inst.Members.ToList())
        {
            if (member.Status != NetConnectionStatus.Connected) continue;
            long peer = member.RemoteUniqueIdentifier;
            // No plane yet (still loading, or between planes): nothing changes for this player.
            if (!inst.Tracks.TryGetValue(peer, out PlayerTrack? track) || !track.Has) continue;

            HashSet<int> known = KnownOf(inst, peer);
            List<Enemy>? entering = null;
            List<Enemy>? leaving = null;
            foreach (Enemy enemy in inst.Enemies.Values)
            {
                bool isKnown = known.Contains(enemy.ShortId);
                bool wanted;
                if (enemy.AllyKind != 0 && enemy.OwnerPeer == peer) wanted = true;
                else
                {
                    float dx = enemy.X - track.X, dy = enemy.Y - track.Y;
                    float d2 = dx * dx + dy * dy;
                    wanted = isKnown ? d2 <= leave2 : d2 <= enter2;
                }

                if (wanted && !isKnown) (entering ??= new List<Enemy>()).Add(enemy);
                else if (!wanted && isKnown) (leaving ??= new List<Enemy>()).Add(enemy);
            }

            if (leaving != null)
            {
                for (int start = 0; start < leaving.Count; start += 30)
                    SendKills(inst, member, leaving.Skip(start).Take(30).ToList(), ReasonVanished, ResultNone, now);
                foreach (Enemy enemy in leaving) known.Remove(enemy.ShortId);
            }
            if (entering != null)
            {
                SendSpawnBatches(inst, entering, now, member);
                foreach (Enemy enemy in entering) known.Add(enemy.ShortId);
            }
        }
    }

    // The radius of the role turned on or off while the instance was running (the host edited world.json).
    // On: everybody was told about every unit so far, so that is what each one knows. Off: each one gets what is missing.
    private static void SwitchInterest(Instance inst, bool on, double now)
    {
        if (on)
        {
            inst.Known.Clear();
            foreach (NetConnection member in inst.Members)
                KnownOf(inst, member.RemoteUniqueIdentifier).UnionWith(inst.Enemies.Keys);
            inst.Filtering = true;
            return;
        }

        foreach (NetConnection member in inst.Members.ToList())
        {
            HashSet<int> known = KnownOf(inst, member.RemoteUniqueIdentifier);
            List<Enemy> missing = inst.Enemies.Values.Where(e => !known.Contains(e.ShortId)).ToList();
            if (missing.Count > 0) SendSpawnBatches(inst, missing, now, member);
        }
        inst.Known.Clear();
        inst.Filtering = false;
    }
}
