using System.Text.RegularExpressions;

// Which enemies live in which region of the open world.
//
// The game's own units are filed by region in the configBinary: "Units/01 - Beach", "Units/11 - Lowlands (City)", "12 (Ruins)",
// "13 (Rural)", "21..23 Midlands", "31..33 Highlands", "41 Mountains", "51 HighMountains". The first digit is the band (the
// region's elevation order) and the second one is the variant, which is the digit in the tile name (lowlands2 -> 12). Inside a
// folder the files are named by role: GRP<n>_<unit> / GRP<n>_<unit>leader (a group: a leader and followers), SLO<n>_<unit>
// (a solo enemy) and BOS<n>_<unit> (setpiece bosses, which the world does not place yet).
//
// Reconstruction: how many of each, where and when was decided by the original server (named_spawn_rules, empty in every
// client build); here a spawn site picks a family of its region at random (WorldServer.Spawns.cs).
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

    // Parts of a unit name that mark a helper the original game spawned from another unit (controllers, grenades, clones...).
    private static readonly string[] HelperMarks = { "controller", "grenade", "exploder", "clone", "neck", "ghost1", "bug", "death", "spawner" };

    private static Dictionary<string, List<Family>> pools = new Dictionary<string, List<Family>>(StringComparer.Ordinal);

    public static int FamilyCount => pools.Values.Sum(p => p.Count);

    // Reads the unit files of the configBinary. "skip" = template names the host does not want.
    public static void Build(IEnumerable<string> skip, Action<string, string> log)
    {
        var skipped = new HashSet<string>(skip, StringComparer.OrdinalIgnoreCase);
        var built = new Dictionary<string, List<Family>>(StringComparer.Ordinal);
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
                f = new Family { Name = solo ? template : "group " + role.Groups[2].Value + " of " + parts[1], Solo = solo };
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
        pools = built;
        log("WORLD", "enemy pools: " + units + " unit(s) in " + FamilyCount + " group(s)/solo(s) (" + rejected + " helper/unavailable unit(s) left out); " +
            string.Join(", ", built.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ":" + p.Value.Count)));
    }

    // The pool of a region: the folder of its band and variant, or any variant of the band.
    public static List<Family> For(WorldData.Region region)
    {
        string band = region.Base switch
        {
            "beach" => "0",
            "lowlands" => "1",
            "midlands" => "2",
            "highlands" => "3",
            "mountains" => "4",
            "highmountains" => "5",
            _ => ""
        };
        if (band.Length == 0) return new List<Family>();
        if (pools.TryGetValue(band + region.Variant, out List<Family>? exact) && exact.Count > 0) return exact;
        List<Family> any = pools.Where(p => p.Key.StartsWith(band, StringComparison.Ordinal)).SelectMany(p => p.Value).ToList();
        return any;
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
