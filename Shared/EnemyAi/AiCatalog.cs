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
    // Inside brains\: generic\ (AIs that work for any unit), world\ (AIs of open-world units) and dungeons\<name>\ (one folder
    // per dungeon; the tutorial is a dungeon too). Every brain file name (without the folder) is its id and must be unique.
    public const string GenericFolder = "generic";
    public const string WorldFolder = "world";
    public const string DungeonsFolder = "dungeons";
    public const string PackageKind = "steambirds-ai-package";

    public static readonly AiOption[] MoveModes =
    {
        new AiOption("orbit", "Orbit the player",
            "Flies to a circle around the player (or around its summoner or its home, see \"around\") and goes around it. Far away it approaches in a straight line. Uses: around, distance, deviation, speed, farSpeed, farFactor, spin."),
        new AiOption("path", "Fly a path",
            "Flies through a list of points in order (offsets from where the unit was born) and, with loop on, starts again from the first. Without loop it stops at the last point. Uses: points, loop, speed."),
        new AiOption("zigzag", "Zigzag toward the player",
            "Straight dashes of \"distance\" units, \"deviation\" degrees to one side of the line to the player and then to the other, facing each dash (a hummingbird closing in). Close to the player the dashes go wider, across. Uses: distance, deviation, jitter, pause, speed, turnMult."),
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
            "Flies to the place where the unit was born and circles there. Uses: speed."),
        new AiOption("builtin", "Built-in flight",
            "Runs one of the server's built-in flights (the \"behavior\" of the unit type, or the one named in \"builtin\": chase, circle, charge, turret, route, kamikaze, patrol, escort). Uses: builtin, speedMult. In the simulator it is only an approximation.")
    };

    // The built-in flights a state can ask for (the behaviors of the server).
    public static readonly string[] BuiltinFlights = { "chase", "circle", "charge", "turret", "route", "kamikaze", "patrol", "escort", "fixed", "idle", "mine" };

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
        new AiOption("summonsAtMost", "Own summons alive at most",
            "True when the units this unit summoned that are still alive number at most this value (0 = all of them are dead)."),
        new AiOption("legsDone", "Dashes done at least",
            "True once the unit finished this many zigzag dashes (or reached this many points of a path) in this state."),
        new AiOption("siblingsAtMost", "Summoner's other summons alive at most",
            "For a summoned unit: true when the other units its summoner summoned that are still alive number at most this value (0 = all of them are dead). With \"unit\", only units of that type count. True when the unit was not summoned."),
        new AiOption("chance", "Chance per second (0-1)", "Every second the transition happens with this probability (0.5 = about once every two seconds).")
    };

    // What an orbit can circle (move.around).
    public static readonly string[] OrbitCenters = { "player", "summoner", "home" };

    public static string OrbitCenterOf(AiMove move)
    {
        string around = (move.Around ?? "").Trim().ToLowerInvariant();
        return OrbitCenters.Contains(around) ? around : "player";
    }

    public static bool IsMoveMode(string? id) => MoveModes.Any(o => string.Equals(o.Id, id, StringComparison.Ordinal));

    public static bool IsConditionType(string? id) => ConditionTypes.Any(o => string.Equals(o.Id, id, StringComparison.Ordinal));

    // File name and id of a brain or behavior: lowercase letters, digits and "_".
    // The id an "ai" field means: the id itself, or the file name at the end of a path inside brains\ ("generic/generic_chase",
    // "dungeons\arena\dungeon_arena_swarmboss"). The file name is the id, whatever folder the file is in.
    public static string IdOfReference(string? reference)
    {
        string text = (reference ?? "").Trim();
        int cut = Math.Max(text.LastIndexOf('/'), text.LastIndexOf('\\'));
        if (cut >= 0) text = text.Substring(cut + 1);
        if (text.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) text = text.Substring(0, text.Length - 5);
        return text.Trim();
    }

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
