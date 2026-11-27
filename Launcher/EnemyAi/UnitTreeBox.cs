using SteambirdsAi;
using SteambirdsLauncher.Core;
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
