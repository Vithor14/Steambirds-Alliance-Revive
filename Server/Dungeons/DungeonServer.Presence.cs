// DungeonServer (part): name of the dungeon a connection is in, for the launchers' Rich Presence.

public static partial class DungeonServer
{
    // Label of the dungeon of this connection (the peer id), or "" when it is not in one.
    public static string LabelOfPeer(long peer)
    {
        lock (Game.Lock)
        {
            return sessions.TryGetValue(peer, out Session? s) ? s.Entry.Label : "";
        }
    }
}
