using System.Globalization;

namespace SteambirdsLauncher.Core;

// Pilot portraits. A portrait is a set of pieces, one per slot (Base, Beak, Deco, Clothes, Eye, Hat, Mask, Pattern), each
// piece being a part id of the game's "PortraitParts" art pool. The pieces are Unity prefabs, so the only list of them is
// portraits_config.txt, written by QuieterPlugin (copied to server\data by SteambirdsGameFiles.exe):
//   part <id> <slot> <slotName> <species> <excludedSlot: 0 = none, otherwise slot + 1> <prefab>   (TAB separated)
// The server picks a portrait for a new pilot with the same rules as RandomPortrait here (Server PilotExtras.cs, copied from
// the game's Portrait.GenerateRandomPortrait): a base, then the other slots of the same species in random order; hat 70%,
// mask 30%, decoration 40%, the rest always when the species has a part for the slot.
public sealed class PortraitParts
{
    public static readonly string[] SlotNames = { "Base", "Beak", "Deco", "Clothes", "Eye", "Hat", "Mask", "Pattern" };

    public sealed class Part
    {
        public int Id { get; init; }
        public int Slot { get; init; }
        public string Species { get; init; } = "";
        public int ExcludedSlot { get; init; } = -1;
        public string Prefab { get; init; } = "";
    }

    public static string FilePath => LauncherPaths.ServerFile(Path.Combine("data", "portraits_config.txt"));

    public List<Part> Parts { get; } = new List<Part>();

    public bool Available => Parts.Any(p => p.Slot == 0);

    public static PortraitParts Load()
    {
        var result = new PortraitParts();
        try
        {
            if (!File.Exists(FilePath)) return result;
            foreach (string line in File.ReadAllLines(FilePath))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] p = line.Split('\t');
                if (p[0] != "part" || p.Length < 6) continue;
                if (!int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id <= 0) continue;
                if (!int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)) continue;
                int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int exclusion);
                result.Parts.Add(new Part
                {
                    Id = id,
                    Slot = slot,
                    Species = p[4],
                    ExcludedSlot = exclusion > 0 ? exclusion - 1 : -1,
                    Prefab = p.Length > 6 ? p[6] : ""
                });
            }
        }
        catch
        {
            // An unreadable file means no list of pieces: the portrait is only shown, not rolled.
        }
        return result;
    }

    // Name of a piece as the game's prefab is called (portrait_player_M_01 -> "M_01"), or just its id.
    public string Describe(string slot, int id)
    {
        Part? part = Parts.FirstOrDefault(p => p.Id == id);
        string name = part == null ? "" : part.Prefab.Replace("portrait_player_", "", StringComparison.OrdinalIgnoreCase);
        return name.Length > 0 ? slot + " " + name + " (" + id.ToString(CultureInfo.InvariantCulture) + ")" : slot + " " + id.ToString(CultureInfo.InvariantCulture);
    }

    public string Describe(IReadOnlyDictionary<string, int> portrait)
    {
        if (portrait.Count == 0) return "—";
        return string.Join(", ", SlotNames.Where(portrait.ContainsKey).Select(s => Describe(s, portrait[s])));
    }

    // A new random portrait, or null when the list of pieces is missing (no base to start from).
    public Dictionary<string, int>? Roll(Random random)
    {
        List<Part> bases = Parts.Where(p => p.Slot == 0).ToList();
        if (bases.Count == 0) return null;

        Part chosenBase = bases[random.Next(bases.Count)];
        var chosen = new Dictionary<int, Part> { [0] = chosenBase };

        List<int> slots = Enumerable.Range(1, SlotNames.Length - 1).OrderBy(_ => random.Next()).ToList();
        foreach (int slot in slots)
        {
            if (slot == 5 && random.NextDouble() > 0.7) continue;   // Hat
            if (slot == 6 && random.NextDouble() > 0.3) continue;   // Mask
            if (slot == 2 && random.NextDouble() > 0.4) continue;   // Deco
            if (chosen.Values.Any(p => p.ExcludedSlot == slot)) continue;

            List<Part> options = Parts.Where(p => p.Slot == slot && p.Species == chosenBase.Species).ToList();
            if (options.Count == 0) continue;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Part option = options[random.Next(options.Count)];
                if (option.ExcludedSlot < 0 || !chosen.ContainsKey(option.ExcludedSlot))
                {
                    chosen[slot] = option;
                    break;
                }
            }
        }

        var result = new Dictionary<string, int>();
        foreach (KeyValuePair<int, Part> pair in chosen.OrderBy(p => p.Key)) result[SlotNames[pair.Key]] = pair.Value.Id;
        return result;
    }
}
