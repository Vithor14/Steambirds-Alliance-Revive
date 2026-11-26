using System.Text.RegularExpressions;

// Which enemies live in which region of the open world.
//
// The game's own units are filed by region in the configBinary: "Units/01 - Beach", "Units/11 - Lowlands (City)", "12 (Ruins)",
// "13 (Rural)", "21..23 Midlands", "31..33 Highlands", "41 Mountains", "51 HighMountains". The first digit is the band (the
// region's elevation order) and the second one is the variant, which is the digit in the tile name (lowlands2 -> 12). Inside a
// folder the files are named by role: GRP<n>_<unit> / GRP<n>_<unit>leader (a group: a leader and followers), SLO<n>_<unit>
// (a solo enemy) and BOS<n>_<unit> (bosses: the units with the same folder and number are one boss with its parts and minions, for
// example Gargantua with its fists and laser; the strongest unit that is not a part is the boss and the default boss of a region is the
// first of its folder, see BossChoicesFor; the open world creates a boss only when the host asks, WorldServer.Requests.cs).
//
// Reconstruction: how many of each, where and when was decided by the original server (named_spawn_rules, empty in every
// client build); here a spawn site picks a family of its region at random (WorldServer.Sites.cs).
//
// These are the defaults. The host changes them per region in world-enemies.json (WorldEnemyLists.cs, edited by the launcher):
// For() answers with the host's list when the region has one, and the game's otherwise.
public static class WorldEnemyPools
{
    public sealed class Family
    {
        public string Name = "";
        public bool Solo;
        public readonly List<string> Leaders = new List<string>();
        public readonly List<string> Members = new List<string>();
    }

    private static readonly Regex FolderCode = new Regex(@"^(\d)(\d)(?= |-)");
    private static readonly Regex FileRole = new Regex(@"^(GRP|SLO)(\d+)_(.+)$");
    private static readonly Regex BossRole = new Regex(@"^BOS(\d*)_(.+)$");

    // Parts of a unit name that mark a helper the original game spawned from another unit (controllers, grenades, clones...).
    private static readonly string[] HelperMarks = { "controller", "grenade", "exploder", "clone", "neck", "ghost1", "bug", "death", "spawner" };

    // Parts of a boss unit that are not the boss itself: its death model, hands, hour marks, shell, mines and missiles...
    private static readonly string[] BossPartMarks =
    {
        "death", "mine", "missile", "claw", "hand", "number", "shell", "fist", "laser", "signal", "tail", "exploder", "spawner",
        "controller", "grenade", "clone", "chest", "saucer", "minion", "pet", "node"
    };

    // Units of a boss group that are never created with it: its death model, what it fires or drops (mines, missiles, chests, grenades),
    // clones and the controllers of the original scripts. The rest of the group (hands, fists, claws, numbers, minions...) is created.
    private static readonly string[] BossNeverMarks =
    {
        "death", "mine", "missile", "exploder", "spawner", "controller", "grenade", "clone", "chest", "master", "manager"
    };

    private const int MaxBossParts = 30;

    // A boss of the open world: the unit that is the boss and the units that come with it (parts and minions of its BOS group).
    public sealed class BossGroup
    {
        public string Name = "";
        public readonly List<string> Parts = new List<string>();
    }

    // The boss of a folder when the strongest unit is not the one that is the boss of the zone (the largest unit of Noble Estates is
    // the train, the Imperial Palace has planets and suns). Checked against the choices of the folder; the rest keep the strongest first.
    private static readonly Dictionary<string, string> PreferredBoss = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["33"] = "pharaoh",
        ["41"] = "hypnotist",
        ["51"] = "notra"
    };

    private static Dictionary<string, List<Family>> pools = new Dictionary<string, List<Family>>(StringComparer.Ordinal);
    private static Dictionary<string, List<BossGroup>> bosses = new Dictionary<string, List<BossGroup>>(StringComparer.Ordinal);
    private static HashSet<string> skippedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static Action<string, string> logger = (_, _) => { };

    public static int FamilyCount => pools.Values.Sum(p => p.Count);

    internal static void Log(string text) => logger("WORLD", text);

    // How many letters two names share at the start.
    private static int SharedStart(string a, string b)
    {
        int n = 0;
        while (n < a.Length && n < b.Length && a[n] == b[n]) n++;
        return n;
    }

    // Reads the unit files of the configBinary. "skip" = template names the host does not want.
    public static void Build(IEnumerable<string> skip, Action<string, string> log)
    {
        logger = log;
        var skipped = new HashSet<string>(skip, StringComparer.OrdinalIgnoreCase);
        skippedNames = skipped;
        var built = new Dictionary<string, List<Family>>(StringComparer.Ordinal);
        var bossUnits = new Dictionary<string, List<(string Name, float Health, bool IsBoss)>>(StringComparer.Ordinal);   // "folder/number" -> units
        var byGroup = new Dictionary<string, Family>(StringComparer.Ordinal);
        int units = 0, rejected = 0;

        foreach (KeyValuePair<string, ConfigNode> file in ConfigBinary.WithPrefix("Units/").OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            string[] parts = file.Key.Split('/');
            if (parts.Length != 3) continue;
            Match folder = FolderCode.Match(parts[1]);
            if (!folder.Success) continue;
            string code = folder.Groups[1].Value + folder.Groups[2].Value;
            if (!IsWorldFolder(code)) continue;

            Match bossFile = BossRole.Match(parts[2]);
            if (bossFile.Success)
            {
                string bossTemplate = file.Value["templateName"].AsString(bossFile.Groups[2].Value);
                if (bossTemplate.Length == 0) continue;
                Enemies.UnitSummary? bossInfo = Enemies.FindUnit(bossTemplate);
                if (bossInfo == null || bossInfo.Wall || bossInfo.Player || bossInfo.Invisible || bossInfo.MaxHealth <= 0f) continue;
                string bossKey = code + "/" + bossFile.Groups[1].Value;
                if (!bossUnits.TryGetValue(bossKey, out List<(string Name, float Health, bool IsBoss)>? unitList))
                {
                    unitList = new List<(string, float, bool)>();
                    bossUnits[bossKey] = unitList;
                }
                if (!unitList.Any(u => u.Name.Equals(bossInfo.Name, StringComparison.OrdinalIgnoreCase))) unitList.Add((bossInfo.Name, bossInfo.MaxHealth, bossInfo.Boss));
                continue;
            }

            Match role = FileRole.Match(parts[2]);
            if (!role.Success) continue;

            string template = file.Value["templateName"].AsString(role.Groups[3].Value);
            if (template.Length == 0 || skipped.Contains(template)) continue;
            string lower = template.ToLowerInvariant();
            if (HelperMarks.Any(m => lower.Contains(m)) || lower.EndsWith("pre", StringComparison.Ordinal))
            {
                rejected++;
                continue;
            }
            Enemies.UnitSummary? info = Enemies.FindUnit(template);
            if (info == null || info.Wall || info.Player || info.Invisible || info.Boss || info.MaxHealth <= 0f)
            {
                rejected++;
                continue;
            }

            bool solo = role.Groups[1].Value == "SLO";
            string family = solo ? code + "/" + template : code + "/GRP" + role.Groups[2].Value;
            if (!byGroup.TryGetValue(family, out Family? f))
            {
                f = new Family { Name = solo ? template : "group " + role.Groups[2].Value, Solo = solo };
                byGroup[family] = f;
                if (!built.TryGetValue(code, out List<Family>? list))
                {
                    list = new List<Family>();
                    built[code] = list;
                }
                list.Add(f);
            }
            if (!solo && lower.Contains("leader")) f.Leaders.Add(template);
            else f.Members.Add(template);
            units++;
        }

        // A group with only leaders spawns them as ordinary members.
        foreach (List<Family> list in built.Values)
        {
            foreach (Family f in list)
            {
                if (f.Members.Count == 0 && f.Leaders.Count > 0)
                {
                    f.Members.AddRange(f.Leaders);
                    f.Leaders.Clear();
                }
            }
        }

        // The bosses of each folder: one per BOS number. The boss is the strongest unit that is not a part; the others (but the death
        // model, what it fires and the controllers) come with it. A group with no boss unit (only signals, for example) is left out. The
        // preferred boss of a folder (if any) goes first, then the strongest.
        var bossesBuilt = new Dictionary<string, List<BossGroup>>(StringComparer.Ordinal);
        foreach (IGrouping<string, KeyValuePair<string, List<(string Name, float Health, bool IsBoss)>>> folderGroups in
                 bossUnits.GroupBy(p => p.Key.Substring(0, p.Key.IndexOf('/')), StringComparer.Ordinal))
        {
            PreferredBoss.TryGetValue(folderGroups.Key, out string? preferred);
            var groups = new List<(BossGroup Group, float Health)>();
            foreach (KeyValuePair<string, List<(string Name, float Health, bool IsBoss)>> pair in folderGroups)
            {
                List<(string Name, float Health, bool IsBoss)> all = pair.Value;
                var mains = all.Where(u => !BossPartMarks.Any(m => u.Name.ToLowerInvariant().Contains(m)))
                    .OrderByDescending(u => u.Health).ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (mains.Count == 0) continue;
                // One BOS number can hold two bosses of the game (the Imperial Palace keeps Solaris and the Ambassador together): every
                // main unit the game marks as a boss is a boss of its own, and each part goes to the boss whose name it shares the
                // longest start with ("ufosaucer" -> "ufo", "solarisplanet" -> "solarissun"); the strongest boss gets the rest.
                var heads = mains.Where(u => u.IsBoss).ToList();
                if (heads.Count < 2) heads = new List<(string Name, float Health, bool IsBoss)> { mains[0] };
                var made = heads.Select(h => (Group: new BossGroup { Name = h.Name }, h.Health)).ToList();
                foreach ((string partName, float _, bool _) in all.OrderByDescending(u => u.Health).ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (made.Any(g => partName.Equals(g.Group.Name, StringComparison.OrdinalIgnoreCase))) continue;
                    string lowerPart = partName.ToLowerInvariant();
                    if (BossNeverMarks.Any(m => lowerPart.Contains(m)) || skipped.Contains(partName)) continue;
                    BossGroup owner = made.Select(g => (g.Group, Shared: SharedStart(lowerPart, g.Group.Name.ToLowerInvariant())))
                        .Where(g => g.Shared >= 3).OrderByDescending(g => g.Shared).Select(g => g.Group).FirstOrDefault() ?? made[0].Group;
                    if (owner.Parts.Count < MaxBossParts) owner.Parts.Add(partName);
                }
                groups.AddRange(made);
            }
            bossesBuilt[folderGroups.Key] = groups
                .OrderByDescending(g => preferred != null && g.Group.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(g => g.Health).ThenBy(g => g.Group.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Group).ToList();
        }
        foreach (string code in bossesBuilt.Where(p => p.Value.Count == 0).Select(p => p.Key).ToList()) bossesBuilt.Remove(code);

        pools = built;
        bosses = bossesBuilt;
        log("WORLD", "enemy pools: " + units + " unit(s) in " + FamilyCount + " group(s)/solo(s) (" + rejected + " helper/unavailable unit(s) left out); " +
            string.Join(", ", built.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ":" + p.Value.Count)) +
            "; bosses: " + string.Join(", ", bossesBuilt.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ":" + string.Join("/", p.Value.Select(b => b.Name + (b.Parts.Count > 0 ? "+" + b.Parts.Count : ""))))));
        WorldEnemyLists.WriteDefaults(WorldData.Regions.Where(r => r.SortOrder >= 0), log);
    }

    // The pool of a region: what the host put in world-enemies.json, or the game's own list.
    public static List<Family> For(WorldData.Region region)
    {
        WorldEnemyLists.RegionEntry? entry = WorldEnemyLists.Of(region.Name);
        return entry?.Groups != null ? FromHost(entry.Groups) : DefaultsFor(region);
    }

    // The game's own pool of a region: the folder of its band and variant, or any variant of the band.
    public static List<Family> DefaultsFor(WorldData.Region region)
    {
        string band = BandOf(region);
        if (band.Length == 0) return new List<Family>();
        if (pools.TryGetValue(band + region.Variant, out List<Family>? exact) && exact.Count > 0) return exact;
        List<Family> any = pools.Where(p => p.Key.StartsWith(band, StringComparison.Ordinal)).SelectMany(p => p.Value).ToList();
        return any;
    }

    // The boss "spawn boss here" creates in a region: the host's choice (world-enemies.json), or the default one. Null when the
    // region has none.
    public static string? BossFor(WorldData.Region region)
    {
        string? chosen = WorldEnemyLists.Of(region.Name)?.Boss;
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            Enemies.UnitSummary? unit = Enemies.FindUnit(chosen);
            if (unit != null && !unit.Wall && !unit.Player) return unit.Name;
        }
        return DefaultBossFor(region);
    }

    public static string? DefaultBossFor(WorldData.Region region)
    {
        List<BossGroup> choices = BossChoicesFor(region);
        return choices.Count > 0 ? choices[0].Name : null;
    }

    // The bosses of the region's folder (the preferred and the strongest first); the band's bosses when its variant has none.
    public static List<BossGroup> BossChoicesFor(WorldData.Region region)
    {
        string band = BandOf(region);
        if (band.Length == 0) return new List<BossGroup>();
        if (bosses.TryGetValue(band + region.Variant, out List<BossGroup>? exact) && exact.Count > 0) return exact.ToList();
        return bosses.Where(p => p.Key.StartsWith(band, StringComparison.Ordinal)).SelectMany(p => p.Value).ToList();
    }

    // The boss group of a unit name: the one of the region's choices, else any in the game; a unit that is not a boss of the game
    // (the host typed any unit) comes alone.
    public static BossGroup BossGroupOf(WorldData.Region region, string unitName)
    {
        BossGroup? found = BossChoicesFor(region).FirstOrDefault(b => b.Name.Equals(unitName, StringComparison.OrdinalIgnoreCase)) ??
                           bosses.Values.SelectMany(l => l).FirstOrDefault(b => b.Name.Equals(unitName, StringComparison.OrdinalIgnoreCase));
        return found ?? new BossGroup { Name = unitName };
    }

    private static string BandOf(WorldData.Region region)
    {
        return region.Base switch
        {
            "beach" => "0",
            "lowlands" => "1",
            "midlands" => "2",
            "highlands" => "3",
            "mountains" => "4",
            "highmountains" => "5",
            _ => ""
        };
    }

    // The groups of world-enemies.json as pools. Units that do not exist (or that the host skips in world.json) are left out.
    private static List<Family> FromHost(List<WorldEnemyLists.GroupEntry> groups)
    {
        var result = new List<Family>();
        foreach (WorldEnemyLists.GroupEntry group in groups)
        {
            List<string> leaders = group.Leaders.Where(IsUsable).ToList();
            List<string> members = group.Members.Where(IsUsable).ToList();
            if (group.Solo)
            {
                members.AddRange(leaders);
                leaders.Clear();
            }
            else if (members.Count == 0 && leaders.Count > 0)
            {
                members.AddRange(leaders);
                leaders.Clear();
            }
            if (members.Count == 0 && leaders.Count == 0) continue;
            var family = new Family { Name = group.Name.Length > 0 ? group.Name : "group", Solo = group.Solo };
            family.Leaders.AddRange(leaders);
            family.Members.AddRange(members);
            result.Add(family);
        }
        return result;
    }

    private static bool IsUsable(string template)
    {
        if (skippedNames.Contains(template)) return false;
        Enemies.UnitSummary? info = Enemies.FindUnit(template);
        return info != null && !info.Wall && !info.Player && info.MaxHealth > 0f;
    }

    private static bool IsWorldFolder(string code)
    {
        return code switch
        {
            "01" or "11" or "12" or "13" or "21" or "22" or "23" or "31" or "32" or "33" or "41" or "51" => true,
            _ => false
        };
    }
}
