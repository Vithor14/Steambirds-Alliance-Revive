using System.Globalization;
using Lidgren.Network;

// What the script engine needs from whoever runs it (the tutorial session, a dungeon instance). Everything about units goes
// straight to Enemies; the host only knows about things that are its own: gates, speech, the exit carrier, the players.
public interface IScriptHost
{
    NetServer Server { get; }
    // The connection the units of the script belong to (the first player of an instance).
    NetConnection Connection { get; }
    string Role { get; }
    Maps.Map Map { get; }

    void Log(string text);
    void Chat(string text);
    // World positions of the players with a plane in the air.
    void LivePlayers(List<(float X, float Y)> into);
    // Creates one unit (a, b in tiles; rotation in degrees, NaN = facing the player). Returns the shortId or 0.
    int CreateUnit(string type, float a, float b, float rotation, string group, Ground.Rect? area, bool? invulnerable, double now);
    void Speak(int shortId, string key, string portrait, bool dialog);

    bool GateOpen(string gate);
    bool GateDamageable(string gate);
    // Advance position of the gate (b, in tiles).
    float GateB(string gate);
    void GateUnlock(string gate, double now);
    void GateOpenNow(string gate, bool explode, double now);
    // A live unit of the gate that can say a line (0 = none).
    int GateSpeaker(string gate);

    void ExitPortal(string point, float autoExitSeconds, double now);
    bool RoomCleared(string room);
    bool TypeDead(string type);
}

// Runs the "script" of a map (MapScript) for one session. One instance per tutorial session or dungeon instance; it keeps
// everything the script has done, so a restart is just a new runner. Called from the host's tick, under Game.Lock.
public sealed partial class MapScriptRunner
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Random rng = new Random();

    private sealed class Pending
    {
        public double When;
        public Action<double> Act = _ => { };
    }

    private readonly IScriptHost host;
    private readonly Maps.Map map;
    private readonly MapScript.ScriptDef def;
    private readonly Dictionary<string, double> done = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    private readonly List<Pending> pending = new List<Pending>();

    // Units made by the script: ids by group, alias -> id, where each alias was last seen, what was attempted.
    private readonly Dictionary<string, List<int>> groupIds = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> aliasIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (float X, float Y)> lastSeen = new Dictionary<string, (float X, float Y)>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> attemptedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> failedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> spawnedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // The guide unit (the tutorial's instructor) goes to a point for a while, or until a rule fires.
    private string guideWho = "instructor";
    private bool guideSet;
    private float guideX;
    private float guideY;
    private double guideUntil;
    private string guideUntilRule = "";

    private readonly List<(float X, float Y)> players = new List<(float X, float Y)>();
    private float playersMaxB;
    private double now;

    public bool Started { get; private set; }
    public int Stage { get; private set; }

    public MapScriptRunner(IScriptHost host)
    {
        this.host = host;
        map = host.Map;
        def = map.Script ?? new MapScript.ScriptDef();
    }

    public bool HasScript => map.Script != null;
    public MapScript.ScriptDef Definition => def;

    public bool IsDone(string rule) => rule.Length > 0 && done.ContainsKey(rule);

    // True when the rule named by "completeWhen" has fired (the objective of the map is done).
    public bool Completed => def.CompleteWhen.Length > 0 && IsDone(def.CompleteWhen);

    public string StageName()
    {
        List<string>? stages = def.Stages;
        return stages != null && Stage >= 0 && Stage < stages.Count ? stages[Stage] : "?";
    }

    // ================================================================== start and tick

    // The script begins: the units marked "start" appear (waiting or not) and the rules start to be checked.
    public void Start(double time)
    {
        now = time;
        Started = true;
        int created = 0;
        foreach (MapScript.SpawnDef sp in def.Spawns ?? new List<MapScript.SpawnDef>())
        {
            if (string.Equals(sp.When, "manual", StringComparison.OrdinalIgnoreCase)) continue;
            created += RunSpawn(sp, time);
        }
        host.Log("[script] started: " + (def.Rules?.Count ?? 0) + " rule(s), " + created + " unit(s) created");
    }

    public void Update(double time)
    {
        now = time;
        RunPending(time);
        if (!Started) return;

        host.LivePlayers(players);
        playersMaxB = float.MinValue;
        foreach ((float x, float y) in players)
        {
            Ground.ToGrid(x, y, out _, out float b);
            if (b > playersMaxB) playersMaxB = b;
        }

        foreach (MapScript.RuleDef rule in def.Rules ?? new List<MapScript.RuleDef>())
        {
            if (done.ContainsKey(rule.Id)) continue;
            if (rule.When != null && !Eval(rule.When)) continue;
            FireRule(rule, time);
        }

        TrackAliases();
        UpdateGuide(time);
    }

    // Marks the rule as done and runs its actions (the ones with a delay wait their turn).
    private void FireRule(MapScript.RuleDef rule, double time)
    {
        done[rule.Id] = time;
        foreach (MapScript.ActionDef action in rule.Do ?? new List<MapScript.ActionDef>())
        {
            MapScript.ActionDef a = action;
            if (a.Delay > 0f) pending.Add(new Pending { When = time + a.Delay, Act = t => Execute(a, t) });
            else Execute(a, time);
        }
    }

    // A rule runs by hand (the "/step" jumps): ignored if it already fired.
    public bool Fire(string id, double time)
    {
        MapScript.RuleDef? rule = (def.Rules ?? new List<MapScript.RuleDef>()).FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
        if (rule == null || done.ContainsKey(rule.Id)) return false;
        now = time;
        FireRule(rule, time);
        return true;
    }

    private void RunPending(double time)
    {
        if (pending.Count == 0) return;
        List<Pending> due = pending.Where(p => p.When <= time).OrderBy(p => p.When).ToList();
        if (due.Count == 0) return;
        pending.RemoveAll(p => p.When <= time);
        foreach (Pending p in due) p.Act(time);
    }

    // ================================================================== jumps ("/step N")

    // Skips ahead as the step says (see MapScript.StepDef). Returns the checkpoint to take the plane to, or null if the step
    // does not exist.
    public string? Jump(int number, double time)
    {
        if (def.Steps == null || !def.Steps.TryGetValue(number.ToString(Inv), out MapScript.StepDef? step)) return null;
        now = time;
        foreach (string id in step.Done ?? new List<string>()) done.TryAdd(id, time);
        foreach (string group in step.Quiet ?? new List<string>()) QuietGroup(group);
        foreach (string group in step.Remove ?? new List<string>()) Enemies.RemoveGroup(host.Connection, group, false, time);
        foreach (string gate in step.Open ?? new List<string>())
        {
            if (!host.GateOpen(gate)) host.GateOpenNow(gate, false, time);
        }
        foreach (string id in step.Fire ?? new List<string>()) Fire(id, time);
        guideUntil = 0;
        guideUntilRule = "";
        if (guideSet) SetGuideActive(false);
        return step.Checkpoint.Length > 0 ? step.Checkpoint : null;
    }

    // ================================================================== units

    // Creates the units of a spawn. Returns how many were made.
    private int RunSpawn(MapScript.SpawnDef sp, double time)
    {
        if (sp.Name.Length > 0 && !spawnedNames.Add(sp.Name)) return 0;

        var places = new List<(float A, float B, float Rot)>();
        if (sp.Ellipse != null)
        {
            MapScript.EllipseDef e = sp.Ellipse;
            Maps.Point center = map.GetPoint(e.Center);
            float radiusA = e.RadiusU / Ground.CellSize;
            float radiusB = e.RadiusV / Ground.CellSize;
            center.World(out float cx, out float cy);
            for (int i = 0; i < Math.Max(1, e.Count); i++)
            {
                float degrees = e.StartDegrees + e.Opening / 2f + i * (360f - e.Opening) / Math.Max(1, e.Count - 1);
                float angle = degrees * MathF.PI / 180f;
                float a = center.A + radiusA * MathF.Cos(angle);
                float b = center.B + radiusB * MathF.Sin(angle);
                Ground.ToWorld(a, b, out float x, out float y);
                float rot = e.FaceCenter ? MathF.Atan2(cx - x, cy - y) * 180f / MathF.PI : center.Rotation;
                places.Add((a, b, rot));
            }
        }
        else if (sp.Points.Length > 0)
        {
            foreach (Maps.Point p in map.GetGroup(sp.Points)) places.Add((p.A, p.B, p.Rotation));
        }
        else
        {
            Maps.Point p = map.GetPoint(sp.Point);
            if (sp.AtUnit.Length > 0 && lastSeen.TryGetValue(sp.AtUnit, out (float X, float Y) seen))
            {
                Ground.ToGrid(seen.X, seen.Y, out float a, out float b);
                places.Add((a, b, float.NaN));
            }
            else
            {
                places.Add((p.A, p.B, p.Rotation));
            }
        }

        Ground.Rect? area = sp.Area.Length > 0 ? map.GetArea(sp.Area) : null;
        if (sp.Group.Length > 0) attemptedGroups.Add(sp.Group);
        if (!groupIds.TryGetValue(sp.Group, out List<int>? ids)) groupIds[sp.Group] = ids = new List<int>();

        int created = 0;
        foreach ((float a, float b, float rot) in places)
        {
            float rotation = sp.Rot ?? rot;
            int id = host.CreateUnit(sp.Type, a, b, rotation, sp.Group, area, sp.Invulnerable, time);
            if (id == 0) continue;
            created++;
            ids.Add(id);
            if (sp.Asleep) Enemies.SetAsleep(host.Connection, id, true, sp.WakeRadius);
            if (sp.Id.Length > 0 && places.Count == 1) aliasIds[sp.Id] = id;
        }
        if (sp.Id.Length > 0 && places.Count == 1 && created == 0) failedAliases.Add(sp.Id);
        if (sp.Id.Length > 0) attemptedAlias.Add(sp.Id);
        return created;
    }

    private readonly HashSet<string> attemptedAlias = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private void TrackAliases()
    {
        foreach (KeyValuePair<string, int> pair in aliasIds)
        {
            if (Enemies.GetPosition(host.Connection, pair.Value, out float x, out float y)) lastSeen[pair.Key] = (x, y);
        }
    }

    // Living unit for a name: an alias, or the first one alive of a group. 0 = none.
    private int Resolve(string name)
    {
        if (name.Length == 0) return 0;
        if (aliasIds.TryGetValue(name, out int id)) return Enemies.IsAlive(host.Connection, id) ? id : 0;
        if (groupIds.TryGetValue(name, out List<int>? ids))
        {
            foreach (int candidate in ids)
            {
                if (Enemies.IsAlive(host.Connection, candidate)) return candidate;
            }
        }
        return 0;
    }

    private void QuietGroup(string group)
    {
        if (!groupIds.TryGetValue(group, out List<int>? ids)) return;
        foreach (int id in ids) Enemies.SetAsleep(host.Connection, id, true, 0f);
    }

    // ================================================================== guide

    private void SetGuideActive(bool active)
    {
        int unit = Resolve(guideWho);
        if (unit != 0) Enemies.SetDestination(host.Connection, unit, guideX, guideY, active);
    }

    // The guide goes to its point while a guide is set (or follows the player when it is not).
    private void UpdateGuide(double time)
    {
        if (!guideSet) return;
        bool active = (guideUntilRule.Length > 0 && !IsDone(guideUntilRule)) || time < guideUntil;
        SetGuideActive(active);
    }

    private static float Random01()
    {
        lock (rng) return (float)rng.NextDouble();
    }
}
