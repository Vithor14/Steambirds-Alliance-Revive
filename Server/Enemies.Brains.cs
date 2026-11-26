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
            if (reportedBrainFailures.Add(AiCatalog.IdOfReference(aiId)))
                log("ENEMY", "type '" + typeKey + "' (and every type that names it) uses the AI '" + aiId + "', which does not exist in the ai folder (see /brains): the built-in behavior is used");
            return null;
        }
        return new AiRuntime(brain, rng);
    }

    // The AI a unit type runs: the one it names ("ai"); a type with no AI gets the generic AI of its old "behavior" (chase when it has
    // none), so every unit is driven by an AI. null only when airains\generic has no such file (then the built-in flight runs).
    private static string? EffectiveAi(EnemyType type)
    {
        if (!string.IsNullOrWhiteSpace(type.Ai)) return type.Ai;
        string generic = "generic_" + NormalizeBehavior(type.Behavior);
        return aiLibrary.Brains.ContainsKey(generic) ? generic : null;
    }

    // The flight a unit type starts with: the built-in flight named by the first state of its AI (the generic_* AIs, which
    // replace the plain "behavior" field), otherwise its "behavior". Decides idle (walls, crates) and fixed (cannons) units.
    public static string TypeFlight(EnemyType type)
    {
        string? aiId = EffectiveAi(type);
        if (!string.IsNullOrWhiteSpace(aiId))
        {
            AiBrain? brain = aiLibrary.Get(aiId);
            if (brain != null && brain.Initial >= 0 && brain.Initial < brain.States.Length)
            {
                AiMove move = brain.States[brain.Initial].Move;
                if (string.Equals(move.Mode, "builtin", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(move.Builtin))
                    return NormalizeBehavior(move.Builtin);
            }
        }
        return NormalizeBehavior(type.Behavior);
    }

    // A fixed cannon fires straight out of its gun mouth and never turns, with or without an AI that flies it with the
    // built-in flight (the generic_fixed AI).
    private static bool IsFixedGun(Enemy enemy)
    {
        return enemy.Behavior == "fixed" && (enemy.Ai == null || enemy.Ai.Out.UseBuiltin);
    }

    // A gun mount may shoot now: the active AI state decides; without one, the guns of the type.
    private static bool GunAllowed(Enemy enemy, int index)
    {
        int[]? fromBrain = enemy.Ai?.Out.Guns;
        if (fromBrain != null && fromBrain.Length > 0) return Array.IndexOf(fromBrain, index) >= 0;
        return enemy.ActiveGuns.Count == 0 || enemy.ActiveGuns.Contains(index);
    }

    // One tick of the AI of a unit that has a brain: runs the state machine and does what the states ask on the way (message,
    // speech, look, homing bullets, shield, summons). The caller turns the output into flight (Think).
    private static AiOutput BrainStep(Instance inst, Enemy enemy, double now, float dt, float velMin, float velMax, bool validTarget,
        Action<string, string>? log)
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
            Now = now, Dt = dt, LastHitAt = enemy.LastDamagedAt,
            SummonsAlive = ai.Brain.NeedsSummonCount ? CountSummons(inst, enemy) : 0
        };
        // The summoner, while it is still alive (a dead one leaves the dictionary; its id may be reused by another unit).
        Enemy? summoner = enemy.Summoner != null && inst.Enemies.TryGetValue(enemy.Summoner.ShortId, out Enemy? found) &&
                          ReferenceEquals(found, enemy.Summoner) ? found : null;
        if (ai.Brain.NeedsSummoner && summoner != null)
        {
            sense.HasSummoner = true;
            sense.SummonerX = summoner.X;
            sense.SummonerY = summoner.Y;
            sense.SummonerRot = summoner.Rot;
        }
        if (ai.Brain.NeedsSiblingCount && enemy.Summoner != null)
            sense.CountSiblings = unit => CountSiblings(inst, enemy, unit);
        AiOutput o = ai.Step(sense);

        if (o.Entered)
        {
            if (log != null)
                log("ENEMY", inst.Role + " " + Describe(enemy) + " AI " + ai.Brain.Id + ": " +
                    (o.PreviousState == null ? "" : o.PreviousState.Id + " -> ") + o.State.Id);
            foreach (AiCompiledState entered in o.EnteredStates)
            {
                if (entered.Message.Length > 0) Warn(inst, entered.Message);
                if (entered.Bark.Length > 0) Speak(inst, enemy, entered.Bark);
                if (entered.Homing.HasValue) enemy.Homing = entered.Homing.Value;
                if (entered.Effect.Length > 0)
                {
                    // A new look from this state on (it stays until another state changes it; a shield shows its own meanwhile).
                    enemy.BaseEffect = MonoHash.StatefulEffectId(entered.Effect);
                    if (now >= enemy.ShieldUntil && enemy.EffectId != enemy.BaseEffect)
                    {
                        enemy.EffectId = enemy.BaseEffect;
                        enemy.EffectChanged = true;
                    }
                }
            }
        }

        // Shield: renewed every tick while the state asks for it; UpdateInstance puts the normal look back when it stops.
        if (o.ShieldOn)
        {
            enemy.ShieldUntil = now + 0.35;
            // The shield itself shows as the game's shielded glow (FLAG_SHIELDED, Enemies.Messages.cs StatusFlags);
            // shieldEffect adds a client effect on top of it while it lasts. Empty = the unit keeps its normal effect.
            short effect = o.ShieldEffect.Length > 0 ? MonoHash.StatefulEffectId(o.ShieldEffect) : enemy.BaseEffect;
            if (effect != enemy.EffectId)
            {
                enemy.EffectId = effect;
                enemy.EffectChanged = true;
            }
        }

        foreach (AiSummonOrder order in o.Summons) SummonUnits(inst, enemy, order, now, log);
        return o;
    }

    private static int CountSummons(Instance inst, Enemy enemy)
    {
        int alive = 0;
        foreach (Enemy other in inst.Enemies.Values)
            if (ReferenceEquals(other.Summoner, enemy)) alive++;
        return alive;
    }

    // siblingsAtMost: the other live units summoned by the same summoner (only those summoned as "unit" when it is not empty).
    // Counted whether or not the summoner is still alive.
    private static int CountSiblings(Instance inst, Enemy enemy, string unit)
    {
        int alive = 0;
        foreach (Enemy other in inst.Enemies.Values)
        {
            if (ReferenceEquals(other, enemy) || !ReferenceEquals(other.Summoner, enemy.Summoner)) continue;
            if (unit.Length > 0 && !string.Equals(other.SummonedAs, unit, StringComparison.OrdinalIgnoreCase)) continue;
            alive++;
        }
        return alive;
    }

    // The type a summon names. A dungeon map keeps its types under "<map>/<name>": the summoner's own map is tried first.
    private static EnemyType? FindSummonType(Enemy enemy, string name, out string key)
    {
        int slash = enemy.TypeKey.LastIndexOf('/');
        if (slash >= 0)
        {
            key = enemy.TypeKey.Substring(0, slash + 1) + name;
            EnemyType? local = GetEnemyType(key);
            if (local != null) return local;
        }
        key = name;
        return GetEnemyType(name);
    }

    // Creates the units of a summon behind the summoner, or evenly spaced on a circle around it (they stay in its group and room,
    // so a script counts them together).
    private static void SummonUnits(Instance inst, Enemy enemy, AiSummonOrder order, double now, Action<string, string>? log)
    {
        string typeName = order.Type;
        int count = order.Count;
        float distance = order.Distance > 0f ? order.Distance : 5f;
        EnemyType? type = FindSummonType(enemy, typeName, out string key);
        if (type == null)
        {
            if (log != null && reportedBrainFailures.Add("summon|" + enemy.TypeKey + "|" + typeName))
                log("ENEMY", "the AI of '" + enemy.TypeKey + "' summons the type '" + typeName + "', which does not exist");
            return;
        }

        var summoned = new List<Enemy>();
        count = Math.Clamp(count, 1, 10);
        for (int i = 0; i < count; i++)
        {
            float a = (order.Around
                ? enemy.Rot + 180f + order.Angle + i * 360f / count
                : enemy.Rot + 180f + (i - (count - 1) * 0.5f) * 40f) * Rad;
            float x = enemy.X + MathF.Sin(a) * distance;
            float y = enemy.Y + MathF.Cos(a) * distance;
            ConfineTo(enemy.Area, false, ref x, ref y, DestMargin);
            Enemy? fresh = CreateEnemy(inst, key, type, x, y, enemy.FromWave, now, log ?? ((_, _) => { }));
            if (fresh == null) continue;
            fresh.Group = enemy.Group;
            fresh.Area = enemy.Area;
            fresh.FreeRoam = fresh.FreeRoam || enemy.FreeRoam;
            fresh.Summoner = enemy;
            fresh.SummonedAs = typeName;
            if (order.Around)
            {
                // Facing along the circle (clockwise), so an orbit around the summoner starts without a U-turn.
                fresh.Rot = NormalizeAngle(a / Rad + 90f);
            }
            summoned.Add(fresh);
        }
        if (summoned.Count == 0) return;
        SendSpawns(inst, summoned, now);
        log?.Invoke("ENEMY", inst.Role + " " + Describe(enemy) + " summoned " + summoned.Count + " x " + key);
    }

    // An AI of brains\world made for this game unit (its "units" list names it), or null. The first by id when there are several.
    public static string? WorldAiMadeFor(string template)
    {
        foreach (KeyValuePair<string, AiBrainFile> pair in aiLibrary.BrainFiles.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!aiLibrary.BrainOrigins.TryGetValue(pair.Key, out string? origin) ||
                !string.Equals(origin, AiCatalog.WorldFolder, StringComparison.OrdinalIgnoreCase)) continue;
            if (!aiLibrary.Brains.ContainsKey(pair.Key)) continue;
            if (AiLibrary.Applies(false, pair.Value.Units, new[] { template })) return pair.Key;
        }
        return null;
    }

    // The game units (templates) an AI summons by itself: the templates of the types its states name in "summon". The open world
    // does not create those as parts of a boss that runs this AI (the AI brings them when the fight asks for them).
    public static HashSet<string> TemplatesSummonedBy(string? aiId)
    {
        var templates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AiBrain? brain = aiLibrary.Get(aiId);
        if (brain == null) return templates;
        foreach (AiCompiledState state in brain.States)
        {
            if (state.SummonType.Length == 0) continue;
            EnemyType? type = GetEnemyType(state.SummonType);
            if (type != null && !string.IsNullOrWhiteSpace(type.Template)) templates.Add(type.Template.Trim());
        }
        return templates;
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
