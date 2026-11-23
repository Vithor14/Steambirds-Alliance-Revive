using Lidgren.Network;

// The open world and the players that want to come to it (friends and flockmates: Social\SocialHandler.Teleport.cs).
public static partial class WorldServer
{
    // Can one more player come in? True while the world has fewer players than world.json "maxPlayers" (or has not been built yet).
    public static bool HasRoom()
    {
        lock (Game.Lock)
        {
            Session? s = session;
            if (s == null) return true;
            int inside = s.Members.Count(c => c.Status == NetConnectionStatus.Connected);
            return inside < WorldSettingsFile.Current.MaxPlayers;
        }
    }

    // Is this spot land the plane can be born on (not water, not a wall)? False when the world is not built.
    public static bool IsFreeLand(NetConnection connection, float x, float y)
    {
        lock (Game.Lock)
        {
            Session? s = session;
            return s?.Map != null && s.Members.Contains(connection) && s.Map.IsFreeLand(x, y);
        }
    }
}
