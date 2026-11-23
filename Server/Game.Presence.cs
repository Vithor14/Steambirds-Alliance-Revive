// Game (part): what each player is doing right now, for the launchers' Rich Presence (Network\PlayerStatus.cs).

public static partial class Game
{
    // Where a player is and which plane flies. role: "nexus", "dungeon", "world" or "tutorial" (RoleName in Program.cs).
    // false when the account has no connection with a plane yet.
    public static bool TryPlaceOf(byte[] accountId, out string role, out string? characterId, out string? planeClass, out long peer)
    {
        lock (gate)
        {
            Player? found = null;
            foreach (Player j in players.Values)
            {
                if (j.Account == null || !j.Account.AsSpan().SequenceEqual(accountId)) continue;
                if (found == null || j.CharacterId != null) found = j;
            }
            role = found?.Role ?? "";
            characterId = found?.CharacterId;
            planeClass = found?.Class;
            peer = found?.Peer ?? 0;
            return found != null;
        }
    }
}
