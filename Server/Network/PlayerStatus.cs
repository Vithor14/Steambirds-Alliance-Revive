// What the players online are doing, for the launchers (the "playerDetails" of GET /revive/info and of the server search
// answer). The launcher of each player (and of the host) turns it into the Discord Rich Presence.
//
// Only game facts go out: no address, no account key, no Steam ID. The player name is the one the other players already see
// in "playerNames".
public static class PlayerStatus
{
    public static List<Dictionary<string, object?>> Describe()
    {
        var result = new List<Dictionary<string, object?>>();
        foreach (PlayerSession session in PlayerSessions.Online())
        {
            try
            {
                result.Add(DescribeOne(session));
            }
            catch
            {
                // A player in the middle of a switch: left out of this answer.
            }
        }
        return result.OrderBy(p => (string)(p["name"] ?? ""), StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Dictionary<string, object?> DescribeOne(PlayerSession session)
    {
        bool placed = Game.TryPlaceOf(session.Account.Id, out string role, out string? characterId, out string? planeClass, out long peer);

        // Plane (class, level) and pilot rank of the account.
        int planeLevel = 0;
        int pilotLevel;
        using (Accounts.Scope scope = Accounts.Use(session.Account))
        {
            pilotLevel = Accounts.Pilot().Level;
            Accounts.SavedPlane? plane = Accounts.GetPlane(characterId);
            if (plane != null)
            {
                planeClass = plane.Class;
                planeLevel = plane.Level;
            }
        }

        string place = "";
        string where = "";
        if (placed)
        {
            switch (role)
            {
                case "nexus":
                    place = "nexus";
                    where = Nexus.Name;
                    break;
                case "tutorial":
                    place = "tutorial";
                    where = "Training Grounds";
                    break;
                case "world":
                    place = "world";
                    where = WorldSettingsFile.Current.Name;
                    break;
                case DungeonServer.Role:
                    place = "dungeon";
                    where = DungeonServer.LabelOfPeer(peer);
                    break;
            }
        }

        return new Dictionary<string, object?>
        {
            ["name"] = session.Name,
            ["plane"] = planeClass ?? "",
            ["planeLevel"] = planeLevel,
            ["pilotLevel"] = pilotLevel,
            ["place"] = place,
            ["where"] = where
        };
    }
}
