using Lidgren.Network;

// Open world: the world is built before the first player comes in ("standby"), so the launcher's World map page already shows it,
// the first player does not wait for the generator and a new seed or size from world.json takes effect at once.
//
// A standby world has no members. The first player takes it over (PrepareSession) when it still matches world.json; otherwise it is
// built again. When the last player leaves, the world is discarded and the next standby is built (a new random one when the seed is 0).
public static partial class WorldServer
{
    private static double nextStandbyTry;
    private static double nextStandbyCheck;

    // What decides the map and the sites: if one of these changes in world.json, a standby world is built again.
    private static string Signature(WorldSettings s)
    {
        return string.Join("|", s.Seed, s.SizeScale, s.BiomeScale, s.Layout, s.LandFraction, s.Spawn.X, s.Spawn.Y, s.Spawn.Rotation,
            s.Spawn.SafeZoneRadius, s.Enemies.SiteSpacingTiles, string.Join(",", s.Enemies.Skip));
    }

    // Called by Update with the world locked. Builds the standby world, or builds it again when world.json changed.
    private static void MaintainStandby(NetServer server, double now, Action<string, string> log)
    {
        if (now < nextStandbyCheck) return;
        nextStandbyCheck = now + 0.5;
        WorldSettings settings = WorldSettingsFile.Current;
        Session? s = session;
        if (!settings.Enabled || !settings.Pregenerate)
        {
            // Not wanted (or the world is off): an old standby goes away.
            if (s != null && s.Pregenerated && s.Members.Count == 0) DropSession(s, log, quiet: true);
            return;
        }

        if (s != null)
        {
            if (!s.Pregenerated || s.Members.Count > 0) return;
            if (s.Signature == Signature(settings)) return;
            log("WORLD", "[world] world.json changed: the world waiting for players is built again");
            DropSession(s, log, quiet: true);
        }

        if (now < nextStandbyTry) return;
        Session fresh = BuildSession(server, now, log);
        if (fresh.Map == null)
        {
            // The reason is in the log; try again later (the game data may be fixed meanwhile).
            nextStandbyTry = now + 15.0;
            return;
        }
        fresh.Pregenerated = true;
        session = fresh;
        WorldLive.WriteStandby(fresh.Radius, fresh.Seed, fresh.Sites.Count, settings.Name, log);
        log("WORLD", "[world] the world is ready (seed " + fresh.Seed + ") and waits for the first player");
    }
}
