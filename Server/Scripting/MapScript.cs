using System.Text.Json;

// Script of a map ("script" section of maps\*.json): what happens in the level, as data.
//
// A script is a list of RULES. A rule has a condition ("when") and a list of actions ("do"); it fires once, the first time its
// condition is true (no "when" = at the start). Units that appear by themselves are SPAWNS. The same engine runs the walker
// tutorial and any dungeon whose map has a "script" (MapScriptRunner). Format: docs/map-script.md.
//
// Everything refers to things of the same map by name: points, groups of points, areas, lines, gates and checkpoints.
// Validate() checks every name when the map is read, so a typo is an error in the log and not a script that stops silently.
public static class MapScript
{
    // ================================================================== file (JSON)

    public sealed class ScriptDef
    {
        // Portrait (client speaker image) by unit alias or group: {"instructor": "sergeant", "boss": "bosscat"}.
        public Dictionary<string, string>? Speakers { get; set; }
        // Names of the stages (for the log and the /step command): the "stage" action picks one by number.
        public List<string>? Stages { get; set; }
        public List<SpawnDef>? Spawns { get; set; }
        public List<RuleDef>? Rules { get; set; }
        // Jumps ("/step N" of the tutorial): what is skipped, by number.
        public Dictionary<string, StepDef>? Steps { get; set; }
        // Id of the rule that means "the objective is done" (the tutorial lets the player leave after it).
        public string CompleteWhen { get; set; } = "";
        public string Note { get; set; } = "";
    }

    // Units that appear. "when": "start" (default, when the script starts) or "manual" (only by a "spawn" action).
    public sealed class SpawnDef
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        // Alias of the unit (single placements): other parts of the script talk about it as "unit": speech, "dead", guide.
        public string Id { get; set; } = "";
        public string Group { get; set; } = "";
        // Where: one point, every point of a group, a ring of units around a point, or where a unit last was.
        public string Point { get; set; } = "";
        public string Points { get; set; } = "";
        public EllipseDef? Ellipse { get; set; }
        public string AtUnit { get; set; } = "";
        public string Area { get; set; } = "";
        // Waits in place, idle, until the script wakes the group, a player gets within wakeRadius (0 = never) or one of them is hit.
        public bool Asleep { get; set; }
        public float WakeRadius { get; set; }
        public bool? Invulnerable { get; set; }
        public float? Rot { get; set; }
        public string When { get; set; } = "start";
        public string Note { get; set; } = "";
    }

    // Units in an ellipse around a point (the ring of gas drones): "opening" degrees without a unit, centered on startDegrees + 180.
    public sealed class EllipseDef
    {
        public string Center { get; set; } = "";
        public int Count { get; set; } = 8;
        public float Opening { get; set; }
        public float RadiusU { get; set; } = 6f;
        public float RadiusV { get; set; } = 6f;
        public float StartDegrees { get; set; } = 270f;
        public bool FaceCenter { get; set; } = true;
    }

    public sealed class RuleDef
    {
        public string Id { get; set; } = "";
        public CondDef? When { get; set; }
        public List<ActionDef>? Do { get; set; }
        public string Note { get; set; } = "";
    }

    // Condition. Every field that is set must be true (AND). "any" is an OR of other conditions, "all" an AND, "not" the opposite.
    public sealed class CondDef
    {
        // The furthest live player is at or past lines[name] (advance axis b).
        public string Line { get; set; } = "";
        // The rule has fired (with afterSeconds: that long ago).
        public string Done { get; set; } = "";
        public float AfterSeconds { get; set; }
        // The rule has NOT fired.
        public string Unless { get; set; } = "";
        public string GateOpen { get; set; } = "";
        // The gate is unlocked, closed, and a player is within "distance" tiles of it.
        public GateNearDef? GateNear { get; set; }
        // The group was spawned and nothing of it is alive (a unit that could not be created counts as gone).
        public string Cleared { get; set; } = "";
        public string Alive { get; set; } = "";
        public AliveAtMostDef? AliveAtMost { get; set; }
        // The unit (alias) was created and is gone / could not be created.
        public string Dead { get; set; } = "";
        public string Failed { get; set; } = "";
        // Somebody in the group is awake (or nobody is waiting any more).
        public string Awake { get; set; } = "";
        public WithinDef? Within { get; set; }
        // Dungeon maps: a room of "encounters" is cleared / a unit type of "types" has died.
        public string RoomCleared { get; set; } = "";
        public string TypeDead { get; set; } = "";
        public List<CondDef>? Any { get; set; }
        public List<CondDef>? All { get; set; }
        public CondDef? Not { get; set; }
    }

    public sealed class GateNearDef
    {
        public string Gate { get; set; } = "";
        public float Distance { get; set; } = 1f;
    }

    public sealed class AliveAtMostDef
    {
        public string Group { get; set; } = "";
        public int Count { get; set; }
    }

    public sealed class WithinDef
    {
        public string Point { get; set; } = "";
        public float Radius { get; set; }
    }

    // Action: one verb per action (the first one found, in the order of the properties below). "delay" waits that many seconds;
    // "if" and "unless" are checked when the action runs (after the delay).
    public sealed class ActionDef
    {
        public float Delay { get; set; }
        public CondDef? If { get; set; }
        public string Unless { get; set; } = "";

        // say: speech key of the client (Texts/bark) said by a unit ("by": alias or group; "byGate": a unit of that gate).
        public string Say { get; set; } = "";
        public string By { get; set; } = "";
        public string ByGate { get; set; } = "";
        public string Portrait { get; set; } = "";
        // "dialog" (default: portrait, bubble and chat line) or "bubble" (bubble only).
        public string Mode { get; set; } = "";
        public string Chat { get; set; } = "";
        // guide: the guide unit ("who": alias or group, default "instructor") flies to the point for "seconds", or until the rule "until" fires.
        public string Guide { get; set; } = "";
        public string Who { get; set; } = "";
        public float Seconds { get; set; }
        public string Until { get; set; } = "";
        // wake: wakes the group (with "stagger": one unit every that many seconds, in the order they were made).
        public string Wake { get; set; } = "";
        public float Stagger { get; set; }
        // quiet: the group goes back to waiting, idle.
        public string Quiet { get; set; } = "";
        // area: the room the AI flies in from now on.
        public string Area { get; set; } = "";
        public int? Stage { get; set; }
        public string Unlock { get; set; } = "";
        public string Open { get; set; } = "";
        public string Spawn { get; set; } = "";
        public string Remove { get; set; } = "";
        // Open / remove: the units blow up (true) or just disappear (false, default).
        public bool Explode { get; set; }
        // portal: the exit carrier to the Nexus at the point; the server takes the player there by itself after "autoExitSeconds".
        public string Portal { get; set; } = "";
        public float AutoExitSeconds { get; set; }
        public string Log { get; set; } = "";
    }

    // What "/step N" does: marks rules as done without running them, runs others, removes units, opens gates, takes the plane.
    public sealed class StepDef
    {
        public string Checkpoint { get; set; } = "";
        public List<string>? Done { get; set; }
        public List<string>? Fire { get; set; }
        public List<string>? Quiet { get; set; }
        public List<string>? Remove { get; set; }
        public List<string>? Open { get; set; }
    }

    // ================================================================== validation

    // Checks every name the script uses against the map. Returns the first problem or null.
    public static string? Validate(Maps.Map map)
    {
        ScriptDef? s = map.Script;
        if (s == null) return null;

        var rules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RuleDef r in s.Rules ?? new List<RuleDef>())
        {
            if (string.IsNullOrWhiteSpace(r.Id)) return "a rule has no \"id\"";
            if (!rules.Add(r.Id)) return "two rules have the id '" + r.Id + "'";
        }
        var spawnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gates = new HashSet<string>(map.Gates.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);

        foreach (SpawnDef sp in s.Spawns ?? new List<SpawnDef>())
        {
            string who = "spawn '" + (sp.Name.Length > 0 ? sp.Name : sp.Type) + "'";
            if (string.IsNullOrWhiteSpace(sp.Type)) return who + ": no \"type\"";
            if (sp.Name.Length > 0 && !spawnNames.Add(sp.Name)) return "two spawns are named '" + sp.Name + "'";
            if (sp.Group.Length > 0) groups.Add(sp.Group);
            if (sp.Id.Length > 0) aliases.Add(sp.Id);
            string? error = CheckPlace(map, sp, who);
            if (error != null) return error;
            if (sp.Area.Length > 0 && !map.Areas.ContainsKey(sp.Area)) return who + ": there is no area '" + sp.Area + "'";
            if (!string.Equals(sp.When, "start", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(sp.When, "manual", StringComparison.OrdinalIgnoreCase))
                return who + ": \"when\" must be \"start\" or \"manual\"";
        }

        foreach (RuleDef r in s.Rules ?? new List<RuleDef>())
        {
            string where = "rule '" + r.Id + "'";
            string? error = r.When != null ? CheckCond(map, r.When, rules, groups, aliases, gates, where) : null;
            if (error != null) return error;
            foreach (ActionDef a in r.Do ?? new List<ActionDef>())
            {
                error = CheckAction(map, s, a, rules, spawnNames, groups, aliases, gates, where);
                if (error != null) return error;
            }
        }

        if (s.CompleteWhen.Length > 0 && !rules.Contains(s.CompleteWhen)) return "\"completeWhen\": there is no rule '" + s.CompleteWhen + "'";
        foreach (KeyValuePair<string, StepDef> pair in s.Steps ?? new Dictionary<string, StepDef>())
        {
            StepDef st = pair.Value;
            string where = "step " + pair.Key;
            if (!int.TryParse(pair.Key, out _)) return where + ": the key must be a number";
            if (st.Checkpoint.Length > 0 && !map.Checkpoints.Any(c => string.Equals(c.Name, st.Checkpoint, StringComparison.OrdinalIgnoreCase)))
                return where + ": there is no checkpoint '" + st.Checkpoint + "'";
            foreach (string id in (st.Done ?? new List<string>()).Concat(st.Fire ?? new List<string>()))
                if (!rules.Contains(id)) return where + ": there is no rule '" + id + "'";
            foreach (string g in (st.Open ?? new List<string>()))
                if (!gates.Contains(g)) return where + ": there is no gate '" + g + "'";
        }
        return null;
    }

    private static string? CheckPlace(Maps.Map map, SpawnDef sp, string who)
    {
        int places = (sp.Point.Length > 0 ? 1 : 0) + (sp.Points.Length > 0 ? 1 : 0) + (sp.Ellipse != null ? 1 : 0);
        if (sp.AtUnit.Length > 0 && sp.Point.Length == 0) return who + ": \"atUnit\" needs a \"point\" for when the unit was never there";
        if (places == 0) return who + ": needs \"point\", \"points\" or \"ellipse\"";
        if (places > 1) return who + ": use only one of \"point\", \"points\" and \"ellipse\"";
        if (sp.Point.Length > 0 && !map.Points.ContainsKey(sp.Point)) return who + ": there is no point '" + sp.Point + "'";
        if (sp.Points.Length > 0 && !map.Groups.ContainsKey(sp.Points)) return who + ": there is no group of points '" + sp.Points + "'";
        if (sp.Ellipse != null && !map.Points.ContainsKey(sp.Ellipse.Center)) return who + ": ellipse center: there is no point '" + sp.Ellipse.Center + "'";
        return null;
    }

    private static string? CheckCond(Maps.Map map, CondDef c, HashSet<string> rules, HashSet<string> groups, HashSet<string> aliases,
        HashSet<string> gates, string where)
    {
        if (c.Line.Length > 0 && !map.Lines.ContainsKey(c.Line)) return where + ": there is no line '" + c.Line + "'";
        if (c.Done.Length > 0 && !rules.Contains(c.Done)) return where + ": there is no rule '" + c.Done + "'";
        if (c.Unless.Length > 0 && !rules.Contains(c.Unless)) return where + ": there is no rule '" + c.Unless + "'";
        if (c.GateOpen.Length > 0 && !gates.Contains(c.GateOpen)) return where + ": there is no gate '" + c.GateOpen + "'";
        if (c.GateNear != null && !gates.Contains(c.GateNear.Gate)) return where + ": there is no gate '" + c.GateNear.Gate + "'";
        foreach (string g in new[] { c.Cleared, c.Alive, c.Awake, c.AliveAtMost?.Group ?? "" })
            if (g.Length > 0 && !groups.Contains(g)) return where + ": no spawn makes the group '" + g + "'";
        foreach (string a in new[] { c.Dead, c.Failed })
            if (a.Length > 0 && !aliases.Contains(a)) return where + ": no spawn has the id '" + a + "'";
        if (c.Within != null && !map.Points.ContainsKey(c.Within.Point)) return where + ": there is no point '" + c.Within.Point + "'";
        foreach (CondDef sub in (c.Any ?? new List<CondDef>()).Concat(c.All ?? new List<CondDef>()).Concat(c.Not != null ? new[] { c.Not } : Array.Empty<CondDef>()))
        {
            string? error = CheckCond(map, sub, rules, groups, aliases, gates, where);
            if (error != null) return error;
        }
        return null;
    }

    private static string? CheckAction(Maps.Map map, ScriptDef s, ActionDef a, HashSet<string> rules, HashSet<string> spawns,
        HashSet<string> groups, HashSet<string> aliases, HashSet<string> gates, string where)
    {
        if (a.If != null)
        {
            string? error = CheckCond(map, a.If, rules, groups, aliases, gates, where);
            if (error != null) return error;
        }
        if (a.Unless.Length > 0 && !rules.Contains(a.Unless)) return where + ": there is no rule '" + a.Unless + "'";
        if (a.Guide.Length > 0 && !map.Points.ContainsKey(a.Guide)) return where + ": guide: there is no point '" + a.Guide + "'";
        if (a.Until.Length > 0 && !rules.Contains(a.Until)) return where + ": there is no rule '" + a.Until + "'";
        if (a.Portal.Length > 0 && !map.Points.ContainsKey(a.Portal)) return where + ": portal: there is no point '" + a.Portal + "'";
        if (a.Area.Length > 0 && !map.Areas.ContainsKey(a.Area)) return where + ": there is no area '" + a.Area + "'";
        if (a.Spawn.Length > 0 && !spawns.Contains(a.Spawn)) return where + ": there is no spawn named '" + a.Spawn + "'";
        foreach (string g in new[] { a.Wake, a.Quiet, a.Remove })
            if (g.Length > 0 && !groups.Contains(g)) return where + ": no spawn makes the group '" + g + "'";
        foreach (string gate in new[] { a.Unlock, a.Open, a.ByGate })
            if (gate.Length > 0 && !gates.Contains(gate)) return where + ": there is no gate '" + gate + "'";
        if (a.Stage.HasValue && (s.Stages == null || a.Stage.Value < 0 || a.Stage.Value >= s.Stages.Count))
            return where + ": stage " + a.Stage.Value + " is not in \"stages\"";
        if (a.Say.Length > 0 && a.By.Length > 0 && !groups.Contains(a.By) && !aliases.Contains(a.By)) return where + ": say: nothing is called '" + a.By + "'";
        return null;
    }

    // ================================================================== reading

    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
