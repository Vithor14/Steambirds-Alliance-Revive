using System.Globalization;

// Game data used by the pilot and plane progression, read from the game's configBinary (GameData/ConfigBinary).
//
// Where each value comes from (names of the client config classes, decompiled Assembly-CSharp):
//   global.xpTargets / pilotXpTargets      plane and pilot level tables (GlobalConfig, XpTargetList)
//   global.realTimePlaneXPProportion       share of each hangar plane's pilot XP earned in real time (Pilot.computedXP)
//   global.xpLimitForMaxLevel              XP cap per kill at max plane level (GameServer.GetXpActuallyAwarded)
//   global.baseSkillLoadoutSlots           skill loadouts without buying more (PlayerData.numSkillLoadoutSlots)
//   global.defaultDamageThreshold          boss damage share needed for loot (ClientPostFightDetails)
//   global.startingClassUnlocks            classes every account starts with
//   global.reliableStats_nearbyPlayerCountRadius  radius of the "nearby players" stat (ClientReceiveReliableStats)
//   pilotScore                             plane XP -> pilot XP conversion per pilot level (PilotScoreConfig)
//   tierConfig.xpTiers / healthTiers       XP and health multipliers per enemy tier (TierConfig)
//   Units/*                                tier, maxHitPoints, xpMultiplier, damageThreshold, showPostFightDetails
//   skills / skillTree                     pilot skills and their groups (SkillConfig, SkillTreeConfig)
//   unlocks / playerclasses                class unlock rules and the unlock item of each class
//   buffs                                  account buffs (BuffConfig)
//   career_rewards / death                 only shown in the log for now (formats still unknown)
//   Texts/<locale>/planeclasses            class names for the death screen
//
// If the configBinary cannot be read, the tables of build 190828 (the old configBinary in OldVersions)
// are used, so XP still works; skill changes are then trusted to the client.
public static class ProgressionData
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ------------------------------------------------------------------ types

    // Same behavior as XpTargetList in the client: Levels[level] = total XP needed for that level.
    public sealed class XpTable
    {
        public int[] Levels { get; }

        public XpTable(IList<(int Level, int Xp)> targets)
        {
            var levels = new int[targets.Count + 2];
            foreach ((int level, int xp) in targets)
            {
                if (level >= 0 && level < levels.Length) levels[level] = xp;
            }
            Levels = levels;
        }

        public int MaxLevel => Levels.Length - 1;

        // Highest level with an XP target. The table always has one empty slot more than the targets
        // (pilot ranks 1..200 make Levels[201] = 0), so this is the last level that commands should set.
        public int TopLevel
        {
            get
            {
                for (int i = Levels.Length - 1; i > 1; i--)
                {
                    if (Levels[i] > 0) return i;
                }
                return Math.Max(1, Levels.Length - 1);
            }
        }

        public bool IsMaxLevel(int level) => level >= Levels.Length - 1;

        public int LevelFromXp(int xp)
        {
            for (int i = 0; i < Levels.Length; i++)
            {
                if (xp < Levels[i]) return i - 1;
            }
            return Levels.Length - 1;
        }

        public int XpFromLevel(int level)
        {
            if (level >= Levels.Length) return int.MaxValue;
            return level >= 0 ? Levels[level] : Levels[0];
        }

        public int XpFromLevelStartToFinish(int level)
        {
            if (IsMaxLevel(level)) return Levels[Levels.Length - 1];
            if (level < 0) level = 0;
            return Levels[level + 1] - Levels[level];
        }

        public int XpFromCurLevelStartToFinish(int xp) => XpFromLevelStartToFinish(LevelFromXp(xp));
    }

    public sealed class ScoreLevel
    {
        public int PilotLevel;
        public readonly List<(int Xp, int Score)> Points = new List<(int Xp, int Score)>();

        // PilotScoreLevelConfig.ScoreFromXp: linear between points, proportional after the last one.
        public int ScoreFromXp(int xp)
        {
            if (Points.Count == 0) return 0;
            for (int i = 0; i < Points.Count; i++)
            {
                if (xp < Points[i].Xp)
                {
                    if (i == 0) return Points[0].Score;
                    (int ax, int ascore) = Points[i - 1];
                    (int bx, int bscore) = Points[i];
                    float t = bx != ax ? Math.Clamp((xp - ax) / (float)(bx - ax), 0f, 1f) : 0f;
                    return RoundToInt(ascore + (bscore - ascore) * t);
                }
            }
            (int lastXp, int lastScore) = Points[Points.Count - 1];
            if (lastXp <= 0) return lastScore;
            return RoundToInt(lastScore / (float)lastXp * xp);
        }
    }

    public sealed class UnitXpInfo
    {
        public string Name = "";
        public int Tier;
        public float BaseHitPoints;
        public float XpMultiplier = 1f;
        public float XpRadius;
        public float DamageThreshold = -1f;
        public bool ShowPostFightDetails;
    }

    public sealed class SkillInfo
    {
        public string Id = "";
        public string Category = "";
        public int Cost = 1;
        public int? CostPerLevel;
        public int MaxLevel = 1;

        public int GetCostPerLevel() => CostPerLevel ?? Cost;
    }

    // SkillGroupRequirementConfig: every field that is set must match (or/not/and nest).
    public sealed class SkillRequirement
    {
        public int? PilotLevelAtLeast;
        public string? ClassLevelPlaneClass;
        public int ClassLevel;
        public List<SkillRequirement>? Or;
        public List<SkillRequirement>? Not;
        public List<SkillRequirement>? And;
    }

    public sealed class SkillGroup
    {
        public string Id = "";
        public List<SkillRequirement> Requirements = new List<SkillRequirement>();
        public List<string> Skills = new List<string>();
    }

    public sealed class UnlockRule
    {
        public bool Always;
        public string ClassName = "";
        public float Amount;
    }

    public sealed class ClassUnlock
    {
        public string ClassToUnlock = "";
        public List<UnlockRule> Conditions = new List<UnlockRule>();
    }

    public sealed class BuffInfo
    {
        public string Id = "";
        public TimeSpan ExpireAfter = TimeSpan.Zero;
        public bool ExpireOnServerLeave;
        public int Stacks = 1;
        public string StackOverflowRule = "";
    }

    // ------------------------------------------------------------------ data

    // Build 190828 (OldVersions\386011-1702765646885248947): used only without a readable configBinary.
    private static readonly int[] FallbackPlaneXp =
    {
        50, 200, 350, 700, 1200, 1900, 2700, 4000, 5500, 7500, 10000, 13000, 17000, 20000, 25000, 30000, 36000, 45000, 54000
    };

    private static readonly int[] FallbackPilotXp =
    {
        0, 1, 2, 3, 7, 15, 25, 36, 50, 74, 100, 131, 164, 202, 244, 308, 377, 452, 532, 619, 711, 809, 960, 1118,
        1286, 1461, 1645, 1838, 2038, 2247, 2465, 2690, 3046, 3414, 3794, 4187, 4593, 5012, 5443, 5887, 6344, 6814,
        7296, 7790, 8298, 9226, 10176, 11149, 12144, 13162, 14203, 15266, 16352, 17461, 18592, 19746, 20923, 22122,
        23344, 24588, 26959, 29372, 31828, 34326, 36866, 39449, 42074, 44741, 47451, 50203, 52997, 55834, 58713,
        61634, 64598, 67604, 70652, 73743, 76876, 80051, 83269, 86529, 89831, 93176, 96563, 99993, 103464, 106978,
        110535, 114134, 117775, 123091, 128469, 133907, 139407, 144968, 150590, 156273, 162017, 167822, 173689,
        179616, 185605, 191654, 197765, 203937, 210170, 216464, 222819, 229236, 235713, 242252, 248851, 255512,
        262234, 269017, 275861, 282766, 289733, 296760, 303849, 310998, 318209, 325481, 332814, 340208, 347663,
        355179, 362757, 370395, 378095, 385856, 393677, 401560, 409504, 417510, 425576, 433703, 441892, 450141,
        458452, 466824, 475257, 483751, 492306, 500922, 509599, 518338, 527137, 535998, 544920, 553903, 562947,
        572052, 581218, 590445, 599734, 609083, 618494, 627966, 637498, 647092, 656748, 666464, 676241, 686079,
        695979, 705939, 715961, 726044, 736188, 746393, 756659, 766986, 777375, 787824, 798335, 808906, 819539,
        830233, 840988, 851804, 862682, 873620, 884619, 895680, 906802, 917984, 929228, 940533, 951899, 963326,
        974815, 986364, 997975, 1009646, 1021379, 1033173, 1045028, 1056944
    };

    private static readonly float[] FallbackXpTiers = { 0.166f, 0.4f, 0.733f, 1.133f, 1.666f, 2.266f, 3.0f, 3.833f, 5.766f, 8.833f, 12.8f, 16.666f };
    private static readonly float[] FallbackHealthTiers = { 1f, 2f, 4f, 6f, 9f, 12f, 16f, 20f, 30f, 44f, 63f, 90f };

    // Without pilotScore (old builds) plane XP turns into pilot XP at this rate (a max level plane ~ 216 pilot XP).
    private const float FallbackPilotXpPerPlaneXp = 0.004f;

    public static bool FromConfigBinary { get; private set; }
    public static XpTable PlaneLevels { get; private set; } = BuildTable(FallbackPlaneXp, 2);
    public static XpTable PilotLevels { get; private set; } = BuildTable(FallbackPilotXp, 1);
    public static List<ScoreLevel> PilotScore { get; private set; } = new List<ScoreLevel>();
    public static float[] XpTiers { get; private set; } = FallbackXpTiers;
    public static float[] HealthTiers { get; private set; } = FallbackHealthTiers;
    public static float RealTimePlaneXpProportion { get; private set; } = 0.65f;
    public static int XpLimitForMaxLevel { get; private set; } = 40000;
    // Retiring a plane pays its not-yet-counted pilot XP also as currency: XP / PilotXpPer1Currency of PilotXpCurrencyId, only from the threshold.
    public static int PilotXpPer1Currency { get; private set; }
    public static int PilotXpCurrencyAwardThreshold { get; private set; } = 10;
    public static string PilotXpCurrencyId { get; private set; } = "scrap";
    public static int BaseSkillLoadoutSlots { get; private set; } = 1;
    public static float DefaultDamageThreshold { get; private set; } = 0.005f;
    public static float NearbyPlayersRadius { get; private set; } = 15f;
    public static float NearbyPlayersTimeout { get; private set; } = 3f;
    public static List<string> StartingClassUnlocks { get; private set; } = new List<string>();
    public static Dictionary<string, UnitXpInfo> Units { get; private set; } = new Dictionary<string, UnitXpInfo>(StringComparer.Ordinal);
    public static Dictionary<string, SkillInfo> Skills { get; private set; } = new Dictionary<string, SkillInfo>(StringComparer.Ordinal);
    public static List<SkillGroup> SkillGroups { get; private set; } = new List<SkillGroup>();
    public static Dictionary<string, SkillGroup> GroupBySkill { get; private set; } = new Dictionary<string, SkillGroup>(StringComparer.Ordinal);
    public static List<ClassUnlock> ClassUnlocks { get; private set; } = new List<ClassUnlock>();
    public static Dictionary<string, string> ClassUnlockItems { get; private set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public static Dictionary<string, BuffInfo> Buffs { get; private set; } = new Dictionary<string, BuffInfo>(StringComparer.Ordinal);
    public static List<string> CareerRewardIds { get; private set; } = new List<string>();

    // locale -> ("class" or "class/variant") -> display name
    private static Dictionary<string, Dictionary<string, string>> classNames =
        new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

    public static bool HasSkillData => Skills.Count > 0 && SkillGroups.Count > 0;

    // ------------------------------------------------------------------ loading

    public static void Load(Action<string, string> log)
    {
        if (!ConfigBinary.Loaded) ConfigBinary.Load(log);
        if (!ConfigBinary.Loaded)
        {
            FromConfigBinary = false;
            ClassUnlockItems = DefaultUnlockItems();
            log("PROGRESSION", "no configBinary: built-in XP tables (build 190828), skills not validated on the server, " +
                "only the walker unlocked");
            return;
        }

        FromConfigBinary = true;
        ConfigNode global = ConfigBinary.Get("global");

        XpTable? plane = ReadTable(global["xpTargets"]);
        XpTable? pilot = ReadTable(global["pilotXpTargets"]);
        if (plane != null) PlaneLevels = plane;
        if (pilot != null) PilotLevels = pilot;
        RealTimePlaneXpProportion = global["realTimePlaneXPProportion"].AsFloat(0.65f);
        XpLimitForMaxLevel = global["xpLimitForMaxLevel"].AsInt(XpLimitForMaxLevel);
        PilotXpPer1Currency = global["pilotXpPer1Currency"].AsInt(PilotXpPer1Currency);
        PilotXpCurrencyAwardThreshold = global["pilotXpCurrencyAwardThreshold"].AsInt(PilotXpCurrencyAwardThreshold);
        PilotXpCurrencyId = global["pilotXpCurrencyId"].AsString(PilotXpCurrencyId);
        BaseSkillLoadoutSlots = Math.Max(1, global["baseSkillLoadoutSlots"].AsInt(1));
        DefaultDamageThreshold = global["defaultDamageThreshold"].AsFloat(DefaultDamageThreshold);
        NearbyPlayersRadius = global["reliableStats_nearbyPlayerCountRadius"].AsFloat(15f);
        NearbyPlayersTimeout = global["reliableStats_nearbyPlayerCountTimeout"].AsFloat(3f);
        StartingClassUnlocks = global["startingClassUnlocks"].AsStringList();

        ConfigNode tiers = ConfigBinary.Get("tierConfig");
        float[]? xpTiers = ReadTiers(tiers["xpTiers"]);
        float[]? healthTiers = ReadTiers(tiers["healthTiers"]);
        if (xpTiers != null) XpTiers = xpTiers;
        if (healthTiers != null) HealthTiers = healthTiers;

        PilotScore = ReadPilotScore(ConfigBinary.Get("pilotScore"));
        Units = ReadUnits();
        ReadSkills(log);
        ReadUnlocks();
        Buffs = ReadBuffs();
        ReadClassNames();
        CareerRewardIds = ConfigBinary.Get("career_rewards").Items.Select(n => n["id"].AsString("")).Where(s => s.Length > 0).ToList();

        log("PROGRESSION", "game data: planes up to level " + PlaneLevels.TopLevel + " (" + PlaneLevels.XpFromLevel(PlaneLevels.TopLevel) +
            " XP), pilots up to level " + PilotLevels.TopLevel + ", pilotScore " +
            (PilotScore.Count > 0 ? PilotScore.Count + " pilot level(s)" : "MISSING (simple conversion " + FallbackPilotXpPerPlaneXp.ToString(Inv) + " pilot XP per plane XP)") +
            ", XP tiers " + XpTiers.Length + ", " + Units.Count + " unit(s), " + Skills.Count + " skill(s) in " + SkillGroups.Count +
            " group(s), " + ClassUnlocks.Count + " unlock rule(s), " + Buffs.Count + " buff(s), " + CareerRewardIds.Count +
            " career reward(s), real-time share " + RealTimePlaneXpProportion.ToString("0.##", Inv));

        LogDiagnostics(log);
    }

    // Small sections of the configBinary that still need study, written to the log so they end up in the report.
    private static void LogDiagnostics(Action<string, string> log)
    {
        log("CONFIG", "career_rewards = " + ConfigBinary.Get("career_rewards").ToJson(3000));
        log("CONFIG", "death = " + ConfigBinary.Get("death").ToJson(3000));
        log("CONFIG", "unlocks = " + ConfigBinary.Get("unlocks").ToJson(4000));
        log("CONFIG", "startingClassUnlocks = " + string.Join(", ", StartingClassUnlocks));
        if (PilotScore.Count > 0)
        {
            ScoreLevel first = PilotScore[0];
            ScoreLevel last = PilotScore[PilotScore.Count - 1];
            log("CONFIG", "pilotScore: level " + first.PilotLevel + " " + DescribePoints(first) + " ... level " + last.PilotLevel + " " + DescribePoints(last));
        }
    }

    private static string DescribePoints(ScoreLevel level)
    {
        return "[" + string.Join(" ", level.Points.Select(p => p.Xp + "->" + p.Score)) + "]";
    }

    private static XpTable BuildTable(int[] xps, int firstLevel)
    {
        var targets = new List<(int, int)>();
        for (int i = 0; i < xps.Length; i++) targets.Add((firstLevel + i, xps[i]));
        return new XpTable(targets);
    }

    private static XpTable? ReadTable(ConfigNode node)
    {
        var targets = new List<(int, int)>();
        foreach (ConfigNode item in node.Items)
        {
            int level = item["level"].AsInt(-1);
            int xp = item["xp"].AsInt(-1);
            if (level >= 0 && xp >= 0) targets.Add((level, xp));
        }
        return targets.Count > 0 ? new XpTable(targets) : null;
    }

    private static float[]? ReadTiers(ConfigNode node)
    {
        var values = new List<float>();
        foreach (ConfigNode item in node.Items)
        {
            int tier = item["tier"].AsInt(-1);
            if (tier < 0) continue;
            while (values.Count <= tier) values.Add(0f);
            values[tier] = item["value"].AsFloat(0f);
        }
        return values.Count > 0 ? values.ToArray() : null;
    }

    private static List<ScoreLevel> ReadPilotScore(ConfigNode node)
    {
        var result = new List<ScoreLevel>();
        foreach (ConfigNode levelNode in node["PilotLevels"].Items)
        {
            var level = new ScoreLevel { PilotLevel = levelNode["PilotLevel"].AsInt(0) };
            foreach (ConfigNode point in levelNode["ScoreConversion"].Items)
                level.Points.Add((point["xp"].AsInt(0), point["score"].AsInt(0)));
            level.Points.Sort((a, b) => a.Xp.CompareTo(b.Xp));
            if (level.Points.Count > 0) result.Add(level);
        }
        result.Sort((a, b) => a.PilotLevel.CompareTo(b.PilotLevel));
        return result;
    }

    private static Dictionary<string, UnitXpInfo> ReadUnits()
    {
        var result = new Dictionary<string, UnitXpInfo>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ConfigNode> file in ConfigBinary.WithPrefix("Units/"))
        {
            IEnumerable<ConfigNode> unitNodes = file.Value.IsList ? file.Value.Items : new[] { file.Value };
            foreach (ConfigNode unit in unitNodes)
            {
                string name = unit["templateName"].AsString("");
                if (name.Length == 0) continue;
                result[name] = new UnitXpInfo
                {
                    Name = name,
                    Tier = Math.Max(0, unit["tier"].AsInt(0)),
                    BaseHitPoints = unit["maxHitPoints"].AsFloat(0f),
                    XpMultiplier = unit["xpMultiplier"].AsFloat(1f),
                    XpRadius = unit["xpRadius"].AsFloat(0f),
                    DamageThreshold = unit["damageThreshold"].AsFloat(-1f),
                    ShowPostFightDetails = unit["showPostFightDetails"].AsBool(false)
                };
            }
        }
        return result;
    }

    private static void ReadSkills(Action<string, string> log)
    {
        var skills = new Dictionary<string, SkillInfo>(StringComparer.Ordinal);
        foreach (ConfigNode node in ConfigBinary.Get("skills").Items)
        {
            string id = node["id"].AsString("");
            if (id.Length == 0) continue;
            skills[id] = new SkillInfo
            {
                Id = id,
                Category = node["category"].AsString(""),
                Cost = node["cost"].AsInt(1),
                CostPerLevel = node["costPerLevel"].AsNullableInt(),
                MaxLevel = node["maxLevel"].AsInt(1)
            };
        }

        var groups = new List<SkillGroup>();
        var bySkill = new Dictionary<string, SkillGroup>(StringComparer.Ordinal);
        foreach (ConfigNode node in ConfigBinary.Get("skillTree")["groups"].Items)
        {
            var group = new SkillGroup
            {
                Id = node["id"].AsString(""),
                Requirements = ReadRequirements(node["requirements"]),
                Skills = node["skills"].AsStringList()
            };
            groups.Add(group);
            foreach (string skill in group.Skills)
            {
                if (bySkill.ContainsKey(skill))
                    log("PROGRESSION", "skill '" + skill + "' appears in more than one group (" + bySkill[skill].Id + ", " + group.Id + ")");
                bySkill[skill] = group;
            }
        }

        Skills = skills;
        SkillGroups = groups;
        GroupBySkill = bySkill;
    }

    private static List<SkillRequirement> ReadRequirements(ConfigNode node)
    {
        var result = new List<SkillRequirement>();
        foreach (ConfigNode item in node.Items)
        {
            var requirement = new SkillRequirement { PilotLevelAtLeast = item["pilotLevelAboveOrEq"].AsNullableInt() };
            ConfigNode classLevel = item["classLevelReached"];
            if (classLevel.IsDictionary)
            {
                requirement.ClassLevelPlaneClass = classLevel["planeClass"].AsString("");
                requirement.ClassLevel = classLevel["level"].AsInt(0);
            }
            if (item["or"].IsList) requirement.Or = ReadRequirements(item["or"]);
            if (item["not"].IsList) requirement.Not = ReadRequirements(item["not"]);
            if (item["and"].IsList) requirement.And = ReadRequirements(item["and"]);
            result.Add(requirement);
        }
        return result;
    }

    private static void ReadUnlocks()
    {
        var unlocks = new List<ClassUnlock>();
        foreach (ConfigNode node in ConfigBinary.Get("unlocks").Items)
        {
            var unlock = new ClassUnlock { ClassToUnlock = node["classToUnlock"].AsString("") };
            if (unlock.ClassToUnlock.Length == 0) continue;
            foreach (ConfigNode condition in node["conditions"].Items)
            {
                string type = condition["type"].AsString("");
                unlock.Conditions.Add(new UnlockRule
                {
                    Always = string.Equals(type, "always", StringComparison.OrdinalIgnoreCase),
                    ClassName = condition["classname"].AsString(""),
                    Amount = condition["amount"].AsFloat(0f)
                });
            }
            unlocks.Add(unlock);
        }
        ClassUnlocks = unlocks;

        var items = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ConfigNode> pair in ConfigBinary.Get("playerclasses").Pairs)
        {
            string item = pair.Value["unlockItemId"].AsString("");
            items[pair.Key] = item.Length > 0 ? item : "playerclass_" + pair.Key;
        }
        ClassUnlockItems = items.Count > 0 ? items : DefaultUnlockItems();
    }

    private static Dictionary<string, string> DefaultUnlockItems()
    {
        var items = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string playerClass in PlayerData.ClassesNetIdShort.Keys) items[playerClass] = "playerclass_" + playerClass;
        return items;
    }

    private static Dictionary<string, BuffInfo> ReadBuffs()
    {
        var result = new Dictionary<string, BuffInfo>(StringComparer.Ordinal);
        foreach (ConfigNode node in ConfigBinary.Get("buffs").Items)
        {
            string id = node["id"].AsString("");
            if (id.Length == 0) continue;
            result[id] = new BuffInfo
            {
                Id = id,
                ExpireAfter = node["expireAfter"].AsTimeSpan(TimeSpan.Zero),
                ExpireOnServerLeave = node["expireOnServerLeave"].AsBool(false),
                Stacks = node["stacks"].AsInt(1),
                StackOverflowRule = node["stackOverflowRule"].AsString("")
            };
        }
        return result;
    }

    private static void ReadClassNames()
    {
        var names = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, ConfigNode> file in ConfigBinary.WithPrefix("Texts/"))
        {
            // "Texts/en-US/planeclasses"
            string[] parts = file.Key.Split('/');
            if (parts.Length != 3 || parts[2] != "planeclasses") continue;
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, ConfigNode> planeClass in file.Value.Pairs)
            {
                string name = planeClass.Value["name"].AsString("");
                if (name.Length > 0) table[planeClass.Key] = name;
                foreach (KeyValuePair<string, ConfigNode> variant in planeClass.Value["variants"].Pairs)
                {
                    string variantName = variant.Value["name"].AsString("");
                    if (variantName.Length > 0) table[planeClass.Key + "/" + variant.Key] = variantName;
                }
            }
            names[parts[1]] = table;
        }
        classNames = names;
    }

    // ------------------------------------------------------------------ queries

    // Pilot XP the plane XP is worth (GameServer.PilotXpFromPlaneXp -> PilotScoreConfig.ScoreFromXp).
    // Same result as the client for every XP (also 0: a new plane is worth the first point of the table), because
    // the rank follows Pilot.computedXP, which the client adds up with this function.
    public static int PilotXpFromPlaneXp(int pilotLevel, int planeXp)
    {
        if (PilotScore.Count == 0) return planeXp <= 0 ? 0 : RoundToInt(planeXp * FallbackPilotXpPerPlaneXp);

        // PilotScoreConfig.GetScoreLevelConfig: first/last outside the range, exact level inside it.
        // (The client's binary search only ends when every level exists; here the closest lower level is used.)
        ScoreLevel chosen = PilotScore[0];
        foreach (ScoreLevel level in PilotScore)
        {
            if (level.PilotLevel <= pilotLevel) chosen = level;
            else break;
        }
        return chosen.ScoreFromXp(planeXp);
    }

    // XP an enemy is worth. Rebuilt from the data (the formula was only on the original server):
    // base health x xpTiers[tier] x xpMultiplier. xpTiers is ~0.19 x healthTiers, so it is ~19% of the real health.
    public static float EnemyXp(string templateName, int tierFromCatalog, float maxHitPointsFromCatalog)
    {
        if (Units.TryGetValue(templateName, out UnitXpInfo? unit))
            return unit.BaseHitPoints * TierValue(XpTiers, unit.Tier) * unit.XpMultiplier;

        // Unit not in the configBinary (or no configBinary): undo the tier health multiplier of the catalog.
        float healthMultiplier = TierValue(HealthTiers, tierFromCatalog);
        float baseHitPoints = healthMultiplier > 0f ? maxHitPointsFromCatalog / healthMultiplier : maxHitPointsFromCatalog;
        return baseHitPoints * TierValue(XpTiers, tierFromCatalog);
    }

    public static UnitXpInfo? GetUnit(string templateName)
    {
        return Units.TryGetValue(templateName, out UnitXpInfo? unit) ? unit : null;
    }

    private static float TierValue(float[] table, int tier)
    {
        if (table.Length == 0) return 0f;
        if (tier < 0) tier = 0;
        if (tier > table.Length - 1) tier = table.Length - 1;
        return table[tier];
    }

    public static string UnlockItemOf(string planeClass)
    {
        return ClassUnlockItems.TryGetValue(planeClass, out string? item) ? item : "playerclass_" + planeClass;
    }

    // Class name for the death screen, in the player's language when the game has it.
    public static string ClassDisplayName(string planeClass, string variant, string locale)
    {
        foreach (string candidate in LocaleCandidates(locale))
        {
            if (!classNames.TryGetValue(candidate, out Dictionary<string, string>? table)) continue;
            if (table.TryGetValue(planeClass + "/" + variant, out string? variantName)) return variantName;
            if (table.TryGetValue(planeClass, out string? className)) return className;
        }
        return planeClass;
    }

    private static IEnumerable<string> LocaleCandidates(string locale)
    {
        if (!string.IsNullOrWhiteSpace(locale))
        {
            yield return locale;
            int dash = locale.IndexOf('-');
            string prefix = dash > 0 ? locale.Substring(0, dash) : locale;
            foreach (string known in classNames.Keys)
            {
                if (known.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) yield return known;
            }
        }
        yield return "en-US";
    }

    // Mathf.RoundToInt rounds halves to even, like Math.Round.
    public static int RoundToInt(float value)
    {
        return (int)Math.Round(value);
    }
}
