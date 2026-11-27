using SteambirdsAi;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// What a unit picker writes. GameUnit is the template name (trainA01). Summon is a type id when the row is a type,
// otherwise the game unit, which is what a summon line and enemies.json accept.
internal enum UnitPickKind
{
    GameUnit,
    Summon
}

// The unit lists the editors share: the same folders as the Enemy AI unit picker, plus roles and summon places.
internal static class UnitLists
{
    public static List<AiUnitGroup> Groups() => AiUnitOrigins.Build(LauncherPaths.ServerFolder, false, null);

    public static IEnumerable<string> BrainIds()
    {
        string root = Path.Combine(LauncherPaths.AiFolder, "brains");
        if (!Directory.Exists(root)) return Array.Empty<string>();
        try
        {
            return Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    public static IEnumerable<string> Roles()
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FormationFile file in Files())
        {
            foreach (FormationMember member in file.Members)
                if (!string.IsNullOrWhiteSpace(member.Role)) roles.Add(member.Role.Trim());
        }
        return roles.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // behind/front/... and one entry per point of every formation (point:<formation>/<id>).
    public static IEnumerable<string> Places(FormationFile? extra = null)
    {
        var list = new List<string> { "behind", "front", "left", "right", "ring", "around" };
        var seen = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        void AddFile(FormationFile? file)
        {
            if (file == null) return;
            foreach (FormationPoint point in file.Points)
            {
                if (string.IsNullOrWhiteSpace(point.Id)) continue;
                string id = "point:" + (file.Id.Length > 0 ? file.Id + "/" : "") + point.Id.Trim();
                if (seen.Add(id)) list.Add(id);
            }
        }
        foreach (FormationFile file in Files()) AddFile(file);
        AddFile(extra);
        return list;
    }

    // The AIs that fit this unit or type id: made for it first, then the generic ones.
    private static AiLibrary? brains;
    private static string brainsSignature = "";

    private static AiLibrary Brains()
    {
        string signature = AiLibrary.Signature(LauncherPaths.AiFolder);
        if (brains == null || signature != brainsSignature)
        {
            brains = AiLibrary.Load(LauncherPaths.AiFolder);
            brainsSignature = signature;
        }
        return brains;
    }

    public static List<(string Id, bool Specific)> Applicable(string unitOrType)
    {
        try
        {
            AiLibrary library = Brains();
            List<string> names = NamesOf(unitOrType);
            return names.Count == 0 ? library.ApplicableTo(Array.Empty<string>()) : library.ApplicableTo(names);
        }
        catch (IOException)
        {
            return new List<(string Id, bool Specific)>();
        }
    }

    public static string AiText(string unitOrType, string id)
    {
        if (id.Length == 0) return "";
        (string Id, bool Specific) found = Applicable(unitOrType).FirstOrDefault(row => row.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (found.Id == null || found.Id.Length == 0) return L.F("{0}  (not made for this unit)", id);
        return found.Specific ? L.F("{0}  (made for this unit)", id) : id;
    }

    public static void FillAi(ComboBox box, string unitOrType, string selected)
    {
        box.BeginUpdate();
        box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.Items.Clear();
        if (selected.Length > 0 && !Applicable(unitOrType).Any(row => row.Id.Equals(selected, StringComparison.OrdinalIgnoreCase)))
            box.Items.Add(new AiRow(selected, L.F("{0}  (not made for this unit)", selected)));
        foreach ((string id, bool specific) in Applicable(unitOrType))
            box.Items.Add(new AiRow(id, specific ? L.F("{0}  (made for this unit)", id) : id));
        box.EndUpdate();
        for (int i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is AiRow row && row.Id.Equals(selected, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedIndex = i;
                return;
            }
        }
        box.SelectedIndex = -1;
    }

    public static string AiId(ComboBox box) => box.SelectedItem is AiRow row ? row.Id : "";

    private static List<string> NamesOf(string unitOrType)
    {
        var names = new List<string>();
        string token = (unitOrType ?? "").Trim();
        if (token.Length == 0) return names;
        void Add(string name)
        {
            name = name.Trim();
            if (name.Length == 0 || name.StartsWith("auto:", StringComparison.OrdinalIgnoreCase)) return;
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
        }
        Add(token);
        try
        {
            string enemies = Path.Combine(LauncherPaths.ServerFolder, "enemies.json");
            if (File.Exists(enemies) && System.Text.Json.Nodes.JsonNode.Parse(TextFiles.ReadShared(enemies)) is System.Text.Json.Nodes.JsonObject root &&
                root["types"] is System.Text.Json.Nodes.JsonObject types && types[token] is System.Text.Json.Nodes.JsonObject type)
            {
                foreach (string option in Json.Str(type, "template").Split('|')) Add(option);
            }
        }
        catch (Exception e) when (e is System.Text.Json.JsonException || e is IOException)
        {
            // The token itself stays the only name.
        }
        return names;
    }

    public static string Picked(AiUnitChoice choice, UnitPickKind kind)
    {
        if (kind == UnitPickKind.Summon && choice.IsType && choice.TypeId.Length > 0) return choice.TypeId;
        if (choice.UnitName.Length > 0) return choice.UnitName;
        return choice.TypeId;
    }

    private static IEnumerable<FormationFile> Files()
    {
        try
        {
            return FormationLibrary.Load(LauncherPaths.AiFolder).ById.Values.OrderBy(file => file.Id, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (IOException)
        {
            return Array.Empty<FormationFile>();
        }
    }
}

// A text box plus the categorized unit list (dungeons, biomes, generic types, units with no origin).
internal sealed class UnitTreeBox : UserControl
{
    private readonly ThemedTextBox text = new ThemedTextBox { Dock = DockStyle.Fill };
    private readonly Button button;
    private readonly UnitPickKind kind;
    private bool filling;

    public UnitTreeBox(UnitPickKind kind)
    {
        this.kind = kind;
        Height = 28;
        MinimumSize = new Size(80, 28);
        button = Ui.Button("...", (_, _) => Pick());
        button.Dock = DockStyle.Right;
        button.Width = 32;
        text.TextChanged += (_, _) => { if (!filling) ValueChanged?.Invoke(); };
        Controls.Add(text);
        Controls.Add(button);
    }

    public event Action? ValueChanged;

    public override Size GetPreferredSize(Size proposedSize) => new Size(Math.Max(120, proposedSize.Width), 28);

    public string Value
    {
        get => text.Text.Trim();
        set
        {
            filling = true;
            text.Text = value ?? "";
            filling = false;
        }
    }

    private void Pick()
    {
        string current = Value;
        using var picker = new AiUnitPicker(UnitLists.Groups(), _ => true, current)
        {
            Location = PointToScreen(new Point(0, Height))
        };
        if (picker.ShowDialog(FindForm()) != DialogResult.OK || picker.Chosen == null) return;
        string next = UnitLists.Picked(picker.Chosen, kind);
        if (next.Length == 0 || next == Value) return;
        Value = next;
        ValueChanged?.Invoke();
    }
}

internal static class AiFill
{
    public static void Apply(ComboBox box, string unit, string selected) => UnitLists.FillAi(box, unit, selected);
    public static string Id(ComboBox box) => UnitLists.AiId(box);
}

internal sealed class AiRow
{
    public AiRow(string id, string text)
    {
        Id = id;
        Text = text;
    }

    public string Id { get; }
    public string Text { get; }
    public override string ToString() => Text;
}
