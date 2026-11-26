using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteambirdsAi;

// The AI files. Two kinds, both plain JSON, one per file, so they can be edited by hand, copied, shared and loaded as needed:
//
//   ai\behaviors\<id>.json   a BEHAVIOR: one way of acting (how to move + how to shoot + what happens on contact).
//   ai\brains\<id>.json      an AI ("brain"): a state machine. Each state acts like a behavior (it names a behavior file or
//                            carries its own move/fire/contact) and has transitions to other states with conditions.
//
// A unit type picks its AI with "ai": "<brain id>" (enemies.json or the "types" of a map). The server reads the files
// again by itself when they change.

public sealed class AiMove
{
    public string Mode { get; set; } = "orbit";
    // orbit: radius of the circle. wander: radius around home.
    public float Distance { get; set; } = 14f;
    // orbit: how many degrees ahead along the circle the destination is (bigger = wider spiral toward the circle).
    public float Deviation { get; set; } = 30f;
    // Fraction of the unit's top speed (1 = top speed).
    public float Speed { get; set; } = 0.7f;
    // orbit: speed used while farther than distance x farFactor from the player.
    public float? FarSpeed { get; set; }
    public float FarFactor { get; set; } = 1.6f;
    // approach: seconds of the player's velocity the unit aims ahead.
    public float Lead { get; set; }
    // wander: seconds between two destinations.
    public float Interval { get; set; } = 5f;
    // orbit direction: 1 clockwise, -1 counter-clockwise, 0 random per unit.
    public int Spin { get; set; }
    // hold: turns to look at the player.
    public bool Face { get; set; } = true;
    // builtin: which built-in flight of the server runs (chase, circle, charge, turret, route, kamikaze, patrol, escort).
    // Empty = the "behavior" of the unit type.
    public string Builtin { get; set; } = "";
    // builtin: multiplier of the unit's own speed (1 = as the type says; 1.2 = a fifth faster).
    public float SpeedMult { get; set; } = 1f;
    // Any mode: multiplier of how fast the unit turns (its turn rate in degrees per second; 2 = twice as fast as the unit type).
    public float TurnMult { get; set; } = 1f;
    // orbit: what the unit circles: "player" (default), "summoner" (the unit that summoned it; its home when it has none or it died)
    // or "home" (where the unit was born).
    public string Around { get; set; } = "player";
    // path: the points the unit flies through, in order, as offsets from its home (x, y in units).
    public List<AiPoint>? Points { get; set; }
    // path: after the last point it starts again from the first (false = it stays at the last point).
    public bool Loop { get; set; } = true;
    // Any mode: top speed in units per second while the state lasts (0 = the unit's own). For units the game made immobile
    // (top speed 0 in the game data, like the Ambassador, whose flight lived on the original server).
    public float TopSpeed { get; set; }
    // Any mode: the unit's body turns by itself at this many degrees per second (positive = clockwise, negative = counter-clockwise)
    // and it slides to its destination without turning its nose toward it (a spinning boss). 0 = off: it flies nose first.
    public float SpinRate { get; set; }
    // orbit around "summoner": the unit keeps its place on the circle and its facing relative to the summoner's body, so it turns
    // with it (a ring of guards fixed to a spinning boss, going the way the boss turns). It slides to its place.
    public bool Lock { get; set; }
    // orbit with lock: the unit's facing, in degrees from the direction that goes from the summoner to it (90 = along the circle,
    // clockwise; -90 = along it the other way, its left side toward the summoner; 0 = facing out; 180 = facing in).
    public float FaceOffset { get; set; } = 90f;
    // orbit with lock: the unit faces the way it travels around the circle (the way the summoner turns, so it flips when the summoner
    // turns the other way), instead of faceOffset. Off = faceOffset.
    public bool FaceForward { get; set; }
    // zigzag: each dash's angle off the line to the player is deviation plus or minus a random amount up to this many degrees
    // (0 = always exactly deviation). It stays inside the forward cone: never under 0 or over deviation + jitter.
    public float Jitter { get; set; }
    // zigzag: between two dashes the unit stops for at least this many seconds and turns to face the player, then dashes again
    // (the first dash after it is born goes at once). 0 = no stop: it turns into the next dash on the move.
    public float Pause { get; set; }
}

public sealed class AiPoint
{
    public float X { get; set; }
    public float Y { get; set; }
}

public sealed class AiFire
{
    public bool Enabled { get; set; } = true;
    // Gun mounts that shoot (0 = the first). Empty = the guns of the unit type.
    public List<int>? Guns { get; set; }
    public float RateMult { get; set; } = 1f;
    // Shooting range in units. 0 = the range of the gun.
    public float Range { get; set; }
    // How far ahead of a moving player it aims. Empty = the value of the unit type.
    public float? AimLead { get; set; }
}

public sealed class AiContact
{
    // The unit blows up (dies without XP or loot) when the player gets this close. 0 = never.
    public float ExplodeRadius { get; set; }
}

public sealed class AiBehaviorFile
{
    public int Format { get; set; } = AiCatalog.FormatVersion;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public AiMove? Move { get; set; }
    public AiFire? Fire { get; set; }
    public AiContact? Contact { get; set; }
}

public sealed class AiCondition
{
    public string Type { get; set; } = "always";
    public float Value { get; set; }
    // timeInState: the time is random between value and max.
    public float? Max { get; set; }
    // The condition is true when it is NOT met.
    public bool Not { get; set; }
    // siblingsAtMost: counts only the units of this unit type (the name the summoner's "summon" used). Empty = any type.
    public string? Unit { get; set; }
}

public sealed class AiTransition
{
    public string To { get; set; } = "";
    // Every condition must be true (AND). For OR, write two transitions. No conditions = always.
    public List<AiCondition>? When { get; set; }
}

public sealed class AiStateFile
{
    public string Id { get; set; } = "";
    // A behavior file. When empty the state uses its own move / fire / contact.
    public string? Behavior { get; set; }
    public AiMove? Move { get; set; }
    public AiFire? Fire { get; set; }
    public AiContact? Contact { get; set; }
    // The unit blows up when it enters the state (a fuse).
    public bool SelfDestruct { get; set; }
    // Chat message shown when the state starts.
    public string? Message { get; set; }
    // Speech bubble (a bark key of the client) when the state starts.
    public string? Bark { get; set; }

    // Summons units of this type (a type of enemies.json or of the map the unit is in) when the state starts...
    public string? Summon { get; set; }
    public int SummonCount { get; set; } = 1;
    // ...and again every this many seconds while the state lasts (0 = only when it starts).
    public float SummonEvery { get; set; }
    // No new summons while this many of its own summons are alive (0 = no limit).
    public int SummonMax { get; set; }
    // Where the summons appear: "behind" (default: behind the summoner, side by side) or "around" (evenly spaced on a circle around it).
    public string? SummonPlace { get; set; }
    // Distance from the summoner where they appear (0 = 5 units).
    public float SummonDistance { get; set; }
    // summonPlace "around": degrees the circle is turned (0 = the first unit right behind the summoner). Half the spacing (60 for 3
    // units) puts a second ring between the units of the first one.
    public float SummonAngle { get; set; }

    // The unit ignores damage in this state: for ShieldSeconds, or the whole state when it is 0.
    public bool Shield { get; set; }
    public float ShieldSeconds { get; set; }
    // Visual effect (a statefulEffects name) shown on top of the shielded glow (FLAG_SHIELDED) while the shield lasts. Empty = only the glow.
    public string? ShieldEffect { get; set; }

    // Visual effect (a statefulEffects name) the unit shows from this state on. Empty = keeps the one it has.
    public string? Effect { get; set; }
    // Turns the homing bullets "on" or "off" from this state on (empty = keeps what the unit has).
    public string? Homing { get; set; }

    // Where the state is drawn in the launcher's graph view (editor only, the server ignores it).
    public float? GraphX { get; set; }
    public float? GraphY { get; set; }
    // Checked in order, every tick: the first one whose conditions are all true wins.
    public List<AiTransition>? Transitions { get; set; }
}

public sealed class AiBrainFile
{
    public int Format { get; set; } = AiCatalog.FormatVersion;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Initial { get; set; } = "";
    // The units this AI was made for: names of game units (the "template" of a unit type, e.g. "swarmboss"). Used to offer the AI
    // only to those units. Ignored in airains\generic: an AI there works for any unit.
    public List<string>? Units { get; set; }
    // Behaviors that live inside this AI file: only its own states can pick them (a state's "behavior" is looked up here first,
    // then in aiehaviors). An embedded behavior has an "id" like a behavior file does.
    public List<AiBehaviorFile>? Behaviors { get; set; }
    public List<AiStateFile>? States { get; set; }
}

public static class AiJson
{
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static AiBehaviorFile ParseBehavior(string text)
    {
        return JsonSerializer.Deserialize<AiBehaviorFile>(text, Options) ?? throw new JsonException("empty file");
    }

    public static AiBrainFile ParseBrain(string text)
    {
        return JsonSerializer.Deserialize<AiBrainFile>(text, Options) ?? throw new JsonException("empty file");
    }
}
