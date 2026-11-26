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
    // orbit: who the circle is around. Empty = the player. "leader", "summoner" or "home".
    public string Around { get; set; } = "";
    // orbit: stay on a fixed angle of the circle (spinRate degrees per second, angle where it starts) instead of chasing a point ahead.
    public bool Lock { get; set; }
    public float Angle { get; set; }
    public float SpinRate { get; set; }
    public float FaceOffset { get; set; }
    // orbit with lock: face along the circle, the way the center turns, instead of faceOffset.
    public bool FaceForward { get; set; }
    // path: false stops at the last point. True (the default) starts again at the first.
    public bool Loop { get; set; } = true;
    // When set, this replaces the unit's own top speed (a unit whose game data says 0 can still move). 0 = the unit's speed.
    public float TopSpeed { get; set; }
    // Multiplies how fast the unit may turn in this state (1 = as the unit says).
    public float TurnMult { get; set; } = 1f;
    // Random shove of the destination, in units. 0 = none.
    public float Jitter { get; set; }
    // Seconds of coasting between dashes (zigzag and any other mode). 0 = never.
    public float Pause { get; set; }
    // path: points relative to where the unit was born. Each point is [x, y] or {"x","y"}.
    [JsonConverter(typeof(AiPointListConverter))]
    public List<AiPoint>? Points { get; set; }
}

public sealed class AiPoint
{
    public float X { get; set; }
    public float Y { get; set; }
}

// Reads a path as [[x, y], ...] or [{"x": x, "y": y}, ...].
public sealed class AiPointListConverter : JsonConverter<List<AiPoint>>
{
    public override List<AiPoint>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("points");
        var list = new List<AiPoint>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                reader.Read();
                float x = reader.TokenType == JsonTokenType.Number ? reader.GetSingle() : 0f;
                reader.Read();
                float y = reader.TokenType == JsonTokenType.Number ? reader.GetSingle() : 0f;
                while (reader.TokenType != JsonTokenType.EndArray && reader.Read()) { }
                list.Add(new AiPoint { X = x, Y = y });
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                float x = 0f, y = 0f;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    string name = reader.GetString() ?? "";
                    reader.Read();
                    float n = reader.TokenType == JsonTokenType.Number ? reader.GetSingle() : 0f;
                    if (name.Equals("x", StringComparison.OrdinalIgnoreCase)) x = n;
                    else if (name.Equals("y", StringComparison.OrdinalIgnoreCase)) y = n;
                }
                list.Add(new AiPoint { X = x, Y = y });
            }
            else reader.Skip();
        }
        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<AiPoint> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (AiPoint point in value)
        {
            writer.WriteStartObject();
            writer.WriteNumber("x", point.X);
            writer.WriteNumber("y", point.Y);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
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
    // summonsAtMost: count only the summons of this unit name. Empty = every summon.
    public string Unit { get; set; } = "";
    // summonsAtMost: count only the summons with this role (a formation piece or a summon line). Empty = every role.
    public string Role { get; set; } = "";
    // The condition is true when it is NOT met.
    public bool Not { get; set; }
}

// One line of a state's "summons" list. The old summon/summonCount fields are the same thing as a list of one.
public sealed class AiSummonEntry
{
    public string Id { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Ai { get; set; } = "";
    public string Role { get; set; } = "";
    // behind, front, left, right, ring.
    public string Place { get; set; } = "behind";
    public int Count { get; set; } = 1;
    public float Every { get; set; }
    public int Max { get; set; }
    public float Distance { get; set; } = 5f;
    public float Angle { get; set; }
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

    // Summons units of this type when the state starts. A "summons" list replaces these four fields.
    public string? Summon { get; set; }
    public int SummonCount { get; set; } = 1;
    public float SummonEvery { get; set; }
    public int SummonMax { get; set; }
    public string? SummonAi { get; set; }
    public string? SummonRole { get; set; }
    public string? SummonPlace { get; set; }
    public float SummonDistance { get; set; }
    public float SummonAngle { get; set; }
    // Several lines: a phase can call different units, each with its own cap, AI and place.
    public List<AiSummonEntry>? Summons { get; set; }

    // The unit ignores damage in this state: for ShieldSeconds, or the whole state when it is 0.
    public bool Shield { get; set; }
    public float ShieldSeconds { get; set; }
    // Visual effect (a statefulEffects name) shown while the shield lasts. Empty = "tankShield".
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
    // Id of a file in ai\formations. Spawning a unit that runs this AI also spawns that formation.
    public string? Formation { get; set; }
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
