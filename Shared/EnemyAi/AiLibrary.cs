using System.Text.Json;

namespace SteambirdsAi;

public sealed class AiProblem
{
    public AiProblem(bool error, string where, string text)
    {
        Error = error;
        Where = where;
        Text = text;
    }

    public bool Error { get; }
    public string Where { get; }
    public string Text { get; }

    public override string ToString() => (Error ? "error " : "warning ") + Where + ": " + Text;
}

// A state ready to run: its behavior is already resolved (a behavior file or its own values).
public sealed class AiCompiledState
{
    public string Id = "";
    public string BehaviorId = "";
    public AiMove Move = new AiMove { Mode = "hold" };
    public AiFire Fire = new AiFire();
    public AiContact Contact = new AiContact();
    public bool SelfDestruct;
    public string Message = "";
    public string Bark = "";
    // Summons of the state (empty = none). SummonType is the first line, kept so older readers still see something.
    public AiSummonEntry[] Summons = Array.Empty<AiSummonEntry>();
    public string SummonType = "";
    public int SummonCount = 1;
    public float SummonEvery;
    public int SummonMax;
    public bool Shield;
    public float ShieldSeconds;
    public string ShieldEffect = "";
    public string Effect = "";
    public bool? Homing;
    public AiCompiledTransition[] Transitions = Array.Empty<AiCompiledTransition>();
}

public sealed class AiCompiledTransition
{
    public int To;
    public AiCondition[] When = Array.Empty<AiCondition>();
}

// An AI ready to run (see AiRuntime).
public sealed class AiBrain
{
    public string Id = "";
    public string Name = "";
    // ai\formations file this AI brings with it. Empty = none.
    public string Formation = "";
    public int Initial;
    // Some state summons units or a condition counts them: the caller must fill AiSense.SummonsAlive.
    public bool NeedsSummonCount;
    public AiCompiledState[] States = Array.Empty<AiCompiledState>();

    public int IndexOf(string stateId)
    {
        for (int i = 0; i < States.Length; i++)
            if (string.Equals(States[i].Id, stateId, StringComparison.Ordinal)) return i;
        return -1;
    }
}

// All the AI files of a folder (ai\behaviors and ai\brains). Loading never throws: a file that cannot be read becomes a
// problem in the list and the others still load.
public sealed class AiLibrary
{
    public readonly Dictionary<string, AiBehaviorFile> Behaviors = new Dictionary<string, AiBehaviorFile>(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, AiBrainFile> BrainFiles = new Dictionary<string, AiBrainFile>(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, AiBrain> Brains = new Dictionary<string, AiBrain>(StringComparer.OrdinalIgnoreCase);
    // Where each brain file is: id -> folder inside brains\ ("generic", "world", "dungeons/arena"; "" = loose in brains\).
    public readonly Dictionary<string, string> BrainOrigins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public readonly List<AiProblem> Problems = new List<AiProblem>();

    public string Folder { get; private set; } = "";

    public AiBrain? Get(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return Brains.TryGetValue(AiCatalog.IdOfReference(id), out AiBrain? brain) ? brain : null;
    }

    public static string BehaviorsPath(string folder) => Path.Combine(folder, AiCatalog.BehaviorsFolder);

    public static string BrainsPath(string folder) => Path.Combine(folder, AiCatalog.BrainsFolder);

    // The folder of a brain file inside brains\, with "/" as the separator ("generic", "dungeons/arena"; "" = loose in brains\).
    public static string OriginOf(string brainsFolder, string filePath)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        string root = Path.GetFullPath(brainsFolder);
        if (dir == null || string.Equals(dir.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return "";
        string relative = Path.GetRelativePath(root, dir);
        return relative.Replace('\\', '/');
    }

    // generic = the AI works for any unit (it lives in brains\generic).
    public static bool IsGenericOrigin(string origin) => string.Equals(origin, AiCatalog.GenericFolder, StringComparison.OrdinalIgnoreCase);

    // The AI may be given to this unit (the game's unit name): generic AIs fit any unit; the others fit the units they list.
    public bool Applies(string id, string unit)
    {
        if (!BrainFiles.TryGetValue(id, out AiBrainFile? file)) return false;
        bool generic = BrainOrigins.TryGetValue(id, out string? origin) && IsGenericOrigin(origin);
        return Applies(generic, file.Units, new[] { unit });
    }

    // The rule on its own (the launcher also uses it on files it has open): a generic AI fits any unit; the others fit the units they
    // list. unitNames are the options of one unit type ("a|b" = a or b).
    public static bool Applies(bool generic, IEnumerable<string>? madeFor, IEnumerable<string> unitNames)
    {
        if (generic) return true;
        if (madeFor == null) return false;
        return madeFor.Any(u => unitNames.Any(n => string.Equals((u ?? "").Trim(), (n ?? "").Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    // The AIs a unit can use: first the ones made for it (not generic), then the generic ones; each group by id.
    public List<(string Id, bool Specific)> ApplicableTo(IEnumerable<string> unitNames)
    {
        string[] names = unitNames.ToArray();
        var specific = new List<string>();
        var generic = new List<string>();
        foreach (KeyValuePair<string, AiBrainFile> pair in BrainFiles)
        {
            bool isGeneric = BrainOrigins.TryGetValue(pair.Key, out string? origin) && IsGenericOrigin(origin);
            if (isGeneric) generic.Add(pair.Key);
            else if (Applies(false, pair.Value.Units, names)) specific.Add(pair.Key);
        }
        specific.Sort(StringComparer.OrdinalIgnoreCase);
        generic.Sort(StringComparer.OrdinalIgnoreCase);
        return specific.Select(id => (id, true)).Concat(generic.Select(id => (id, false))).ToList();
    }

    // Changes when a file is added, removed or saved: the server compares it every few seconds.
    public static string Signature(string folder)
    {
        long sum = 0;
        int count = 0;
        try
        {
            foreach (string sub in new[] { BehaviorsPath(folder), BrainsPath(folder), Path.Combine(folder, AiCatalog.FormationsFolder) })
            {
                if (!Directory.Exists(sub)) continue;
                foreach (string file in Directory.EnumerateFiles(sub, "*.json", SearchOption.AllDirectories))
                {
                    count++;
                    sum += File.GetLastWriteTimeUtc(file).Ticks / 10000 + new FileInfo(file).Length;
                }
            }
        }
        catch (IOException)
        {
            // Being written right now: the signature changes again when it is done.
        }
        return count + ":" + sum;
    }

    public static AiLibrary Load(string folder)
    {
        var library = new AiLibrary { Folder = folder };

        foreach (string path in Files(BehaviorsPath(folder)))
        {
            string id = AiCatalog.CleanId(Path.GetFileNameWithoutExtension(path));
            try
            {
                AiBehaviorFile file = AiJson.ParseBehavior(ReadShared(path));
                CheckFileId(library, path, id, file.Id);
                library.Behaviors[id] = file;
            }
            catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException)
            {
                library.Problems.Add(new AiProblem(true, "behaviors\\" + Path.GetRelativePath(BehaviorsPath(folder), path), e.Message));
            }
        }

        // Files in the folders (generic, world, dungeons) come first: a loose file left in brains\ by the old layout loses to them.
        string brainsRoot = Path.GetFullPath(BrainsPath(folder));
        foreach (string path in Files(BrainsPath(folder)).OrderBy(f =>
                     string.Equals(Path.GetDirectoryName(Path.GetFullPath(f)), brainsRoot, StringComparison.OrdinalIgnoreCase) ? 1 : 0))
        {
            string id = AiCatalog.CleanId(Path.GetFileNameWithoutExtension(path));
            try
            {
                AiBrainFile file = AiJson.ParseBrain(ReadShared(path));
                CheckFileId(library, path, id, file.Id);
                if (library.BrainFiles.ContainsKey(id))
                {
                    library.Problems.Add(new AiProblem(OriginOf(BrainsPath(folder), path).Length > 0, "brains\\" + Path.GetRelativePath(BrainsPath(folder), path),
                        "another AI file already has the id \"" + id + "\" (in " + library.BrainOrigins[id] + "): this one is ignored"));
                    continue;
                }
                library.BrainFiles[id] = file;
                library.BrainOrigins[id] = OriginOf(BrainsPath(folder), path);
            }
            catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException)
            {
                library.Problems.Add(new AiProblem(true, "brains\\" + Path.GetRelativePath(BrainsPath(folder), path), e.Message));
            }
        }

        foreach (KeyValuePair<string, AiBehaviorFile> pair in library.Behaviors)
            library.Problems.AddRange(CheckBehavior(pair.Key, pair.Value));

        foreach (KeyValuePair<string, AiBrainFile> pair in library.BrainFiles)
        {
            AiBrain? brain = Compile(pair.Key, pair.Value, library.FindBehavior, library.Problems);
            if (brain != null) library.Brains[pair.Key] = brain;
        }
        return library;
    }

    public AiBehaviorFile? FindBehavior(string id)
    {
        return Behaviors.TryGetValue((id ?? "").Trim(), out AiBehaviorFile? file) ? file : null;
    }

    // Every .json of the folder and of the folders inside it (brains are sorted into generic\, world\ and dungeons\<name>\).
    private static IEnumerable<string> Files(string folder)
    {
        if (!Directory.Exists(folder)) return Array.Empty<string>();
        return Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void CheckFileId(AiLibrary library, string path, string id, string? inFile)
    {
        if (id.Length == 0)
            library.Problems.Add(new AiProblem(true, Path.GetFileName(path), "the file name has no letters or digits"));
        else if (!string.IsNullOrWhiteSpace(inFile) && !string.Equals(inFile, id, StringComparison.OrdinalIgnoreCase))
            library.Problems.Add(new AiProblem(false, Path.GetFileName(path), "the \"id\" inside (" + inFile + ") is not the file name: the file name is used"));
    }

    // ------------------------------------------------------------------ checks and compile

    public static List<AiProblem> CheckBehavior(string id, AiBehaviorFile file)
    {
        var problems = new List<AiProblem>();
        string where = "behavior " + id;
        CheckMove(problems, where, file.Move);
        CheckFire(problems, where, file.Fire);
        return problems;
    }

    private static AiSummonEntry[] CompileSummons(AiStateFile state, List<AiProblem> problems, string where)
    {
        var lines = new List<AiSummonEntry>();
        if (state.Summons != null && state.Summons.Count > 0)
        {
            foreach (AiSummonEntry line in state.Summons)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.Unit)) continue;
                lines.Add(NormalizeSummon(line, problems, where));
            }
        }
        else if (!string.IsNullOrWhiteSpace(state.Summon))
        {
            lines.Add(NormalizeSummon(new AiSummonEntry
            {
                Unit = state.Summon!.Trim(),
                Ai = state.SummonAi ?? "",
                Role = state.SummonRole ?? "",
                Place = string.IsNullOrWhiteSpace(state.SummonPlace) ? "behind" : state.SummonPlace!.Trim(),
                Count = state.SummonCount,
                Every = state.SummonEvery,
                Max = state.SummonMax,
                Distance = state.SummonDistance > 0f ? state.SummonDistance : 5f,
                Angle = state.SummonAngle
            }, problems, where));
        }
        return lines.ToArray();
    }

    private static AiSummonEntry NormalizeSummon(AiSummonEntry line, List<AiProblem> problems, string where)
    {
        // 0 summons nothing. A missing count stays 1 (the property default); only an explicit 0 is skipped.
        int count = line.Count < 0 ? 0 : line.Count;
        if (count > 30)
        {
            problems.Add(new AiProblem(false, where, "summons at most 30 units at a time: 30 are used"));
            count = 30;
        }
        float every = Math.Max(0f, line.Every);
        if (every > 0f && every < 0.5f) every = 0.5f;
        return new AiSummonEntry
        {
            Id = line.Id ?? "",
            Unit = (line.Unit ?? "").Trim(),
            Ai = (line.Ai ?? "").Trim(),
            Role = (line.Role ?? "").Trim(),
            Place = string.IsNullOrWhiteSpace(line.Place) ? "behind" : line.Place.Trim().ToLowerInvariant(),
            Count = count,
            Every = every,
            Max = Math.Max(0, line.Max),
            Distance = line.Distance <= 0f ? 5f : line.Distance,
            Angle = line.Angle
        };
    }

    private static void CheckMove(List<AiProblem> problems, string where, AiMove? move)
    {
        if (move == null) return;
        if (!AiCatalog.IsMoveMode(move.Mode))
            problems.Add(new AiProblem(true, where, "unknown move mode \"" + move.Mode + "\""));
        if (move.Speed < 0f) problems.Add(new AiProblem(false, where, "negative speed"));
        if ((string.Equals(move.Mode, "orbit", StringComparison.Ordinal) || string.Equals(move.Mode, "orbitLeader", StringComparison.Ordinal)) && move.Distance <= 0f)
            problems.Add(new AiProblem(false, where, "orbit with distance 0 or less"));
        if (string.Equals(move.Mode, "builtin", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(move.Builtin) &&
            !AiCatalog.BuiltinFlights.Contains((move.Builtin ?? "").Trim().ToLowerInvariant()))
            problems.Add(new AiProblem(true, where, "unknown built-in flight \"" + move.Builtin + "\""));
        if (move.SpeedMult <= 0f) problems.Add(new AiProblem(false, where, "speedMult 0 or less"));
    }

    private static void CheckFire(List<AiProblem> problems, string where, AiFire? fire)
    {
        if (fire == null) return;
        if (fire.RateMult < 0f) problems.Add(new AiProblem(false, where, "negative fire rate multiplier"));
    }

    // Checks a brain and turns it into something that runs. Returns null when it cannot work (no states, no valid first state).
    public static AiBrain? Compile(string id, AiBrainFile file, Func<string, AiBehaviorFile?> behaviors, List<AiProblem>? problems = null)
    {
        problems ??= new List<AiProblem>();
        string where = "brain " + id;
        List<AiStateFile> states = (file.States ?? new List<AiStateFile>()).Where(s => s != null).ToList();
        if (states.Count == 0)
        {
            problems.Add(new AiProblem(true, where, "has no states"));
            return null;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (AiStateFile state in states)
        {
            if (string.IsNullOrWhiteSpace(state.Id)) problems.Add(new AiProblem(true, where, "a state has no id"));
            else if (!ids.Add(state.Id)) problems.Add(new AiProblem(true, where, "two states are called \"" + state.Id + "\""));
        }

        // Behaviors embedded in the file: only this AI's states see them, and they win over a behavior file with the same id.
        var embedded = new Dictionary<string, AiBehaviorFile>(StringComparer.OrdinalIgnoreCase);
        foreach (AiBehaviorFile own in (file.Behaviors ?? new List<AiBehaviorFile>()).Where(b => b != null))
        {
            string ownId = AiCatalog.CleanId(own.Id ?? "");
            if (ownId.Length == 0) problems.Add(new AiProblem(true, where, "an embedded behavior has no id"));
            else if (behaviors(ownId) != null)
                problems.Add(new AiProblem(true, where, "the embedded behavior \"" + ownId + "\" has the id of a file of the behaviors folder: name it \"" + id + "_<name>\""));
            else if (!embedded.TryAdd(ownId, own)) problems.Add(new AiProblem(true, where, "two embedded behaviors are called \"" + ownId + "\""));
            else problems.AddRange(CheckBehavior(id + "/" + ownId, own));
        }
        Func<string, AiBehaviorFile?> sharedBehaviors = behaviors;
        behaviors = name => embedded.TryGetValue(AiCatalog.CleanId(name), out AiBehaviorFile? hit) ? hit : sharedBehaviors(name);

        var brain = new AiBrain
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(file.Name) ? id : file.Name,
            Formation = (file.Formation ?? "").Trim()
        };
        brain.States = new AiCompiledState[states.Count];
        for (int i = 0; i < states.Count; i++)
        {
            AiStateFile s = states[i];
            string stateWhere = where + ", state " + s.Id;
            var run = new AiCompiledState
            {
                Id = s.Id ?? "",
                BehaviorId = s.Behavior ?? "",
                SelfDestruct = s.SelfDestruct,
                Message = s.Message ?? "",
                Bark = s.Bark ?? "",
                Summons = CompileSummons(s, problems, stateWhere),
                SummonType = "",
                SummonCount = 1,
                SummonEvery = 0f,
                SummonMax = 0,
                Shield = s.Shield,
                ShieldSeconds = Math.Max(0f, s.ShieldSeconds),
                ShieldEffect = (s.ShieldEffect ?? "").Trim(),
                Effect = (s.Effect ?? "").Trim(),
                Homing = string.Equals(s.Homing?.Trim(), "on", StringComparison.OrdinalIgnoreCase) ? true
                    : string.Equals(s.Homing?.Trim(), "off", StringComparison.OrdinalIgnoreCase) ? false : null
            };
            if (run.Summons.Length > 0)
            {
                run.SummonType = run.Summons[0].Unit;
                run.SummonCount = run.Summons[0].Count;
                run.SummonEvery = run.Summons[0].Every;
                run.SummonMax = run.Summons[0].Max;
            }

            AiMove? move = s.Move;
            AiFire? fire = s.Fire;
            AiContact? contact = s.Contact;
            if (!string.IsNullOrWhiteSpace(s.Behavior))
            {
                AiBehaviorFile? behavior = behaviors(s.Behavior!);
                if (behavior == null)
                    problems.Add(new AiProblem(true, stateWhere, "uses the behavior \"" + s.Behavior + "\", which does not exist"));
                else
                {
                    move = behavior.Move;
                    fire = behavior.Fire;
                    contact = behavior.Contact;
                    CheckMove(problems, stateWhere + " (behavior " + s.Behavior + ")", move);
                }
            }
            else
            {
                CheckMove(problems, stateWhere, move);
            }
            CheckFire(problems, stateWhere, fire);

            run.Move = move ?? new AiMove { Mode = "hold" };
            if (!AiCatalog.IsMoveMode(run.Move.Mode)) run.Move = new AiMove { Mode = "hold" };
            run.Fire = fire ?? new AiFire();
            run.Contact = contact ?? new AiContact();
            brain.States[i] = run;
        }

        for (int i = 0; i < states.Count; i++)
        {
            AiStateFile s = states[i];
            var list = new List<AiCompiledTransition>();
            foreach (AiTransition t in s.Transitions ?? new List<AiTransition>())
            {
                if (t == null) continue;
                int to = brain.IndexOf(t.To ?? "");
                if (to < 0)
                {
                    problems.Add(new AiProblem(true, where + ", state " + s.Id, "a transition goes to \"" + t.To + "\", which is not a state"));
                    continue;
                }
                var conditions = new List<AiCondition>();
                foreach (AiCondition c in t.When ?? new List<AiCondition>())
                {
                    if (c == null) continue;
                    if (!AiCatalog.IsConditionType(c.Type))
                    {
                        problems.Add(new AiProblem(true, where + ", state " + s.Id, "unknown condition \"" + c.Type + "\""));
                        continue;
                    }
                    if (c.Max.HasValue && c.Max.Value < c.Value)
                        problems.Add(new AiProblem(false, where + ", state " + s.Id, "condition " + c.Type + ": max is smaller than value"));
                    conditions.Add(c);
                }
                list.Add(new AiCompiledTransition { To = to, When = conditions.ToArray() });
            }
            brain.States[i].Transitions = list.ToArray();
        }

        brain.NeedsSummonCount = brain.States.Any(st => st.Summons.Length > 0 || st.Transitions.Any(tr => tr.When.Any(c => c.Type == "summonsAtMost")));
        brain.Initial = brain.IndexOf(file.Initial ?? "");
        if (brain.Initial < 0)
        {
            problems.Add(new AiProblem(!string.IsNullOrWhiteSpace(file.Initial), where,
                string.IsNullOrWhiteSpace(file.Initial)
                    ? "has no initial state: the first one is used"
                    : "the initial state \"" + file.Initial + "\" does not exist: the first one is used"));
            brain.Initial = 0;
        }

        // States nobody reaches.
        var reached = new HashSet<int> { brain.Initial };
        var queue = new Queue<int>();
        queue.Enqueue(brain.Initial);
        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            foreach (AiCompiledTransition t in brain.States[at].Transitions)
                if (reached.Add(t.To)) queue.Enqueue(t.To);
        }
        for (int i = 0; i < brain.States.Length; i++)
        {
            if (!reached.Contains(i))
                problems.Add(new AiProblem(false, where, "the state \"" + brain.States[i].Id + "\" can never be reached"));
        }

        return brain;
    }
}
