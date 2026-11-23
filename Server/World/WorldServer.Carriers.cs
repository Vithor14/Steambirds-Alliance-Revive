using Lidgren.Network;

// Open world: dungeon carriers (portals) that appear on the land for a while. Flying into one takes the player to that dungeon
// (the dungeon server, with the same plane); when the dungeon ends the player returns to the Nexus, as always.
//
// Reconstruction: the original server decided which dungeon showed up, where and for how long; here it is one random open dungeon
// of dungeons.json at a time, up to maxAtOnce, every everySeconds, for lifetimeSeconds (world.json).
public static partial class WorldServer
{
    private sealed class Carrier
    {
        public int Id;
        public DungeonCatalog.Entry Entry = null!;
        public float X, Y;
        public double EndsAt;
        // The carrier leaves the map a little after its countdown ends (the client closes its doors first).
        public double RemoveAt;
    }

    private const double LeaveDelay = 17.0;   // the client's finish animation lasts 15.5 s

    private static void UpdateCarriers(Session s, List<(NetConnection Connection, float X, float Y)> players, double now,
        Action<string, string> log)
    {
        WorldSettings.CarrierSettings settings = WorldSettingsFile.Current.Carriers;

        foreach (Carrier carrier in s.Carriers.Values.Where(c => now >= c.RemoveAt).ToList())
        {
            foreach (NetConnection member in Connected(s))
                WorldObjects.SendRemoval(s.Server, member, carrier.Id, carrier.X, carrier.Y, WorldObjects.ReasonFizzled, now);
            s.Carriers.Remove(carrier.Id);
            log("WORLD", "[world] carrier of " + carrier.Entry.Label + " (" + carrier.Id + ") left");
        }

        if (!settings.Enabled || players.Count == 0 || now < s.NextCarrierAt || s.Carriers.Count >= settings.MaxAtOnce) return;
        s.NextCarrierAt = now + settings.EverySeconds;
        Carrier? made = CreateCarrier(s, settings, null, now, log);
        if (made != null) Chat(s, "A dungeon carrier appeared in the open world: " + made.Entry.Label + ". " + Direction(s, made, players));
    }

    // One carrier of an open dungeon (a given one, or at random among those without a carrier). Null if there is no room or dungeon.
    private static Carrier? CreateCarrier(Session s, WorldSettings.CarrierSettings settings, DungeonCatalog.Entry? wanted, double now,
        Action<string, string> log)
    {
        DateTime clock = DateTime.Now;
        DungeonCatalog.Entry? entry = wanted;
        if (entry == null)
        {
            List<DungeonCatalog.Entry> open = DungeonCatalog.All().Where(d => d.IsOpen(clock, out _)).ToList();
            List<DungeonCatalog.Entry> free = open.Where(d => !s.Carriers.Values.Any(c => c.Entry.Type == d.Type)).ToList();
            if (free.Count == 0) free = open;
            if (free.Count == 0) return null;
            entry = free[random.Next(free.Count)];
        }

        if (!FindCarrierPlace(s, settings, out float x, out float y)) return null;
        var carrier = new Carrier
        {
            Id = WorldObjects.NewShortId(),
            Entry = entry,
            X = x,
            Y = y,
            EndsAt = now + settings.LifetimeSeconds,
            RemoveAt = now + settings.LifetimeSeconds + LeaveDelay
        };
        s.Carriers[carrier.Id] = carrier;
        foreach (NetConnection member in Connected(s))
        {
            if (s.Synced.Contains(member.RemoteUniqueIdentifier)) SendCarrier(s, member, carrier, now);
        }
        log("WORLD", "[world] carrier of " + entry.Label + " (" + carrier.Id + ") at (" + x.ToString("0") + ", " + y.ToString("0") + "), for " +
            settings.LifetimeSeconds.ToString("0") + " s");
        return carrier;
    }

    private static bool FindCarrierPlace(Session s, WorldSettings.CarrierSettings settings, out float x, out float y)
    {
        WorldMap map = s.Map!;
        for (int attempt = 0; attempt < 400; attempt++)
        {
            double angle = random.NextDouble() * Math.PI * 2.0;
            float distance = settings.MinDistance + (float)random.NextDouble() * (settings.MaxDistance - settings.MinDistance);
            float tx = map.SpawnX + (float)Math.Sin(angle) * distance;
            float ty = map.SpawnY + (float)Math.Cos(angle) * distance;
            if (tx * tx + ty * ty > (map.Radius - 60f) * (map.Radius - 60f)) continue;
            if (!map.IsOpenLand(tx, ty, 14f)) continue;
            if (s.Carriers.Values.Any(c => (c.X - tx) * (c.X - tx) + (c.Y - ty) * (c.Y - ty) < 80f * 80f)) continue;
            x = tx;
            y = ty;
            return true;
        }
        x = 0f;
        y = 0f;
        return false;
    }

    private static void SendCarrier(Session s, NetConnection connection, Carrier carrier, double now)
    {
        WorldObjects.SendTimedDungeonPortal(s.Server, connection, carrier.Id, carrier.X, carrier.Y, 0f, DungeonCatalog.Endpoint,
            DungeonCatalog.HostId, carrier.Entry.Label, DungeonCatalog.GameVersion, carrier.Entry.Type, DungeonCatalog.MaxPlayers,
            now, carrier.EndsAt);
    }

    // "It is to the north-east, about 300 units away" (from the first player).
    private static string Direction(Session s, Carrier carrier, List<(NetConnection Connection, float X, float Y)> players)
    {
        if (players.Count == 0) return "";
        (NetConnection _, float px, float py) = players[0];
        float dx = carrier.X - px, dy = carrier.Y - py;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
        double heading = Math.Atan2(dx, dy) * 180.0 / Math.PI;
        int index = (int)Math.Round((heading + 360.0) % 360.0 / 45.0) % 8;
        return "It is to the " + names[index] + ", about " + (Math.Round(distance / 10.0) * 10).ToString("0") + " units away.";
    }

    // ServerSendMeThroughPortal in the world: the dungeon of this carrier, if it is still there.
    public static DungeonCatalog.Entry? CarrierEntry(NetConnection connection, int shortId, double now)
    {
        lock (Game.Lock)
        {
            Session? s = session;
            if (s == null || !s.Members.Contains(connection)) return null;
            if (!s.Carriers.TryGetValue(shortId, out Carrier? carrier)) return null;
            if (now > carrier.EndsAt) return null;
            DungeonCatalog.Entry? entry = DungeonCatalog.Find(carrier.Entry.Type);
            return entry != null && entry.IsOpen(DateTime.Now, out _) ? entry : null;
        }
    }
}
