using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lidgren.Network;

// Templates from units_config.txt, the enemies.json reader and the unit catalog.
public static partial class Enemies
{
    // ================================================================== templates

    // Resolves the template of a type: options separated by |, the first one that exists in the catalog wins.
    private static GameUnit? Resolver(EnemyType type, out string reason)
    {
        string key = (type.Template ?? "") + "#" + type.Hash.ToString(Inv);
        if (resolvedTemplates.TryGetValue(key, out GameUnit? ready))
        {
            reason = "";
            return ready;
        }

        foreach (string raw in (type.Template ?? "").Split('|'))
        {
            string option = raw.Trim();
            if (option.Length == 0) continue;

            GameUnit? found = option.StartsWith("auto:", StringComparison.OrdinalIgnoreCase)
                ? PickAutomatic(option.Substring(5).Trim().ToLowerInvariant())
                : FindInCatalog(option);
            if (found != null)
            {
                resolvedTemplates[key] = found;
                reason = "";
                return found;
            }
        }

        if (type.Hash != 0)
        {
            GameUnit synthetic = Synthetic(type.Hash);
            resolvedTemplates[key] = synthetic;
            reason = "";
            return synthetic;
        }

        reason = catalog.Count == 0
            ? "the catalog " + CatalogPath() + " does not exist (it comes in the server's data folder)"
            : "no option of '" + type.Template + "' exists in the catalog (in the chat: /catalog FILTER)";
        return null;
    }

    // What the open world needs to know about a unit of units_config.txt before it puts it on the map.
    // Null when the name is not in the catalog.
    public sealed record UnitSummary(string Name, int Tier, bool Armed, bool Boss, bool Wall, bool Player, bool Invisible, float MaxHealth);

    public static UnitSummary? FindUnit(string name)
    {
        lock (Game.Lock)
        {
            GameUnit? u = FindInCatalog(name);
            if (u == null) return null;
            return new UnitSummary(u.Name, u.Tier, u.Armed, u.Boss, u.Wall, u.Player,
                u.Art.StartsWith("!EnemyInvisible", StringComparison.OrdinalIgnoreCase), u.MaxHp);
        }
    }

    private static string FailureReason(EnemyType type)
    {
        Resolver(type, out string reason);
        return reason.Length > 0 ? reason : "enemy limit or error (see the server console)";
    }

    private static GameUnit? FindInCatalog(string name)
    {
        if (catalog.TryGetValue(name, out GameUnit? exact)) return exact;
        return catalog.Values.FirstOrDefault(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    // auto:weak, auto:medium, auto:strong, auto:turret and auto:boss.
    // Candidates: units with a gun, collision and health, that are not a wall or a player plane.
    // If there are island mission enemies (islandQuestEnemies), the choice is among them.
    private static GameUnit? PickAutomatic(string type)
    {
        List<GameUnit> armed = catalog.Values
            .Where(u => u.Armed && !u.Wall && !u.Player && u.MaxHp > 0f && u.Radius > 0f)
            .OrderBy(u => u.MaxHp).ThenBy(u => u.Name, StringComparer.Ordinal)
            .ToList();
        if (armed.Count == 0) return null;

        List<GameUnit> fromIsland = armed.Where(u => u.IslandMin >= 0).ToList();
        List<GameUnit> choiceBase = fromIsland.Count >= 3 ? fromIsland : armed;
        List<GameUnit> mobile = choiceBase.Where(u => u.VelMax > 0.1f && !u.Boss).ToList();
        List<GameUnit> fixedOnes = armed.Where(u => u.VelMax <= 0.1f && !u.Boss).ToList();

        switch (type)
        {
            case "weak":
            {
                // If the game has enemies with "tutorial" in the name, they come first.
                GameUnit? fromTutorial = armed.FirstOrDefault(u => u.VelMax > 0.1f && !u.Boss &&
                    u.Name.Contains("tutorial", StringComparison.OrdinalIgnoreCase));
                return fromTutorial ?? mobile.FirstOrDefault() ?? fixedOnes.FirstOrDefault();
            }
            case "medium":
                return Percentile(mobile, 0.5f) ?? fixedOnes.FirstOrDefault();
            case "strong":
                return Percentile(mobile, 0.85f) ?? fixedOnes.LastOrDefault();
            case "turret":
                return fixedOnes.FirstOrDefault();
            case "boss":
            {
                // Marked as a boss (after-fight or own music): the weakest of them.
                // Without a mark: the one with the most guns.
                GameUnit? marked = armed.FirstOrDefault(u => u.Boss);
                return marked ?? armed.OrderByDescending(u => u.Guns.Count).ThenBy(u => u.MaxHp).FirstOrDefault();
            }
        }
        return null;
    }

    private static GameUnit? Percentile(List<GameUnit> list, float p)
    {
        if (list.Count == 0) return null;
        int index = (int)MathF.Round((list.Count - 1) * p);
        return list[Math.Clamp(index, 0, list.Count - 1)];
    }

    // Unit without a catalog, only with the hash written in enemies.json. Test values.
    private static GameUnit Synthetic(short hash)
    {
        var u = new GameUnit
        {
            Name = "(hash " + hash.ToString(Inv) + ")",
            Hash = hash,
            MaxHp = 100f,
            VelMin = 4f,
            VelMax = 9f,
            Turn = 120f,
            Radius = 1f,
            Synthetic = true
        };
        u.Guns.Add(new GameGun
        {
            Index = 0, Name = "gun0", FireRate = 2f, Burst = 3, Reload = 1.5f, Damage = 1f,
            Speed = 18f, Duration = 1.2f, Range = 20f, Multi = 1
        });
        return u;
    }

    // ================================================================== files

    private static string CatalogPath()
    {
        return GameDataFiles.FilePath("units_config.txt");
    }

    private static string ConfigPath()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "enemies.json");
    }

    // Reads (or rereads, if it changed) units_config.txt. Lines separated by TAB (see the file header).
    private static void LoadCatalog(Action<string, string> log)
    {
        string path = CatalogPath();
        try
        {
            if (!File.Exists(path))
            {
                if (!warnedMissingCatalog)
                {
                    warnedMissingCatalog = true;
                    log("ENEMY", path + " does not exist yet: without it enemies cannot be created. " +
                        "It comes in the server's data folder (Server\\data).");
                }
                return;
            }

            DateTime modified = File.GetLastWriteTimeUtc(path);
            if (modified == catalogReadAt) return;

            var fresh = new Dictionary<string, GameUnit>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] p = line.Split('\t');

                if (p[0] == "unit" && p.Length >= 19)
                {
                    var added = new GameUnit
                    {
                        Name = p[1],
                        Hash = Short(p[2]),
                        Tier = Int(p[3]),
                        MaxHp = Real(p[4]),
                        VelMin = Real(p[6]),
                        VelMax = Real(p[7]),
                        Turn = Real(p[8]),
                        Radius = Real(p[9]),
                        Capsule = p[10] == "1",
                        SpawnDuration = Real(p[11]),
                        Wall = p[13] == "1",
                        Player = p[14] == "1",
                        Boss = p[15] == "1",
                        IslandMin = Int(p[16], -1),
                        Art = p.Length > 19 ? p[19] : ""
                    };
                    fresh[added.Name] = added;
                }
                else if (p[0] == "gun" && p.Length >= 19)
                {
                    if (!fresh.TryGetValue(p[1], out GameUnit? owner)) continue;
                    owner.Guns.Add(new GameGun
                    {
                        Index = Int(p[2]),
                        Name = p[3],
                        Mode = Int(p[4]),
                        Angle = Real(p[5]),
                        Arc = Real(p[6]),
                        FireRate = Real(p[8]),
                        Burst = Int(p[9], 1),
                        Reload = Real(p[10]),
                        Damage = Real(p[11]),
                        Speed = Real(p[12]),
                        Duration = Real(p[13]),
                        Range = Real(p[14]),
                        Multi = Int(p[15], 1)
                    });
                }
            }

            catalog = fresh;
            catalogReadAt = modified;
            resolvedTemplates.Clear();
            reportedFailures.Clear();
            warnedMissingCatalog = false;

            int armedCount = fresh.Values.Count(u => u.Armed && !u.Wall && !u.Player);
            log("ENEMY", path + ": " + fresh.Count + " unit(s), " + armedCount + " armed");
            ShowAutomaticPicks(log);
        }
        catch (IOException)
        {
            // The plugin may be writing the file right now: tries again on the next check.
        }
        catch (Exception e)
        {
            catalogReadAt = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            log("ENEMY", "could not read " + path + ": " + e.GetType().Name + ": " + e.Message);
        }
    }

    private static void ShowAutomaticPicks(Action<string, string> log)
    {
        if (catalog.Count == 0) return;
        var parts = new List<string>();
        foreach (string type in new[] { "weak", "medium", "strong", "turret", "boss" })
        {
            GameUnit? u = PickAutomatic(type);
            parts.Add("auto:" + type + "=" + (u == null ? "(none)" : u.Name + " (health " + Num(u.MaxHp) + ")"));
        }
        log("ENEMY", "automatic picks: " + string.Join(", ", parts));
    }

    // Reads (or rereads, if it changed) enemies.json. With an error, the previous configuration stays.
    private static void LoadConfig(Action<string, string> log)
    {
        string path = ConfigPath();
        try
        {
            if (!File.Exists(path))
            {
                if (!configLoaded)
                {
                    configLoaded = true;
                    config = Normalize(new EnemiesConfig());
                    log("ENEMY", path + " does not exist: no enemy types from the file (only the default types of the code)");
                    Validate(log);
                }
                return;
            }

            DateTime modified = File.GetLastWriteTimeUtc(path);
            if (modified == configReadAt) return;
            configReadAt = modified;

            EnemiesConfig? loaded = JsonSerializer.Deserialize<EnemiesConfig>(File.ReadAllText(path), JsonOptions);
            if (loaded == null) throw new JsonException("empty file");
            config = Normalize(loaded);
            configLoaded = true;
            resolvedTemplates.Clear();
            reportedFailures.Clear();
            log("ENEMY", path + ": " + config.Types.Count + " type(s), " + config.Encounters.Count + " encounter(s) (" +
                string.Join(", ", config.Encounters.Select(e => e.Name + (e.Active ? "" : " off"))) + ")");
            Validate(log);
        }
        catch (IOException)
        {
            configReadAt = DateTime.MinValue; // file in use (editor saving): try again
        }
        catch (Exception e)
        {
            if (!configLoaded)
            {
                configLoaded = true;
                config = Normalize(new EnemiesConfig());
            }
            log("ENEMY", "error in " + path + " (the previous configuration applies): " + e.Message);
        }
    }

    // Removes nulls, makes type names case-insensitive and puts the phases in order.
    private static EnemiesConfig Normalize(EnemiesConfig c)
    {
        c.Arena ??= new ArenaConfig();
        c.Encounters ??= new List<EncounterConfig>();
        var types = new Dictionary<string, EnemyType>(StringComparer.OrdinalIgnoreCase);
        if (c.Types != null)
        {
            foreach (var pair in c.Types)
            {
                if (pair.Value == null) continue;
                EnemyType t = pair.Value;
                if (string.IsNullOrWhiteSpace(t.Name)) t.Name = pair.Key;
                t.Template ??= "auto:weak";
                t.Behavior = NormalizeBehavior(t.Behavior);
                t.Guns ??= new List<int>();
                types[pair.Key] = t;
            }
        }
        c.Types = types;

        c.Encounters = c.Encounters.Where(x => x != null).ToList();
        foreach (EncounterConfig e in c.Encounters)
        {
            e.Name ??= "";
            e.Server ??= "";
            e.Waves = (e.Waves ?? new List<WaveConfig>()).Where(o => o != null).ToList();
            foreach (WaveConfig o in e.Waves)
                o.Groups = (o.Groups ?? new List<GroupConfig>()).Where(g => g != null && !string.IsNullOrWhiteSpace(g.Type)).ToList();
        }
        return c;
    }

    private static void Validate(Action<string, string> log)
    {
        ValidateAiReferences(log);
        foreach (EncounterConfig e in config.Encounters)
        {
            for (int i = 0; i < e.Waves.Count; i++)
            {
                foreach (GroupConfig g in e.Waves[i].Groups)
                {
                    if (!config.Types.ContainsKey(g.Type) && !defaultTypes.ContainsKey(g.Type))
                        log("ENEMY", "enemies.json: encounter '" + e.Name + "' wave " + (i + 1) + " uses the type '" + g.Type + "', which does not exist");
                }
            }
        }
    }

}
