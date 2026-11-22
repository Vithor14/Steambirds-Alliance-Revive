namespace SteambirdsAi;

// One choice of a list (a way to move, a kind of condition) with the text the editor shows.
public sealed class AiOption
{
    public AiOption(string id, string label, string help)
    {
        Id = id;
        Label = label;
        Help = help;
    }

    public string Id { get; }
    public string Label { get; }
    public string Help { get; }
}

// Everything an AI file may use. The server runs these; the launcher editor builds its lists from them, so a new move
// or condition is added here, in AiRuntime and in the editor texts (L.T), and nowhere else.
public static class AiCatalog
{
    public const int FormatVersion = 1;
    public const string BehaviorsFolder = "behaviors";
    public const string BrainsFolder = "brains";
    public const string PackageKind = "steambirds-ai-package";

    public static readonly AiOption[] MoveModes =
    {
        new AiOption("orbit", "Orbit the player",
            "Flies to a circle around the player and goes around it. Far away it approaches in a straight line. Uses: distance, deviation, speed, farSpeed, farFactor, spin."),
        new AiOption("approach", "Go for the player",
            "Flies straight at the player, a little ahead of where the plane is going. Uses: speed, lead."),
        new AiOption("straight", "Keep going straight",
            "Does not turn toward anything: keeps flying where it points (a pass through the player). Uses: speed."),
        new AiOption("wander", "Wander near home",
            "Picks a random point around the place where the unit was born, flies there, and picks another. Uses: distance (radius), interval, speed."),
        new AiOption("hold", "Stay still",
            "Does not move. With face on, it turns to look at the player (a turret). Uses: face."),
        new AiOption("flee", "Run away",
            "Flies away from the player. Uses: speed."),
        new AiOption("home", "Go back home",
            "Flies to the place where the unit was born and circles there. Uses: speed.")
    };

    public static readonly AiOption[] ConditionTypes =
    {
        new AiOption("always", "Always", "Always true. The transition happens at once."),
        new AiOption("timeInState", "Time in this state (s)",
            "True after this many seconds in the state. With \"max\" the time is picked at random between value and max each time the state starts."),
        new AiOption("playerWithin", "Player closer than",
            "True when a player is closer than this distance (units). False when there is no player."),
        new AiOption("playerBeyond", "Player farther than",
            "True when a player exists and is farther than this distance (units)."),
        new AiOption("hasTarget", "Has a player to look at",
            "True while a live player is in the dungeon. Use \"not\" for: the player is gone."),
        new AiOption("healthBelow", "Health below (0-1)", "True when the health fraction is at or under the value (0.5 = half)."),
        new AiOption("healthAbove", "Health above (0-1)", "True when the health fraction is at or over the value."),
        new AiOption("recentlyHit", "Was hit in the last (s)", "True when the unit took damage in the last value seconds."),
        new AiOption("facingTarget", "Facing the player (degrees)", "True when the player is inside this angle in front of the unit."),
        new AiOption("chance", "Chance per second (0-1)", "Every second the transition happens with this probability (0.5 = about once every two seconds).")
    };

    public static bool IsMoveMode(string? id) => MoveModes.Any(o => string.Equals(o.Id, id, StringComparison.Ordinal));

    public static bool IsConditionType(string? id) => ConditionTypes.Any(o => string.Equals(o.Id, id, StringComparison.Ordinal));

    // File name and id of a brain or behavior: lowercase letters, digits and "_".
    public static string CleanId(string? text)
    {
        var chars = new List<char>();
        foreach (char c in (text ?? "").Trim().ToLowerInvariant())
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_') chars.Add(c);
            else if (c == ' ' || c == '-' || c == '.') chars.Add('_');
        }
        string id = new string(chars.ToArray()).Trim('_');
        return id.Length > 60 ? id.Substring(0, 60) : id;
    }
}
