using System.Globalization;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// "World map" tab of the Server tab: the map of the open world (WorldMapPage) and its settings (world.json: size, how it is generated,
// seed...). "Reroll world" writes a new random seed in world.json; the server, which reads the file by itself, builds the new world at
// once when nobody is in the open world (or when the last player leaves), and the map here changes with it.
internal sealed class WorldPage : UserControl
{
    private readonly LauncherContext context;
    private readonly Label rerollStatus;

    public WorldPage(LauncherContext context)
    {
        this.context = context;
        ToolTip tip = context.ToolTip;

        var rerollButton = Ui.Button(L.T("Reroll world"), (_, _) => Reroll());
        tip.SetToolTip(rerollButton, L.T("Writes a new random seed in world.json. The server builds the new world right away if nobody is in it; with players inside, it starts when the world is empty."));
        rerollStatus = Ui.Note("");
        var top = Ui.WrapFlow(rerollButton, rerollStatus);
        top.Dock = DockStyle.Top;

        var settings = new SettingsPage(tip,
            L.T("Settings of the open world (world.json). The server reads the file by itself: if nobody is in the world, the new map is built at once (see the Map tab); " +
                "with players inside it starts when the world is empty. Enemies, carriers and the spawn point are in the file itself (Settings tab, Files)."),
            new SettingsSection(L.T("Open world"), "world.json",
                SettingsSection.Only("enabled", "name", "maxPlayers", "layout", "sizeScale", "seed", "pregenerate", "biomeScale", "landFraction", "edgeWall", "edgeMargin"))
            {
                SavedNote = L.T("The server builds the new world right away if nobody is in the open world.")
            });

        var tabs = new ThemedTabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(TabOf(L.T("Map"), new WorldMapPage(context)));
        tabs.TabPages.Add(TabOf(L.T("World settings"), settings));

        // Fill first, then the Top control.
        Controls.Add(tabs);
        Controls.Add(top);
    }

    private static TabPage TabOf(string title, Control content)
    {
        var page = new TabPage(title) { Padding = new Padding(6) };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    private void Reroll()
    {
        string path = LauncherPaths.ServerFile("world.json");
        if (!File.Exists(path))
        {
            Ui.ShowInfo(FindForm(), L.T("world.json does not exist yet. Start the server once: it creates the file."));
            return;
        }

        WorldLiveData? live = WorldLiveData.Load();
        bool playing = live != null && live.Active && live.Players.Count > 0 && (live.Age?.TotalSeconds ?? 999) < 8;
        if (playing && !Ui.Confirm(FindForm(),
                L.F("{0} player(s) are in the open world now. The new world starts when it is empty. Roll a new seed anyway?", live!.Players.Count)))
            return;

        try
        {
            int seed = Random.Shared.Next(1, int.MaxValue);
            string text = TextFiles.ReadKeepingBom(path, out bool hasBom);
            string result = JsoncFile.ApplyChanges(text,
                new Dictionary<string, string> { ["seed"] = seed.ToString(CultureInfo.InvariantCulture) }, out List<string> missing);
            if (missing.Count > 0)
            {
                Ui.ShowError(FindForm(), L.T("world.json has no \"seed\" key."));
                return;
            }
            TextFiles.WriteAtomic(path, result, hasBom);
            bool serverUp = context.Server.State == ServerState.Running || NetworkCheck.AnyServerListening();
            Ui.SetStatus(rerollStatus,
                L.F("New seed {0} saved at {1}.", seed, DateTime.Now.ToString("HH:mm:ss")) + " " +
                (!serverUp ? L.T("The server is not running: it builds the world when it starts.")
                    : playing ? L.T("It starts when the world is empty.")
                    : L.T("The server is building it now.")),
                TextRole.Good);
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), L.F("Could not change {0}:\n{1}", path, e.Message));
        }
    }
}
