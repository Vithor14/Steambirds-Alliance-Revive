using Lidgren.Network;

// Open world: what the launcher's "World map" page shows (WorldLive).
public static partial class WorldServer
{
    private static double nextLiveWrite;

    private static void WriteLive(Session s, double now)
    {
        if (now < nextLiveWrite) return;
        nextLiveWrite = now + 1.0;
        WorldMap? map = s.Map;
        if (map == null) return;

        var live = new WorldLive.LiveFile
        {
            Active = true,
            Name = WorldSettingsFile.Current.Name,
            Seed = s.Seed,
            Radius = s.Radius,
            UptimeSeconds = (int)Math.Max(0, now - s.CreatedAt),
            Sites = s.Sites.Count,
            ActiveSites = s.Sites.Count(x => x.Active),
            EnemiesAlive = s.Sites.Sum(x => x.Units.Count),
            SitesCleared = s.Kills
        };

        float scale = Math.Max(0.01f, s.Radius / Ground.WorldRadius);
        foreach (NetConnection connection in Connected(s))
        {
            long peer = connection.RemoteUniqueIdentifier;
            var info = new WorldLive.PlayerInfo
            {
                Name = PlayerSessions.NameOf(connection),
                SecondsInWorld = s.JoinedAt.TryGetValue(peer, out double joined) ? (int)Math.Max(0, now - joined) : 0
            };
            using (Accounts.Scope scope = Accounts.UseFor(connection))
            {
                info.PilotRank = Accounts.Pilot().Level;
                if (Game.GetPlayerStatus(peer, out Game.PlayerStatus status))
                {
                    info.X = status.X;
                    info.Y = status.Y;
                    info.Rotation = status.Rotation;
                    info.Alive = status.Alive;
                    if (status.HasHealth) info.Health = Math.Clamp(status.Health, 0f, 1f);
                    info.Shots = status.Shots;
                    info.Hits = status.Hits;
                    float distance = MathF.Sqrt(status.X * status.X + status.Y * status.Y);
                    info.Distance = distance;
                    info.Elevation = WorldData.ElevationAt(distance / scale);
                    WorldData.Region? region = map.RegionOfPoint(status.X, status.Y);
                    if (region != null)
                    {
                        info.Region = region.ZoneName;
                        info.Tier = region.Tier;
                    }
                    Accounts.SavedPlane? plane = Accounts.GetPlane(status.CharacterId);
                    if (plane != null)
                    {
                        info.PlaneClass = plane.Class;
                        info.PlaneVariant = plane.Variant;
                        info.PlaneLevel = plane.Level;
                    }
                    else if (status.PlaneClass != null)
                    {
                        info.PlaneClass = status.PlaneClass;
                    }
                }
            }
            live.Players.Add(info);
        }

        foreach (Carrier carrier in s.Carriers.Values)
        {
            live.Carriers.Add(new WorldLive.CarrierInfo
            {
                Label = carrier.Entry.Label,
                Type = carrier.Entry.Type,
                X = carrier.X,
                Y = carrier.Y,
                SecondsLeft = (int)Math.Max(0, carrier.EndsAt - now)
            });
        }
        WorldLive.WriteLive(live, logger);
    }
}
