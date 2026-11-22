using SteambirdsAi;

// Enemy AI files: the folder ai\ (behaviors\*.json and brains\*.json), made in the launcher's "Enemy AI" tab.
// A unit type (enemies.json or the "types" of a map) picks one with "ai": "<brain id>". Without it, the built-in
// "behavior" of the type applies. The files are read again by themselves when they change (new enemies use the new
// version; the ones already flying keep the one they were born with).
// Format and logic: Shared\EnemyAi (compiled into the launcher too, so the simulator runs the same code).
public static partial class Enemies
{
    private static AiLibrary aiLibrary = new AiLibrary();
    private static string aiSignature = "";
    private static bool aiLoaded;
    private static readonly HashSet<string> reportedBrainFailures = new HashSet<string>();

    private static string AiFolder()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "ai");
    }

    // Reads (or rereads, if a file changed) the ai folder. A file with an error is skipped and reported; the others still work.
    private static void LoadAi(Action<string, string> log)
    {
        try
        {
            string folder = AiFolder();
            string signature = AiLibrary.Signature(folder);
            if (aiLoaded && signature == aiSignature) return;
            aiSignature = signature;
            aiLoaded = true;

            AiLibrary fresh = AiLibrary.Load(folder);
            aiLibrary = fresh;
            reportedBrainFailures.Clear();
            log("ENEMY", folder + ": " + fresh.Brains.Count + " AI(s), " + fresh.Behaviors.Count + " behavior(s)");
            foreach (AiProblem problem in fresh.Problems) log("ENEMY", "ai: " + problem);
            ValidateAiReferences(log);
        }
        catch (Exception e)
        {
            log("ENEMY", "could not read the ai folder: " + e.GetType().Name + ": " + e.Message);
        }
    }

    // The AI of a new unit, or null when the type does not use one (or the file does not exist: the built-in behavior runs).
    private static AiRuntime? AttachBrain(string typeKey, string? aiId, Action<string, string> log)
    {
        if (string.IsNullOrWhiteSpace(aiId)) return null;
        AiBrain? brain = aiLibrary.Get(aiId);
        if (brain == null)
        {
            if (reportedBrainFailures.Add(typeKey + "|" + aiId))
                log("ENEMY", "type '" + typeKey + "' uses the AI '" + aiId + "', which does not exist in the ai folder (see /brains): the built-in behavior is used");
            return null;
        }
        return new AiRuntime(brain, rng);
    }

    // A gun mount may shoot now: the active AI state decides; without one, the guns of the type or boss phase.
    private static bool GunAllowed(Enemy enemy, int index)
    {
        int[]? fromBrain = enemy.Ai?.Out.Guns;
        if (fromBrain != null && fromBrain.Length > 0) return Array.IndexOf(fromBrain, index) >= 0;
        return enemy.ActiveGuns.Count == 0 || enemy.ActiveGuns.Contains(index);
    }

    // One tick of the AI of a unit that has a brain: fills the destination and speed for the common flight code of Think.
    private static void BrainThink(Instance inst, Enemy enemy, double now, float dt, float velMin, float velMax, bool validTarget,
        Action<string, string>? log, out float targetX, out float targetY, out float targetVel, out bool hold)
    {
        AiRuntime ai = enemy.Ai!;
        var sense = new AiSense
        {
            X = enemy.X, Y = enemy.Y, Rot = enemy.Rot, Vel = enemy.Vel,
            VelMin = velMin, VelMax = velMax,
            HomeX = enemy.HomeX, HomeY = enemy.HomeY,
            HealthFraction = enemy.MaxHp > 0f ? enemy.Health / enemy.MaxHp : 1f,
            HasTarget = validTarget,
            TargetX = inst.PlayerX, TargetY = inst.PlayerY, TargetVX = inst.PlayerVX, TargetVY = inst.PlayerVY,
            Now = now, Dt = dt, LastHitAt = enemy.LastDamagedAt
        };
        AiOutput o = ai.Step(sense);

        if (o.Entered)
        {
            if (log != null)
                log("ENEMY", inst.Role + " " + Describe(enemy) + " AI " + ai.Brain.Id + ": " +
                    (o.PreviousState == null ? "" : o.PreviousState.Id + " -> ") + o.State.Id);
            if (o.State.Message.Length > 0) Warn(inst, o.State.Message);
            if (o.State.Bark.Length > 0) Speak(inst, enemy, o.State.Bark);
        }

        targetX = o.TargetX;
        targetY = o.TargetY;
        targetVel = o.TargetVel;
        hold = o.Hold;
    }

    // /brains: the AIs of the folder and the state of the ones flying now.
    private static void ReplyBrains(Instance inst)
    {
        if (aiLibrary.Brains.Count == 0)
        {
            Reply(inst, "No AI in " + AiFolder() + " (make one in the launcher's Enemy AI tab).");
            return;
        }
        Reply(inst, aiLibrary.Brains.Count + " AI(s), " + aiLibrary.Behaviors.Count + " behavior(s), " +
            aiLibrary.Problems.Count(p => p.Error) + " error(s): " + string.Join(", ", aiLibrary.Brains.Keys.Take(12)));
        foreach (Enemy enemy in inst.Enemies.Values.Where(e => e.Ai != null).Take(8))
            Reply(inst, Describe(enemy) + ": AI " + enemy.Ai!.Brain.Id + ", state " + enemy.Ai.State.Id + " (" +
                Num((float)enemy.Ai.StateTime(NowFor(inst))) + "s)");
    }

    private static double NowFor(Instance inst)
    {
        return inst.LastTick;
    }

    // Types of enemies.json that name an AI that is not in the folder (reported when the files are read).
    private static void ValidateAiReferences(Action<string, string> log)
    {
        foreach (KeyValuePair<string, EnemyType> pair in config.Types)
        {
            string id = pair.Value.Ai;
            if (id.Length > 0 && aiLibrary.Get(id) == null)
                log("ENEMY", "enemies.json: the type '" + pair.Key + "' uses the AI '" + id + "', which does not exist");
        }
    }
}
