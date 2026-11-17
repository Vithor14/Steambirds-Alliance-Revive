namespace SteambirdsLauncher.Core.GameData;

// What the launcher takes from the game's configBinary, read once per game folder (about half a second).
// Without the game (or with a configBinary it cannot read) the lists fall back to what the project already knows,
// so every screen keeps working; Error says why.
public sealed class GameConfig
{
    public sealed class Locale
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public bool Hidden { get; init; }
        public override string ToString() => Name.Length > 0 ? Name + " (" + Id + ")" : Id;
    }

    public sealed class PlaneClass
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string UnlockItem { get; init; } = "";
        public bool PreRelease { get; init; }
        public List<KeyValuePair<string, string>> Variants { get; } = new List<KeyValuePair<string, string>>();   // id, name
        public override string ToString() => Name.Length > 0 && Name != Id ? Name + " (" + Id + ")" : Id;
    }

    // Same as XpTargetList in the client (and XpTable in the server): Levels[level] = total XP needed for that level.
    public sealed class XpTable
    {
        public XpTable(IList<(int Level, int Xp)> targets)
        {
            var levels = new int[targets.Count + 2];
            foreach ((int level, int xp) in targets)
            {
                if (level >= 0 && level < levels.Length) levels[level] = xp;
            }
            Levels = levels;
        }

        public int[] Levels { get; }

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
            if (level >= Levels.Length) return Levels[Levels.Length - 1];
            return level >= 0 ? Levels[level] : Levels[0];
        }
    }

    // pilotScore: plane XP -> pilot XP, per pilot rank (PilotScoreConfig).
    public sealed class ScoreLevel
    {
        public int PilotLevel { get; init; }
        public List<(int Xp, int Score)> Points { get; } = new List<(int Xp, int Score)>();

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

    // Classes of build 201219, in the order the game unlocks them (used when there is no configBinary).
    private static readonly string[] KnownClasses =
    {
        "walker", "paladin", "support", "olfaithful", "medic", "daggerknight", "assault", "tank",
        "pirate", "wizard", "engineer", "siegetank", "rapier", "duster", "vampire"
    };

    // Without pilotScore (old builds) plane XP turns into pilot XP at this rate (same as the server).
    private const float FallbackPilotXpPerPlaneXp = 0.004f;

    public bool Loaded { get; private set; }
    public string Error { get; private set; } = "";
    public string SourcePath { get; private set; } = "";

    public List<Locale> Locales { get; } = new List<Locale>();
    public List<PlaneClass> Classes { get; } = new List<PlaneClass>();
    public List<string> Currencies { get; } = new List<string>();
    public List<string> Ingredients { get; } = new List<string>();
    public XpTable? PlaneLevels { get; private set; }
    public XpTable? PilotLevels { get; private set; }
    public List<ScoreLevel> PilotScore { get; } = new List<ScoreLevel>();
    public float RealTimePlaneXpProportion { get; private set; } = 0.65f;

    public bool HasXpTables => PlaneLevels != null && PilotLevels != null;

    public static string ConfigBinaryPath(string gameFolder) =>
        Path.Combine(gameFolder, "Steambirds_Data", "StreamingAssets", "configBinary");

    public static GameConfig Load(string? gameFolder)
    {
        var config = new GameConfig();
        try
        {
            // The game's own file first; on a host PC without the game, the copy kept with the server (server\data).
            string? path = null;
            if (!string.IsNullOrWhiteSpace(gameFolder) && File.Exists(ConfigBinaryPath(gameFolder))) path = ConfigBinaryPath(gameFolder);
            else if (File.Exists(ServerGameData.ServerCopyPath)) path = ServerGameData.ServerCopyPath;
            if (path == null)
            {
                if (string.IsNullOrWhiteSpace(gameFolder)) throw new FileNotFoundException("no game folder");
                throw new FileNotFoundException("configBinary not found", ConfigBinaryPath(gameFolder));
            }
            Dictionary<string, ConfigNode> files = ConfigBinaryReader.Parse(File.ReadAllBytes(path));
            config.SourcePath = path;
            config.Read(files);
            config.Loaded = true;
        }
        catch (Exception e)
        {
            config.Error = e.Message;
        }
        config.AddFallbacks();
        return config;
    }

    public static GameConfig Empty()
    {
        var config = new GameConfig { Error = "not loaded" };
        config.AddFallbacks();
        return config;
    }

    // ------------------------------------------------------------------ queries

    public PlaneClass? FindClass(string id) => Classes.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    // Unlock item of a class ("medic" -> "playerclass_medic"); class ids and unlock items both work.
    public string UnlockItemOf(string classOrItem)
    {
        PlaneClass? planeClass = FindClass(classOrItem);
        return planeClass != null ? planeClass.UnlockItem : classOrItem;
    }

    // Same as the server (ProgressionData.PilotXpFromPlaneXp).
    public int PilotXpFromPlaneXp(int pilotLevel, int planeXp)
    {
        if (PilotScore.Count == 0) return planeXp <= 0 ? 0 : RoundToInt(planeXp * FallbackPilotXpPerPlaneXp);
        ScoreLevel chosen = PilotScore[0];
        foreach (ScoreLevel level in PilotScore)
        {
            if (level.PilotLevel <= pilotLevel) chosen = level;
            else break;
        }
        return chosen.ScoreFromXp(planeXp);
    }

    // Pilot XP the hangar planes give in real time (PilotProgression.RealtimePilotXp in the server).
    public float RealtimePilotXp(IEnumerable<int> planeXps, int pilotLevel)
    {
        float sum = 0f;
        foreach (int xp in planeXps) sum += RealTimePlaneXpProportion * PilotXpFromPlaneXp(pilotLevel, xp);
        return sum;
    }

    // Base XP that puts the pilot XP at the start of a rank (what the /rank command does).
    public int BaselineForRankStart(IEnumerable<int> planeXps, int rank)
    {
        if (PilotLevels == null) return 0;
        float baseline = PilotLevels.XpFromLevel(rank) - RealtimePilotXp(planeXps, rank);
        return Math.Max(0, RoundToInt(baseline));
    }

    public static int RoundToInt(float value) => (int)Math.Round(value);

    // ------------------------------------------------------------------ reading

    private void Read(Dictionary<string, ConfigNode> files)
    {
        ConfigNode Get(string name) => files.TryGetValue(name, out ConfigNode? node) ? node : ConfigNode.Missing;

        // Game languages (TextMachine.LocalesConfig).
        foreach (ConfigNode locale in Get("Texts/locales")["locales"].Items)
        {
            string id = locale["id"].AsString("");
            if (id.Length == 0) continue;
            Locales.Add(new Locale { Id = id, Name = locale["name"].AsString(id), Hidden = locale["hidden"].AsBool(false) });
        }

        // Plane classes: playerclasses, names from Texts/en-US/planeclasses, in the order of the "unlocks" rules.
        ConfigNode names = Get("Texts/en-US/planeclasses");
        var order = new List<string>();
        foreach (ConfigNode unlock in Get("unlocks").Items)
        {
            string id = unlock["classToUnlock"].AsString("");
            if (id.Length > 0 && !order.Contains(id)) order.Add(id);
        }
        var classes = new List<PlaneClass>();
        foreach (KeyValuePair<string, ConfigNode> pair in Get("playerclasses").Pairs)
        {
            string unlockItem = pair.Value["unlockItemId"].AsString("");
            var planeClass = new PlaneClass
            {
                Id = pair.Key,
                Name = names[pair.Key]["name"].AsString(pair.Value["name"].AsString(pair.Key)),
                UnlockItem = unlockItem.Length > 0 ? unlockItem : "playerclass_" + pair.Key,
                PreRelease = pair.Value["preRelease"].AsBool(false)
            };
            foreach (KeyValuePair<string, ConfigNode> variant in names[pair.Key]["variants"].Pairs)
                planeClass.Variants.Add(new KeyValuePair<string, string>(variant.Key, variant.Value["name"].AsString(variant.Key)));
            classes.Add(planeClass);
        }
        Classes.AddRange(classes.OrderBy(c => order.IndexOf(c.Id) < 0 ? int.MaxValue : order.IndexOf(c.Id)).ThenBy(c => c.Id, StringComparer.Ordinal));

        // Currencies: the store config (StoreConfig.currencies) and the recycling currency (mulching.mulchCurrency).
        foreach (KeyValuePair<string, ConfigNode> file in files)
        {
            ConfigNode currencies = file.Value["currencies"];
            if (!file.Value.IsDictionary || !currencies.IsList || (!file.Value.Has("stores") && !file.Value.Has("products"))) continue;
            foreach (ConfigNode currency in currencies.Items)
            {
                string id = currency["id"].AsString("");
                if (id.Length > 0 && !Currencies.Contains(id)) Currencies.Add(id);
            }
        }
        string mulch = Get("mulching")["mulchCurrency"].AsString("");
        if (mulch.Length > 0 && !Currencies.Contains(mulch)) Currencies.Insert(0, mulch);

        // Ingredients (IngredientConfig list).
        foreach (ConfigNode ingredient in Get("ingredients").Items)
        {
            string id = ingredient["id"].AsString("");
            if (id.Length > 0 && !Ingredients.Contains(id)) Ingredients.Add(id);
        }

        // XP tables (global.xpTargets / pilotXpTargets) and the plane XP -> pilot XP conversion.
        ConfigNode global = Get("global");
        PlaneLevels = ReadTable(global["xpTargets"]);
        PilotLevels = ReadTable(global["pilotXpTargets"]);
        RealTimePlaneXpProportion = global["realTimePlaneXPProportion"].AsFloat(0.65f);
        foreach (ConfigNode levelNode in Get("pilotScore")["PilotLevels"].Items)
        {
            var level = new ScoreLevel { PilotLevel = levelNode["PilotLevel"].AsInt(0) };
            foreach (ConfigNode point in levelNode["ScoreConversion"].Items)
                level.Points.Add((point["xp"].AsInt(0), point["score"].AsInt(0)));
            level.Points.Sort((a, b) => a.Xp.CompareTo(b.Xp));
            if (level.Points.Count > 0) PilotScore.Add(level);
        }
        PilotScore.Sort((a, b) => a.PilotLevel.CompareTo(b.PilotLevel));
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

    private void AddFallbacks()
    {
        if (Locales.Count == 0) Locales.Add(new Locale { Id = "en-US", Name = "English" });
        if (Classes.Count == 0)
        {
            foreach (string id in KnownClasses)
                Classes.Add(new PlaneClass { Id = id, Name = id, UnlockItem = "playerclass_" + id });
        }
        foreach (string currency in new[] { "newscrap", "gold" })
        {
            if (!Currencies.Contains(currency)) Currencies.Add(currency);
        }
    }
}
