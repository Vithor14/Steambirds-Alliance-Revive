using SteambirdsAi;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.Editor;

// The weapons (gun mounts) of a unit as a checklist: a check says "this mount shoots". Clicking the name of a mount shows
// its data (pattern, arc, fire rate, bullet, reach...). The list comes from the unit, so it changes with the unit.
// An empty stored list means "every mount of the unit": then all the boxes are checked.
internal sealed class GunMountList : FlowLayoutPanel
{
    private sealed class Entry
    {
        public int Index;
        public AiGunStats? Gun;
        public CheckBox Box = null!;
        public Label Name = null!;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private readonly Label details;
    private readonly Label note;
    private bool loading;
    private int selected = -1;

    public GunMountList()
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(3, 2, 3, 2);
        details = Ui.Wrapping("", 430, true);
        note = Ui.Wrapping("", 430, true);
    }

    // The indexes that shoot when the user changes a check. An empty list = all the mounts of the unit.
    public event Action<List<int>>? Changed;

    // Shows the guns of the unit and which of them the file lets shoot.
    public void Show(IReadOnlyList<AiGunStats> guns, IReadOnlyList<int> stored)
    {
        loading = true;
        try
        {
            SuspendLayout();
            Controls.Clear();
            entries.Clear();
            var indexes = guns.Select(g => g.Index).ToList();
            foreach (int extra in stored.Where(i => !indexes.Contains(i))) indexes.Add(extra);

            foreach (int index in indexes.Distinct().OrderBy(i => i))
            {
                AiGunStats? gun = guns.FirstOrDefault(g => g.Index == index);
                var entry = new Entry { Index = index, Gun = gun };
                entry.Box = new ThemedCheckBox
                {
                    AutoSize = true, Margin = new Padding(3, 3, 0, 0),
                    Checked = stored.Count == 0 || stored.Contains(index)
                };
                entry.Box.CheckedChanged += (_, _) => OnChecked(entry);
                entry.Name = new Label { AutoSize = true, Margin = new Padding(0, 4, 3, 0), Cursor = Cursors.Hand, Text = Caption(entry) };
                entry.Name.Click += (_, _) => Select(entry.Index);
                Controls.Add(Ui.Flow(entry.Box, entry.Name));
                entries.Add(entry);
            }

            if (entries.Count == 0)
            {
                note.Text = L.T("This unit has no weapons, so there is nothing to choose.");
                Controls.Add(note);
                details.Text = "";
                selected = -1;
            }
            else
            {
                note.Text = stored.Count == 0
                    ? L.T("Every mount shoots. Uncheck the ones that should stay quiet in this state; click a name to see its data.")
                    : L.T("Click a name to see the data of the mount. Check all of them to go back to \"every mount shoots\".");
                Controls.Add(note);
                Controls.Add(details);
                Select(entries.Any(e => e.Index == selected) ? selected : entries[0].Index);
            }
        }
        finally
        {
            ResumeLayout(true);
            loading = false;
        }
    }

    private static string Caption(Entry entry)
    {
        AiGunStats? g = entry.Gun;
        if (g == null) return "#" + entry.Index + "  " + L.T("(this unit has no such mount)");
        string name = g.Name.Length > 0 ? g.Name : "gun" + g.Index;
        string text = "#" + g.Index + "  " + name + "  -  " + L.T(AiGunAim.ModeName(g.Mode));
        return g.Damage > 0f ? text + ", " + L.F("damage {0}", Num(g.Damage)) : text + " (" + L.T("no damage") + ")";
    }

    private void OnChecked(Entry changed)
    {
        if (loading) return;
        if (!entries.Any(e => e.Box.Checked))
        {
            // A state with no weapon at all is "Shoots" off: the list cannot be empty.
            loading = true;
            changed.Box.Checked = true;
            loading = false;
            note.Text = L.T("At least one mount has to shoot. To stop shooting in this state, turn off \"Shoots\".");
            Select(changed.Index);
            return;
        }
        Select(changed.Index);
        List<int> on = entries.Where(e => e.Box.Checked).Select(e => e.Index).ToList();
        Changed?.Invoke(on.Count == entries.Count ? new List<int>() : on);
    }

    private void Select(int index)
    {
        selected = index;
        foreach (Entry entry in entries) entry.Name.Font = entry.Index == index ? Ui.BoldFont : Font;
        Entry? chosen = entries.FirstOrDefault(e => e.Index == index);
        details.Text = chosen == null ? "" : Describe(chosen);
    }

    private static string Num(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    // What the data of one mount means, in plain words (numbers of units_config.txt, written by the plugin).
    private static string Describe(Entry entry)
    {
        AiGunStats? g = entry.Gun;
        if (g == null) return L.T("The file names a mount that this unit does not have: it is ignored here.");

        var text = new System.Text.StringBuilder();
        text.AppendLine(L.F("Mount #{0} \"{1}\"", g.Index, g.Name));
        text.AppendLine(L.F("Pattern: {0} - {1}.", L.T(AiGunAim.ModeName(g.Mode)), L.T(AiGunAim.ModeHelp(g.Mode))));
        text.AppendLine(g.Arc >= 359f
            ? L.F("Points {0} degrees from the front and can turn all the way around (firing arc 360).", Num(g.Angle))
            : g.Arc > 0.5f
                ? L.F("Points {0} degrees from the front, firing arc {1} degrees.", Num(g.Angle), Num(g.Arc))
                : L.F("Fixed {0} degrees from the front (no firing arc).", Num(g.Angle)));
        if (g.SlewRate > 0f) text.AppendLine(L.F("Turn speed of the mount: {0}.", Num(g.SlewRate)));
        text.AppendLine(L.F("Position on the unit's art: ({0}, {1}).", Num(g.OffsetX), Num(g.OffsetY)));

        string burst = g.Burst >= 1000 ? L.T("no limit") : g.Burst.ToString();
        text.AppendLine(L.F("Fires {0} shots per second, {1} shots per burst, then {2} s of reload.", Num(g.FireRate), burst, Num(g.Reload)));
        text.AppendLine(L.F("Each shot: {0} bullet(s), damage {1} each, speed {2}, lasts {3} s.", g.Multi, Num(g.Damage), Num(g.Speed), Num(g.Duration)));
        text.AppendLine(L.F("Reach: about {0}.", Num(AiGunAim.Range(g.Speed, g.Duration, g.Range))));
        if (Math.Abs(g.BulletTurn) > 0.01f) text.AppendLine(L.F("The bullets steer by themselves (turn {0}).", Num(g.BulletTurn)));

        float perSecond = Math.Max(0.05f, g.FireRate) * Math.Max(1, g.Multi);
        if (g.Burst > 1 && g.Burst < 1000 && g.Reload > 0f)
        {
            float charge = g.Burst / Math.Max(0.05f, g.FireRate);
            perSecond *= charge / (charge + g.Reload);
        }
        text.AppendLine(L.F("About {0} bullets per second while it shoots, {1} damage per second if all hit.", Num(perSecond), Num(perSecond * g.Damage)));
        text.Append(g.GunType.Length > 0
            ? L.F("Shot type (gun type of the game): {0}.", g.GunType)
            : L.T("Shot type: not in your units_config.txt yet (rebuild the plugin and export the game files again)."));
        return text.ToString();
    }
}
