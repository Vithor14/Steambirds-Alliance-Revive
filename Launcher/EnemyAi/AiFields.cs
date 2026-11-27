using System.Globalization;
using System.Text.Json.Nodes;
using SteambirdsAi;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;

namespace SteambirdsLauncher.EnemyAi;

// The fields of the AI forms (JsonForm specs) and the texts of the lists. Keys are the names used in the AI files.
internal static class AiFields
{
    public static IEnumerable<FieldSpec> Info()
    {
        yield return new FieldSpec("name", "Name", FieldKind.Text) { Help = "Name shown in the launcher. The file name is the id the unit types use." };
        yield return new FieldSpec("author", "Author", FieldKind.Text) { Help = "Who made it (shown to whoever you share the file with)." };
        yield return new FieldSpec("description", "Description", FieldKind.MultiText) { Help = "What it does, in a few words." };
    }

    // Move, fire and contact: the whole of a behavior. A state without a behavior file has these in itself.
    public static IEnumerable<FieldSpec> Behavior()
    {
        yield return FieldSpec.Header("Movement");
        yield return new FieldSpec("move.mode", "Mode", FieldKind.Choice)
        {
            Choices = () => AiCatalog.MoveModes.Select(o => o.Id),
            AllowEmpty = false,
            Help = string.Join("\n", AiCatalog.MoveModes.Select(o => o.Id + ": " + L.T(o.Help)))
        };
        yield return Number("move.speed", "Speed (fraction of top speed)", 0.7, 0, 3, 0.05,
            "1 = the unit's top speed. Most modes use it.", UsedBy("orbit", "orbitLeader", "approach", "straight", "wander", "flee", "home"));
        yield return new FieldSpec("move.farSpeed", "Speed when far (orbit)", FieldKind.OptionalNumber)
        {
            Help = "orbit: speed while the player is farther than distance x \"far factor\". Empty = the same speed.",
            EnabledWhen = UsedBy("orbit", "orbitLeader")
        };
        yield return Number("move.distance", "Distance", 14, 0, 500, 1,
            "orbit: radius of the circle. wander: radius around home. zigzag: length of one dash.", UsedBy("orbit", "orbitLeader", "wander", "zigzag"));
        yield return Number("move.deviation", "Deviation (degrees)", 30, 0, 180, 5,
            "orbit: how many degrees ahead along the circle the unit aims. zigzag: degrees off the line to the player each dash.", UsedBy("orbit", "orbitLeader", "zigzag"));
        yield return Number("move.farFactor", "Far factor", 1.6, 1, 20, 0.1, "orbit: \"far\" starts at distance x this value.", UsedBy("orbit", "orbitLeader"));
        yield return Number("move.lead", "Lead (s)", 0, 0, 5, 0.05, "approach: seconds of the player's velocity the unit aims ahead (0 = at the player).", UsedBy("approach"));
        yield return Number("move.interval", "Interval (s)", 5, 0.5, 120, 0.5, "wander: seconds between two destinations.", UsedBy("wander"));
        yield return new FieldSpec("move.spin", "Spin (1 right, -1 left, 0 random)", FieldKind.Int)
        {
            Min = -1, Max = 1, Help = "orbit: 1 goes around clockwise, -1 counter-clockwise, 0 picks at random for each unit.",
            EnabledWhen = UsedBy("orbit", "orbitLeader")
        };
        yield return new FieldSpec("move.face", "Face the player (hold)", FieldKind.Bool)
        {
            DefaultBool = true, Help = "hold: the unit turns to look at the player (a turret).",
            EnabledWhen = UsedBy("hold")
        };
        yield return new FieldSpec("move.builtin", "Built-in flight (builtin)", FieldKind.Choice)
        {
            Choices = () => AiCatalog.BuiltinFlights,
            AllowEmpty = true,
            Help = "builtin: which flight of the server runs. Empty = the \"behavior\" of the unit type.",
            EnabledWhen = UsedBy("builtin")
        };
        yield return Number("move.speedMult", "Speed multiplier (builtin)", 1, 0.1, 5, 0.1,
            "builtin: multiplies the speed of the unit (1.2 = a fifth faster than the unit type says).", UsedBy("builtin"));
        yield return new FieldSpec("move.around", "Circle around", FieldKind.Choice)
        {
            Choices = () => new[] { "player", "summoner", "leader", "home" },
            AllowEmpty = true,
            Help = "orbit: who the circle is around. Empty = the player.",
            EnabledWhen = UsedBy("orbit")
        };
        yield return new FieldSpec("move.lock", "Lock to the circle", FieldKind.Bool)
        {
            Help = "orbit: stay on a fixed place of the circle and turn with the summoner, instead of chasing a point ahead.",
            EnabledWhen = UsedBy("orbit")
        };
        yield return new FieldSpec("move.faceForward", "Face the way it circles", FieldKind.Bool)
        {
            Help = "orbit with lock: face along the circle, the way the center turns.",
            EnabledWhen = UsedBy("orbit")
        };
        yield return Number("move.spinRate", "Spin (degrees/s)", 0, -180, 180, 1,
            "How fast the body turns by itself. On hold, the unit stays put and spins. On a path, the body spins while it slides.",
            UsedBy("hold", "path", "orbit"));
        yield return Number("move.topSpeed", "Top speed (units/s)", 0, 0, 40, 0.5,
            "Replaces the unit's own top speed while this state lasts. 0 = the unit's speed. A unit the game gives speed 0 needs this to move.");
        yield return Number("move.turnMult", "Turn multiplier", 1, 0.1, 20, 0.1, "Multiplies how fast the unit may turn (1 = as the unit says).");
        yield return Number("move.jitter", "Dash angle random", 0, 0, 90, 1, "zigzag: the dash angle varies by up to this many degrees.", UsedBy("zigzag"));
        yield return Number("move.pause", "Pause between dashes (s)", 0, 0, 5, 0.1, "zigzag: it stops and faces the player between dashes.", UsedBy("zigzag"));
        yield return new FieldSpec("move.loop", "Loop the path", FieldKind.Bool)
        {
            DefaultBool = true, Help = "path: off = stop at the last point.",
            EnabledWhen = UsedBy("path")
        };
        yield return new FieldSpec("move.points", "Path points (x y)", FieldKind.MultiText)
        {
            Help = "path: one point per line, \"x y\", relative to where the unit was born. The unit slides from point to point.",
            EnabledWhen = UsedBy("path"),
            Reader = ReadPathPoints,
            Writer = WritePathPoints
        };

        yield return FieldSpec.Header("Weapons");
        yield return new FieldSpec("fire.enabled", "Shoots", FieldKind.Bool) { DefaultBool = true, Help = "Off = the unit does not shoot in this state." };
        yield return new FieldSpec("fire.guns", "Weapons (mounts)", FieldKind.GunMounts)
        {
            Guns = () => AiUnits.CurrentGuns,
            Help = "The gun mounts of the unit in the simulator. Checked = shoots in this state. All checked = every mount of the unit. Click a name to see its data."
        };
        yield return Number("fire.rateMult", "Fire rate multiplier", 1, 0, 20, 0.1, "2 = twice as often.");
        yield return Number("fire.range", "Shooting range", 0, 0, 500, 1, "Distance at which the unit shoots. 0 = the range of the gun.");
        yield return new FieldSpec("fire.aimLead", "Aim lead", FieldKind.OptionalNumber)
        {
            Help = "How far ahead of a moving player the unit aims (0 = at the player). Empty = the value of the unit type."
        };

        yield return FieldSpec.Header("Contact");
        yield return Number("contact.explodeRadius", "Explodes within", 0, 0, 50, 0.1,
            "The unit blows up (no XP, no loot) when the player gets this close. 0 = never.");
    }

    // What a state summons, and the look it turns on. The shield is AiFields.Shield, its own tab.
    public static IEnumerable<FieldSpec> Summons(Func<string> summonUnit)
    {
        yield return FieldSpec.Header("Summons");
        yield return new FieldSpec("summon", "Unit", FieldKind.UnitTree)
        {
            OnChanged = (_, _, _) => { },
            Help = "The unit this state calls. The list is the same as the Enemy AI unit picker: dungeons, world biomes, generic types, and units with no origin. Empty = no summons."
        };
        yield return new FieldSpec("summonAi", "AI", FieldKind.Choice)
        {
            Choices = () => UnitLists.Applicable(summonUnit()).Select(row => row.Id),
            ChoiceLabel = id => UnitLists.AiText(summonUnit(), id),
            Help = "AI the summoned units run. The list is the AIs made for this unit, then the generic ones. Empty = the AI of the unit type. Gray when this line summons nothing.",
            EnabledWhen = SummonLive
        };
        yield return new FieldSpec("summonRole", "Role", FieldKind.EditableChoice)
        {
            Choices = UnitLists.Roles,
            Help = "Role name, so a summonsAtMost condition or a formation link can count only this kind. The list is the roles used by formations; you can type a new one. Gray when this line summons nothing.",
            EnabledWhen = SummonLive
        };
        yield return new FieldSpec("summonPlace", "Place", FieldKind.EditableChoice)
        {
            Choices = () => UnitLists.Places(),
            Help = "behind, front, left, right, ring or around. A formation point is point:formation/id and sits there (x to the right, y in front of this unit). Empty = behind. Gray when this line summons nothing.",
            EnabledWhen = SummonLive
        };
        yield return new FieldSpec("summonCount", "Amount each time", FieldKind.Int)
        {
            Min = 0, Max = 30, Default = 1,
            Help = "How many units appear each time. 0 summons nothing, and the other fields of this line stay gray. 1 to 30 summon that many."
        };
        yield return Number("summonEvery", "Again every (s)", 0, 0, 600, 0.5,
            "0 = only when the state starts. Otherwise it summons again every this many seconds while the state lasts. Gray when this line summons nothing.",
            SummonLive);
        yield return new FieldSpec("summonMax", "At most alive", FieldKind.Int)
        {
            Min = 0, Max = 100,
            Help = "No new summons of this line while this many of them are alive. 0 = no limit. Gray when this line summons nothing.",
            EnabledWhen = SummonLive
        };
        yield return Number("summonDistance", "Summon distance", 5, 0, 80, 0.5,
            "How far from the summoner they appear. 0 = 5. Gray for a formation point (that place ignores distance) and when this line summons nothing.",
            SummonDistance);
        yield return Number("summonAngle", "Summon angle", 0, -360, 360, 5,
            "Where a ring or around starts, in degrees. Gray for every other place, and when this line summons nothing.",
            SummonAngle);

        yield return FieldSpec.Header("Look and weapons");
        yield return new FieldSpec("effect", "Visual effect from here on", FieldKind.Choice)
        {
            Choices = Editor.EditorAssets.EffectChoices,
            Help = "Name of a client effect (statefulEffects, e.g. detectionZone_1) the unit shows from this state on. Empty = keeps the one it has."
        };
        yield return new FieldSpec("homing", "Homing bullets", FieldKind.Choice)
        {
            Choices = () => new[] { "on", "off" },
            AllowEmpty = true,
            Help = "on / off: turns the homing bullets on or off from this state on. Empty = keeps what the unit has."
        };
    }

    // The shield of a state. The look is the same list the formation editor uses.
    public static IEnumerable<FieldSpec> Shield()
    {
        yield return new FieldSpec("shield", "Shield", FieldKind.Bool) { Help = "The unit ignores all damage while the shield is up." };
        yield return Number("shieldSeconds", "Shield lasts (s)", 0, 0, 600, 0.5,
            "0 = the whole time the unit is in this state. Gray while Shield is off.", ShieldOn);
        yield return new FieldSpec("shieldEffect", "Shield look", FieldKind.Choice)
        {
            Choices = Editor.EditorAssets.EffectChoices,
            Help = "Name of a client effect shown while the shield is up. Empty = no bubble, the unit still takes no damage. tankShield is the player tank's bubble: leave it for that plane. Gray while Shield is off.",
            EnabledWhen = ShieldOn
        };
    }

    public static IEnumerable<FieldSpec> BrainInfo(Func<IEnumerable<string>> stateIds)
    {
        foreach (FieldSpec spec in Info()) yield return spec;
        yield return new FieldSpec("units", "Made for (units)", FieldKind.StringList)
        {
            Help = "Names of the game units this AI was made for (the \"template\" of the unit type, such as swarmboss). The AI is offered only to them. Ignored for an AI in the generic folder: it works for any unit."
        };
        yield return new FieldSpec("initial", "First state", FieldKind.Choice)
        {
            Choices = stateIds,
            AllowEmpty = false,
            Help = "The state the unit is in when it is born."
        };
    }

    public static IEnumerable<FieldSpec> State(Func<IEnumerable<string>> behaviorIds)
    {
        yield return new FieldSpec("id", "State name", FieldKind.Text) { Help = "Name of the state. Transitions and the \"first state\" refer to it." };
        yield return new FieldSpec("behavior", "Behavior (file)", FieldKind.Choice)
        {
            Choices = behaviorIds,
            AllowEmpty = true,
            Help = "The behavior the state acts like: one that lives inside this AI (made with \"New behavior...\") or a file of the behaviors folder. Empty = the state has its own values (the \"Behavior values\" tab)."
        };
        yield return new FieldSpec("selfDestruct", "Blows up when it starts", FieldKind.Bool)
        {
            Help = "The unit explodes (no XP, no loot) as soon as it enters this state: a fuse."
        };
        yield return new FieldSpec("message", "Chat message", FieldKind.Text) { Help = "Shown in the chat when the state starts." };
        yield return new FieldSpec("bark", "Speech (bark key)", FieldKind.Text) { Help = "Key of a speech line of the client, said by the unit when the state starts." };
    }

    public static IEnumerable<FieldSpec> Transition(Func<IEnumerable<string>> stateIds)
    {
        yield return new FieldSpec("to", "Go to state", FieldKind.Choice)
        {
            Choices = stateIds,
            AllowEmpty = false,
            Help = "The state the unit changes to when every condition below is true. Transitions are checked in order; the first that matches wins."
        };
    }

    public static IEnumerable<FieldSpec> Condition()
    {
        yield return new FieldSpec("type", "Condition", FieldKind.Choice)
        {
            Choices = () => AiCatalog.ConditionTypes.Select(o => o.Id),
            AllowEmpty = false,
            Help = string.Join("\n", AiCatalog.ConditionTypes.Select(o => o.Id + ": " + L.T(o.Help)))
        };
        yield return new FieldSpec("value", "Value", FieldKind.Number) { Increment = 0.5, Help = "Seconds, distance, a fraction or degrees, depending on the condition." };
        yield return new FieldSpec("max", "Up to (random)", FieldKind.OptionalNumber)
        {
            Help = "timeInState only: the time is picked at random between value and this number, each time the state starts."
        };
        yield return new FieldSpec("not", "Not (the opposite)", FieldKind.Bool) { Help = "The condition is true when it is NOT met." };
        yield return new FieldSpec("unit", "Count only this unit", FieldKind.Text)
        {
            Help = "summonsAtMost only: count summons of this unit name. Empty = every unit."
        };
        yield return new FieldSpec("role", "Count only this role", FieldKind.Text)
        {
            Help = "summonsAtMost only: count summons with this role. Empty = every role."
        };
    }

    private static FieldSpec Number(string key, string label, double def, double min, double max, double step, string help, Func<JsonObject, bool>? enabledWhen = null)
    {
        return new FieldSpec(key, label, FieldKind.Number) { Default = def, Min = min, Max = max, Increment = step, Help = help, EnabledWhen = enabledWhen };
    }

    // The movement mode of a behavior (or of a state with its own values); an empty one is "orbit", like the file reader.
    private static string ModeOf(JsonObject data)
    {
        string mode = Json.Str(Json.ObjOrNull(data, "move"), "mode");
        return mode.Length > 0 ? mode : "orbit";
    }

    // A movement field is active only for the modes that read it (the list is the "Uses:" of each mode in AiCatalog.MoveModes);
    // for the others it is greyed out.
    private static Func<JsonObject, bool> UsedBy(params string[] modes) => data => modes.Contains(ModeOf(data), StringComparer.Ordinal);

    // A summon line does nothing without a unit, or with amount 0 (the runtime skips that line).
    private static bool SummonLive(JsonObject data)
    {
        if (Json.Str(data, "summon").Length == 0) return false;
        return data["summonCount"] == null || Json.Int(data, "summonCount", 1) > 0;
    }

    private static string SummonPlaceOf(JsonObject data)
    {
        string place = Json.Str(data, "summonPlace").Trim();
        return place.Length == 0 ? "behind" : place;
    }

    private static bool SummonDistance(JsonObject data)
    {
        if (!SummonLive(data)) return false;
        return !SummonPlaceOf(data).StartsWith("point:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SummonAngle(JsonObject data)
    {
        if (!SummonLive(data)) return false;
        string place = SummonPlaceOf(data);
        return place.Equals("ring", StringComparison.OrdinalIgnoreCase) || place.Equals("around", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShieldOn(JsonObject data) => Json.Bool(data, "shield");

    // ------------------------------------------------------------------ list texts

    public static string DescribeState(JsonObject state)
    {
        string id = Json.Str(state, "id", "?");
        string behavior = Json.Str(state, "behavior");
        string what = behavior.Length > 0 ? behavior : L.T("behavior values") + ": " + Json.Str(Json.ObjOrNull(state, "move"), "mode", "?");
        int transitions = Json.ArrOrNull(state, "transitions")?.Count ?? 0;
        string extras = (Json.Str(state, "summon").Length > 0 ? "  +" + L.T("summons") : "") + (Json.Bool(state, "shield") ? "  +" + L.T("shield") : "");
        return id + "   [" + what + "]" + extras + "   " + transitions + " " + L.T("transition(s)");
    }

    public static string DescribeTransition(JsonObject transition)
    {
        string to = Json.Str(transition, "to", "?");
        List<JsonObject> conditions = Json.Objects(Json.ArrOrNull(transition, "when")).ToList();
        string when = conditions.Count == 0 ? L.T("always") : string.Join(" " + L.T("and") + " ", conditions.Select(DescribeCondition));
        return "→ " + to + "   " + L.T("when") + " " + when;
    }

    public static string DescribeCondition(JsonObject condition)
    {
        string type = Json.Str(condition, "type", "always");
        AiOption? option = AiCatalog.ConditionTypes.FirstOrDefault(o => o.Id == type);
        string label = option == null ? type : L.T(option.Label);
        string text = label;
        if (type != "always" && type != "hasTarget")
        {
            text += " " + Json.Num(condition, "value").ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            if (type == "timeInState" && Json.Has(condition, "max"))
                text += "–" + Json.Num(condition, "max").ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }
        return Json.Bool(condition, "not") ? L.T("not") + " (" + text + ")" : text;
    }

    // ------------------------------------------------------------------ new things

    public static JsonObject NewState(IEnumerable<string> existing)
    {
        var used = new HashSet<string>(existing, StringComparer.Ordinal);
        string id = "state";
        for (int n = 1; used.Contains(id); n++) id = "state_" + n;
        return new JsonObject
        {
            ["id"] = id,
            ["move"] = new JsonObject { ["mode"] = "orbit", ["distance"] = 14, ["speed"] = 0.7 },
            ["fire"] = new JsonObject { ["enabled"] = true }
        };
    }

    public static JsonObject NewTransition(string to)
    {
        return new JsonObject
        {
            ["to"] = to,
            ["when"] = new JsonArray(new JsonObject { ["type"] = "timeInState", ["value"] = 3 })
        };
    }

    public static JsonObject NewCondition()
    {
        return new JsonObject { ["type"] = "timeInState", ["value"] = 3 };
    }

    public static JsonObject NewBehavior(string name)
    {
        return new JsonObject
        {
            ["name"] = name,
            ["author"] = "",
            ["description"] = "",
            ["move"] = new JsonObject { ["mode"] = "orbit", ["distance"] = 14, ["deviation"] = 30, ["speed"] = 0.7, ["farSpeed"] = 1 },
            ["fire"] = new JsonObject { ["enabled"] = true }
        };
    }

    public static JsonObject NewBrain(string name)
    {
        return new JsonObject
        {
            ["name"] = name,
            ["author"] = "",
            ["description"] = "",
            ["initial"] = "wait",
            ["states"] = new JsonArray(
                new JsonObject
                {
                    ["id"] = "wait",
                    ["move"] = new JsonObject { ["mode"] = "wander", ["distance"] = 10, ["interval"] = 5, ["speed"] = 0.45 },
                    ["fire"] = new JsonObject { ["enabled"] = false },
                    ["transitions"] = new JsonArray(new JsonObject
                    {
                        ["to"] = "attack",
                        ["when"] = new JsonArray(new JsonObject { ["type"] = "playerWithin", ["value"] = 60 })
                    })
                },
                new JsonObject
                {
                    ["id"] = "attack",
                    ["move"] = new JsonObject { ["mode"] = "orbit", ["distance"] = 14, ["deviation"] = 30, ["speed"] = 0.7, ["farSpeed"] = 1 },
                    ["fire"] = new JsonObject { ["enabled"] = true },
                    ["transitions"] = new JsonArray(new JsonObject
                    {
                        ["to"] = "wait",
                        ["when"] = new JsonArray(new JsonObject { ["type"] = "hasTarget", ["not"] = true })
                    })
                })
        };
    }

    private static string ReadPathPoints(JsonObject state)
    {
        if (state["move"] is not JsonObject move || move["points"] is not JsonArray points) return "";
        var lines = new List<string>();
        foreach (JsonNode? node in points)
        {
            if (node is JsonObject point)
                lines.Add(point["x"]?.ToString() + " " + point["y"]?.ToString());
            else if (node is JsonArray pair && pair.Count >= 2)
                lines.Add(pair[0]?.ToString() + " " + pair[1]?.ToString());
        }
        return string.Join("\r\n", lines);
    }

    private static void WritePathPoints(JsonObject state, string text)
    {
        JsonObject move = state["move"] as JsonObject ?? new JsonObject();
        state["move"] = move;
        var points = new JsonArray();
        foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split(new[] { ' ', ',', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) continue;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) continue;
            points.Add(new JsonObject { ["x"] = x, ["y"] = y });
        }
        if (points.Count == 0) move.Remove("points");
        else move["points"] = points;
    }
}
