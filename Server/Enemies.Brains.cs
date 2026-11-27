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
            Formations.Reload(folder, fresh);
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
            HasLeader = enemy.Leader != null && inst.Enemies.ContainsKey(enemy.Leader.ShortId),
            LeaderX = enemy.Leader?.X ?? 0f, LeaderY = enemy.Leader?.Y ?? 0f,
            Now = now, Dt = dt, LastHitAt = enemy.LastDamagedAt,
            SummonsAlive = ai.Brain.NeedsSummonCount ? CountSummons(inst, enemy, "", "") : 0,
            CountMatching = ai.Brain.NeedsSummonCount ? (unit, role) => CountSummons(inst, enemy, unit, role) : null,
            HasSummoner = enemy.Summoner != null && inst.Enemies.ContainsKey(enemy.Summoner.ShortId),
            SummonerX = enemy.Summoner?.X ?? 0f, SummonerY = enemy.Summoner?.Y ?? 0f, SummonerRot = enemy.Summoner?.Rot ?? 0f,
            CountSiblings = name => CountSiblings(inst, enemy, name)
        };
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
        if (o.ShieldOn && !enemy.ShieldSuppressed)
        {
            enemy.ShieldUntil = now + 0.35;
            // Empty look: the unit takes no damage and keeps its own sprite. tankShield is the player tank's bubble.
            if (o.ShieldEffect.Length > 0)
            {
                short effect = MonoHash.StatefulEffectId(o.ShieldEffect);
                if (effect != enemy.EffectId)
                {
                    enemy.EffectId = effect;
                    enemy.EffectChanged = true;
                }
            }
        }

        foreach (AiSummonOrder order in o.Summons) SummonUnits(inst, enemy, order, now, log);
        return o;
    }

    private static int CountSummons(Instance inst, Enemy enemy, string unit, string role)
    {
        int alive = 0;
        foreach (Enemy other in inst.Enemies.Values)
        {
            if (!ReferenceEquals(other.Summoner, enemy)) continue;
            if (unit.Length > 0 && !other.SummonUnit.Equals(unit, StringComparison.OrdinalIgnoreCase)) continue;
            if (role.Length > 0 && !other.SummonRole.Equals(role, StringComparison.OrdinalIgnoreCase)) continue;
            alive++;
        }
        return alive;
    }

    private static int CountSiblings(Instance inst, Enemy enemy, string unit)
    {
        if (enemy.Summoner == null) return 0;
        int alive = 0;
        foreach (Enemy other in inst.Enemies.Values)
        {
            if (ReferenceEquals(other, enemy) || !ReferenceEquals(other.Summoner, enemy.Summoner)) continue;
            if (unit.Length > 0 && !other.SummonUnit.Equals(unit, StringComparison.OrdinalIgnoreCase)) continue;
            alive++;
        }
        return alive;
    }

    // The one AI in ai\brains\world whose units list names this template (the Ambassador for ufo, the Inquisitor for
    // highmountainhunter). Null when none do, or when more than one does: the host picks among those in the launcher.
    public static string? WorldAiMadeFor(string template)
    {
        string? found = null;
        foreach (KeyValuePair<string, AiBrainFile> pair in aiLibrary.BrainFiles.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!aiLibrary.BrainOrigins.TryGetValue(pair.Key, out string? origin) ||
                !string.Equals(origin, AiCatalog.WorldFolder, StringComparison.OrdinalIgnoreCase)) continue;
            if (!aiLibrary.Brains.ContainsKey(pair.Key)) continue;
            if (!AiLibrary.Applies(false, pair.Value.Units, new[] { template })) continue;
            if (found != null) return null;
            found = pair.Key;
        }
        return found;
    }

    // Game units an AI summons by itself. A boss that runs this AI does not also spawn those as parts.
    public static HashSet<string> TemplatesSummonedBy(string? aiId)
    {
        var templates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AiBrain? brain = aiLibrary.Get(aiId);
        if (brain == null) return templates;
        foreach (AiCompiledState state in brain.States)
        {
            foreach (AiSummonEntry line in state.Summons)
            {
                if (line.Unit.Length == 0) continue;
                EnemyType? type = GetEnemyType(line.Unit);
                string name = type != null && !string.IsNullOrWhiteSpace(type.Template) ? type.Template.Trim() : line.Unit;
                if (name.Length > 0) templates.Add(name);
            }
        }
        return templates;
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
        EnemyType? named = GetEnemyType(name);
        if (named != null) return named;
        EnemyType? fromAi = TypeFromAi(name, out key);
        if (fromAi != null) return fromAi;
        UnitSummary? plain = FindUnit(name);
        if (plain == null || plain.Wall || plain.Player)
        {
            key = name;
            return null;
        }
        key = "summon/" + plain.Name;
        if (GetEnemyType(key) == null)
        {
            RegisterDefaultType(key, new EnemyType
            {
                Name = plain.Name,
                Template = plain.Name,
                Ai = "generic_chase",
                FreeRoam = true
            });
        }
        return GetEnemyType(key);
    }

    // /enemy and a summon can name an AI file instead of a type in enemies.json. The unit is the first one in that
    // AI's "units" list (so ambassador_cat_outer must list ufosaucermini first). "ai_inquisitor" also finds
    // inquisitor_chase. The type is remembered for the rest of the run; a type of the same key in enemies.json still wins.
    internal static EnemyType? TypeFromAi(string reference, out string key)
    {
        key = "";
        AiBrain? brain = BrainForCommand(reference);
        if (brain == null || !aiLibrary.BrainFiles.TryGetValue(brain.Id, out AiBrainFile? file)) return null;
        string template = file.Units?.FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))?.Trim() ?? "";
        if (template.Length == 0) return null;
        UnitSummary? unit = FindUnit(template);
        var type = new EnemyType
        {
            Name = string.IsNullOrWhiteSpace(file.Name) ? brain.Id : file.Name.Trim(),
            Template = template,
            Ai = brain.Id,
            Boss = unit?.Boss ?? false,
            DeathUnit = FindUnit(template + "DEATH") != null ? template + "DEATH" : ""
        };
        key = brain.Id;
        RegisterDefaultType(key, type);
        if (!key.StartsWith("ai_", StringComparison.OrdinalIgnoreCase)) RegisterDefaultType("ai_" + key, type);
        return type;
    }

    private static AiBrain? BrainForCommand(string reference)
    {
        string id = AiCatalog.IdOfReference(reference);
        if (id.Length == 0) return null;
        if (aiLibrary.Brains.TryGetValue(id, out AiBrain? exact)) return exact;
        string stem = id.StartsWith("ai_", StringComparison.OrdinalIgnoreCase) ? id.Substring(3) : id;
        if (stem.Length > 0 && !stem.Equals(id, StringComparison.OrdinalIgnoreCase) &&
            aiLibrary.Brains.TryGetValue(stem, out AiBrain? stripped)) return stripped;
        AiBrain? prefixed = null;
        foreach (KeyValuePair<string, AiBrain> pair in aiLibrary.Brains)
        {
            if (!pair.Key.Equals(stem, StringComparison.OrdinalIgnoreCase) &&
                !pair.Key.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase)) continue;
            if (prefixed != null) return null;
            prefixed = pair.Value;
        }
        return prefixed;
    }

    // Creates "count" units behind the summoner (they stay in its group and room, so a script counts them together).
    private static void SummonUnits(Instance inst, Enemy enemy, AiSummonOrder order, double now, Action<string, string>? log)
    {
        string typeName = order.Type;
        EnemyType? type = FindSummonType(enemy, typeName, out string key);
        if (type == null)
        {
            if (log != null && reportedBrainFailures.Add("summon|" + enemy.TypeKey + "|" + typeName))
                log("ENEMY", "the AI of '" + enemy.TypeKey + "' summons the type '" + typeName + "', which does not exist");
            return;
        }
        if (order.Ai.Length > 0 && !string.Equals(type.Ai, order.Ai, StringComparison.OrdinalIgnoreCase))
        {
            string alt = key + "~" + order.Ai;
            if (GetEnemyType(alt) == null)
            {
                RegisterDefaultType(alt, new EnemyType
                {
                    Name = type.Name, Template = type.Template, Ai = order.Ai, FreeRoam = type.FreeRoam,
                    HealthMultiplier = type.HealthMultiplier, Boss = type.Boss
                });
            }
            EnemyType? swapped = GetEnemyType(alt);
            if (swapped != null) { type = swapped; key = alt; }
        }

        var summoned = new List<Enemy>();
        int count = Math.Clamp(order.Count, 1, 30);
        for (int i = 0; i < count; i++)
        {
            SummonOffset(enemy, order, i, count, out float x, out float y);
            ConfineTo(enemy.Area, false, ref x, ref y, DestMargin);
            Enemy? fresh = CreateEnemy(inst, key, type, x, y, enemy.FromWave, now, log ?? ((_, _) => { }));
            if (fresh == null) continue;
            fresh.Group = enemy.Group;
            fresh.Area = enemy.Area;
            fresh.Summoner = enemy;
            fresh.SummonUnit = typeName;
            fresh.SummonRole = order.Role ?? "";
            fresh.Leader = enemy;
            summoned.Add(fresh);
        }
        if (summoned.Count == 0) return;
        SendSpawns(inst, summoned, now);
        log?.Invoke("ENEMY", inst.Role + " " + Describe(enemy) + " summoned " + summoned.Count + " x " + key);
    }

    private static void SummonOffset(Enemy enemy, AiSummonOrder order, int index, int count, out float x, out float y)
    {
        string place = (order.Place ?? "behind").ToLowerInvariant();
        if (SummonAtPoint(enemy, place, out x, out y)) return;
        float distance = order.Distance <= 0f ? 5f : order.Distance;
        float step = count <= 1 ? 0f : (index - (count - 1) * 0.5f) * 40f;
        float facing = place switch
        {
            "front" => enemy.Rot + step,
            "left" => enemy.Rot - 90f + step,
            "right" => enemy.Rot + 90f + step,
            "ring" or "around" => order.Angle + (count <= 1 ? 0f : index * 360f / count),
            _ => enemy.Rot + 180f + step
        };
        float rad = facing * Rad;
        x = enemy.X + MathF.Sin(rad) * distance;
        y = enemy.Y + MathF.Cos(rad) * distance;
    }

    // A place "point:id" or "point:formation/id" sits on that formation point, in front of and beside the summoner.
    private static bool SummonAtPoint(Enemy enemy, string place, out float x, out float y)
    {
        x = 0f;
        y = 0f;
        if (!place.StartsWith("point:", StringComparison.OrdinalIgnoreCase)) return false;
        string rest = place.Substring(6);
        int slash = rest.IndexOf('/');
        FormationFile? file = slash >= 0 ? Formations.Library.Get(rest.Substring(0, slash)) : ResolveFormation(enemy);
        file ??= ResolveFormation(enemy);
        if (!FormationMotion.TryPointOffset(place, file, out float localX, out float localY)) return false;
        FormationMotion.Pose pose = FormationMotion.Slot(enemy.X, enemy.Y, enemy.Rot, 0f, localX, localY);
        x = pose.X;
        y = pose.Y;
        return true;
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
