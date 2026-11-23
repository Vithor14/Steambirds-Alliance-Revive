// Game (part): a read-only look at what the server knows about a player's plane, for the pages that show players (the open world map).
public static partial class Game
{
    public readonly record struct PlayerStatus(float X, float Y, float Rotation, bool Alive, bool HasHealth, float Health,
        string? CharacterId, string? PlaneClass, int Shots, int Hits, double Damage);

    // False while the player has no known position yet. Health is the fraction (0 to 1) the client last reported.
    public static bool GetPlayerStatus(long peer, out PlayerStatus status)
    {
        lock (gate)
        {
            if (!players.TryGetValue(peer, out Player? j) || !j.HasPosition)
            {
                status = default;
                return false;
            }
            status = new PlayerStatus(j.X, j.Y, j.Rotation, j.PlaneAlive, j.HasHealth, j.Health, j.CharacterId, j.Class, j.Shots, j.Hits, j.Damage);
            return true;
        }
    }
}
