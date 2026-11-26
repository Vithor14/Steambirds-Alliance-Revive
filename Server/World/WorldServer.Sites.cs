using Lidgren.Network;

// Open world: the regions the player flies over and the enemies of the spawn sites.
//
// Reconstruction: the original server decided by its own rules (named_spawn_rules, empty in the client) where and when each
// enemy appeared. Here the land has "spawn sites" on a jittered grid; a site gets its enemies when a player comes within
// spawnRadius, they sleep until a player is within wakeRadius, and they are taken away when every player is farther than
// despawnRadius. After the enemies of a site die it waits the region's secondsPerStandardEnemyIn10x10 (from the game's config)
// times the enemies it had, times respawnScale of world.json.
public static partial class WorldServer
{
    private sealed class Site
    {
        public int Index;
        public float X, Y;
        public WorldData.Region Region = null!;
        public bool Active;
        public double ReadyAt;
        public int LastCount;
        public readonly List<int> Units = new List<int>();
    }

    private static void BuildSites(Session s, WorldSettings settings)
    {
        s.Sites.Clear();
        WorldMap map = s.Map!;
        var rng = new Random(s.Seed ^ 0x5174);
        float spacing = settings.Enemies.SiteSpacingTiles;
        float clearRadius = settings.Spawn.SafeZoneRadius;
        int steps = (int)(map.Half / spacing) + 1;
        for (int n = -steps; n <= steps; n++)
        {
            for (int m = -steps; m <= steps; m++)
            {
                float a = (n + 0.15f + 0.7f * (float)rng.NextDouble()) * spacing;
                float b = (m + 0.15f + 0.7f * (float)rng.NextDouble()) * spacing;
                Ground.ToWorld(a, b, out float x, out float y);
                WorldData.Region? region = map.RegionOfPoint(x, y);
                if (region == null) continue;
                (int ta, int tb) = WorldMap.TileOf(x, y);
                if (!map.InWorld[ta + map.Half, tb + map.Half]) continue;
                float dx = x - map.SpawnX, dy = y - map.SpawnY;
                if (dx * dx + dy * dy < clearRadius * clearRadius) continue;
                if (!map.IsOpenLand(x, y, 6f)) continue;
                // A region with no enemies still gets its sites: the host may add enemies to its list (world-enemies.json) later.
                s.Sites.Add(new Site { Index = s.Sites.Count, X = x, Y = y, Region = region });
            }
        }
    }

    // ================================================================== tick

    private static void Tick(Session s, double now, Action<string, string> log)
    {
        // Players that must go back.
        foreach (KeyValuePair<long, double> pair in s.Returns.ToList())
        {
            if (now < pair.Value) continue;
            s.Returns.Remove(pair.Key);
            NetConnection? who = s.Members.FirstOrDefault(c => c.RemoteUniqueIdentifier == pair.Key);
            if (who != null && ReturnToNexus != null) ReturnToNexus(s.Server, who, "[world] the world could not keep the player");
        }
        if (s.Map == null) return;

        // How far a client sees enemies (a change in world.json applies at once).
        Enemies.SetViewRadius(Role, WorldSettingsFile.Current.Enemies.ViewRadius);

        List<(NetConnection Connection, float X, float Y)> players = Players(s);
        UpdateRegions(s, players);
        if (WorldSettingsFile.Current.Enemies.Enabled) UpdateSites(s, players, now, log);
        UpdateCarriers(s, players, now, log);
        WriteLive(s, now);
    }

    // Tells each player the name of the region under their plane when it changes.
    private static void UpdateRegions(Session s, List<(NetConnection Connection, float X, float Y)> players)
    {
        foreach ((NetConnection connection, float x, float y) in players)
        {
            WorldData.Region? region = s.Map!.RegionOfPoint(x, y);
            if (region == null) continue;   // over water or a canyon: the last region stays
            long peer = connection.RemoteUniqueIdentifier;
            if (s.RegionShown.TryGetValue(peer, out string? shown) && shown == region.Base) continue;
            s.RegionShown[peer] = region.Base;
            WorldObjects.SendSubterritoryName(s.Server, connection, region.Base);
        }
    }

    private static void UpdateSites(Session s, List<(NetConnection Connection, float X, float Y)> players, double now,
        Action<string, string> log)
    {
        NetConnection? handle = Connected(s).FirstOrDefault();
        if (handle == null) return;
        WorldSettings.EnemySettings settings = WorldSettingsFile.Current.Enemies;
        float spawn2 = settings.SpawnRadius * settings.SpawnRadius;
        float despawn2 = settings.DespawnRadius * settings.DespawnRadius;

        int alive = 0;
        foreach (Site site in s.Sites)
        {
            if (!site.Active) continue;
            site.Units.RemoveAll(id => !Enemies.IsAlive(handle, id));
            alive += site.Units.Count;
        }

        foreach (Site site in s.Sites)
        {
            float nearest2 = float.MaxValue;
            foreach ((NetConnection _, float px, float py) in players)
            {
                float dx = site.X - px, dy = site.Y - py;
                float d2 = dx * dx + dy * dy;
                if (d2 < nearest2) nearest2 = d2;
            }

            if (site.Active)
            {
                if (site.Units.Count == 0)
                {
                    // Everybody of the group died: the site rests before it spawns again.
                    site.Active = false;
                    s.Kills++;
                    site.ReadyAt = now + Math.Max(5.0, site.Region.SecondsPerStandardEnemyIn10x10 * Math.Max(1, site.LastCount) * settings.RespawnScale);
                }
                else if (nearest2 > despawn2)
                {
                    foreach (int id in site.Units) Enemies.RemoveUnit(handle, id, false, now);
                    alive -= site.Units.Count;
                    site.Units.Clear();
                    site.Active = false;
                    site.ReadyAt = now + 3.0;
                }
                continue;
            }

            if (now < site.ReadyAt || nearest2 > spawn2 || alive >= settings.MaxAlive) continue;
            alive += SpawnSite(s, handle, site, settings, now, log);
        }
    }

    // Creates the group of a site (asleep). Returns how many units were created.
    private static int SpawnSite(Session s, NetConnection handle, Site site, WorldSettings.EnemySettings settings, double now,
        Action<string, string> log)
    {
        List<WorldEnemyPools.Family> pool = WorldEnemyPools.For(site.Region);
        if (pool.Count == 0)
        {
            // The list of the region is empty (the host may fill it, world-enemies.json): look again in a while.
            site.ReadyAt = now + 20.0;
            return 0;
        }
        WorldEnemyPools.Family family = pool[random.Next(pool.Count)];

        // The units of the group, the leader first. "Leader" ones are the group's leader (the others escort it).
        var templates = new List<(string Template, bool Leader)>();
        if (family.Counts != null)
        {
            // An exact group (the host set how many of each): one leader picked at random, every member, each as many times as said.
            int Count(string unit) => family.Counts.TryGetValue(unit, out int n) ? Math.Clamp(n, 0, WorldEnemyLists.MaxCount) : 1;
            if (!family.Solo && family.Leaders.Count > 0)
            {
                string leader = family.Leaders[random.Next(family.Leaders.Count)];
                for (int i = Count(leader); i > 0; i--) templates.Add((leader, true));
            }
            if (family.Solo && family.Members.Count > 0)
            {
                string alone = family.Members[random.Next(family.Members.Count)];
                for (int i = Count(alone); i > 0; i--) templates.Add((alone, false));
            }
            else
            {
                foreach (string member in family.Members)
                    for (int i = Count(member); i > 0; i--) templates.Add((member, false));
            }
        }
        else
        {
            int size = family.Solo ? 1 : random.Next(Math.Min(2, settings.GroupSizeMax), settings.GroupSizeMax + 1);
            if (!family.Solo && family.Leaders.Count > 0) templates.Add((family.Leaders[random.Next(family.Leaders.Count)], true));
            while (templates.Count < size && family.Members.Count > 0) templates.Add((family.Members[random.Next(family.Members.Count)], false));
            if (templates.Count == 0 && family.Leaders.Count > 0) templates.Add((family.Leaders[0], true));
        }

        // A formation (the Five Star ring, and any other leader that has one) replaces the random escorts:
        // only the leader is created here, and it brings its pieces with it.
        string? lead = null;
        foreach ((string template, bool isLeader) in templates)
        {
            if (!isLeader || lead != null) continue;
            lead = template;
        }
        bool formationOwns = false;
        if (lead != null)
        {
            string? host = WorldEnemyLists.HostAi(site.Region.Name, lead);
            formationOwns = host != null ? Formations.ForAi(host) != null : Formations.For(lead) != null;
            if (formationOwns) templates = new List<(string Template, bool Leader)> { (lead, true) };
        }

        string group = "site:" + site.Index;
        int created = 0;
        int leaderId = 0;
        foreach ((string template, bool isLeader) in templates)
        {
            string key = EnsureType(template, settings, site.Region.Name);
            float x = site.X, y = site.Y;
            if (created > 0)
            {
                double angle = random.NextDouble() * Math.PI * 2.0;
                float distance = 5f + (float)random.NextDouble() * 5f;
                float nx = site.X + (float)Math.Sin(angle) * distance;
                float ny = site.Y + (float)Math.Cos(angle) * distance;
                if (s.Map!.IsFreeLand(nx, ny))
                {
                    x = nx;
                    y = ny;
                }
            }
            float rotation = (float)(random.NextDouble() * 360.0 - 180.0);
            int id = Enemies.CreateFromScript(s.Server, handle, Role, key, x, y, rotation, group, null, null, now, log);
            if (id == 0) continue;
            Enemies.SetAsleep(handle, id, true, settings.WakeRadius);
            // The first leader of the group is the one the others circle (an AI with the "orbitLeader" move).
            if (isLeader && leaderId == 0) leaderId = id;
            else if (leaderId != 0) Enemies.SetLeader(handle, id, leaderId);
            site.Units.Add(id);
            created++;
        }
        if (formationOwns && leaderId != 0)
        {
            int extra = Enemies.SpawnFormationMembers(handle, leaderId, now, log);
            created += extra;
        }

        site.LastCount = created;
        if (created == 0)
        {
            site.ReadyAt = now + 30.0;
            return 0;
        }
        site.Active = true;
        s.Spawned++;
        if (s.Spawned <= 6)
        {
            log("WORLD", "[world] site " + site.Index + " (" + site.Region.Name + ", tier " + site.Region.Tier + ", at " + site.X.ToString("0") + ", " +
                site.Y.ToString("0") + ") spawned " + string.Join(", ", templates.Select(u => u.Template).Take(created)) + " (asleep until a player is within " +
                settings.WakeRadius.ToString("0") + " units)");
        }
        return created;
    }

    // The enemy type of a template in the world: the game's unit with the default AI (generic_chase), free to roam. A type with the same
    // key in enemies.json ("world/<template>") replaces it, so the host or the Enemy AI tab can give it its own AI.
    private static string EnsureType(string template, WorldSettings.EnemySettings settings, string regionName)
    {
        // The AI the host picked for this unit in this biome (world-enemies.json). A unit with its own AI is a type of its own.
        string ai = WorldEnemyLists.AiFor(regionName, template);
        if (ai.Equals(WorldEnemyLists.DefaultAi, StringComparison.OrdinalIgnoreCase))
            ai = Formations.DefaultAi(template) ?? ai;
        string key = "world/" + template + (ai.Equals(WorldEnemyLists.DefaultAi, StringComparison.OrdinalIgnoreCase) ? "" : "~" + ai);
        Enemies.RegisterDefaultType(key, new Enemies.EnemyType
        {
            Name = template,
            Template = template,
            Ai = ai,
            FreeRoam = true,
            HealthMultiplier = settings.HealthMultiplier
        });
        return key;
    }
}
