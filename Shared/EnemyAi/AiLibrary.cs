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
    public int Initial;
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
    public readonly List<AiProblem> Problems = new List<AiProblem>();

    public string Folder { get; private set; } = "";

    public AiBrain? Get(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return Brains.TryGetValue(id.Trim(), out AiBrain? brain) ? brain : null;
    }

    public static string BehaviorsPath(string folder) => Path.Combine(folder, AiCatalog.BehaviorsFolder);

    public static string BrainsPath(string folder) => Path.Combine(folder, AiCatalog.BrainsFolder);

    // Changes when a file is added, removed or saved: the server compares it every few seconds.
    public static string Signature(string folder)
    {
        long sum = 0;
        int count = 0;
        try
        {
            foreach (string sub in new[] { BehaviorsPath(folder), BrainsPath(folder) })
            {
                if (!Directory.Exists(sub)) continue;
                foreach (string file in Directory.EnumerateFiles(sub, "*.json"))
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
                library.Problems.Add(new AiProblem(true, "behaviors\\" + Path.GetFileName(path), e.Message));
            }
        }

        foreach (string path in Files(BrainsPath(folder)))
        {
            string id = AiCatalog.CleanId(Path.GetFileNameWithoutExtension(path));
            try
            {
                AiBrainFile file = AiJson.ParseBrain(ReadShared(path));
                CheckFileId(library, path, id, file.Id);
                library.BrainFiles[id] = file;
            }
            catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException)
            {
                library.Problems.Add(new AiProblem(true, "brains\\" + Path.GetFileName(path), e.Message));
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

    private static IEnumerable<string> Files(string folder)
    {
        if (!Directory.Exists(folder)) return Array.Empty<string>();
        return Directory.EnumerateFiles(folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
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

    private static void CheckMove(List<AiProblem> problems, string where, AiMove? move)
    {
        if (move == null) return;
        if (!AiCatalog.IsMoveMode(move.Mode))
            problems.Add(new AiProblem(true, where, "unknown move mode \"" + move.Mode + "\""));
        if (move.Speed < 0f) problems.Add(new AiProblem(false, where, "negative speed"));
        if (string.Equals(move.Mode, "orbit", StringComparison.Ordinal) && move.Distance <= 0f)
            problems.Add(new AiProblem(false, where, "orbit with distance 0 or less"));
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

        var brain = new AiBrain { Id = id, Name = string.IsNullOrWhiteSpace(file.Name) ? id : file.Name };
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
                Bark = s.Bark ?? ""
            };

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
