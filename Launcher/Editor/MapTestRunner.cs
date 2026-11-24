using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.Editor;

// "Play test" of the map editor: starts a server that has nothing but this map (SteambirdsServer.exe --test-map, Server\Dungeons\
// MapTest.cs) and opens the game logging in to it. The server sends the game straight to the map after the login, like the
// tutorial: no account to make, no Nexus, no other server. It listens on this PC only and on ports of its own, so the normal
// server of the Server tab can keep running.
internal static class MapTestRunner
{
    // The map file must be saved already (the server reads the file). True when the game was opened (or is already in the test).
    public static async Task<bool> RunAsync(LauncherContext context, IWin32Window owner, string mapPath)
    {
        string folder = context.GameFolder;
        if (!GameInstall.HasGame(folder))
        {
            Ui.ShowInfo(owner, L.F("Steambirds.exe was not found in:\n{0}\n\nSet the game folder in the Client tab first.", folder));
            return false;
        }

        ServerProcess test = context.MapTestServer;
        bool sameMap = test.IsActive && string.Equals(context.MapTestPath, mapPath, StringComparison.OrdinalIgnoreCase);
        if (context.Game.IsRunning(folder))
        {
            if (context.PlayingTarget?.IsMapTest == true && sameMap)
                Ui.ShowInfo(owner, L.T("The game is already in the test of this map. The map was saved: type /restart in the game's chat to load the new version."));
            else
                Ui.ShowInfo(owner, L.T("The game is already open (it can only be open once). Close it and click Play test again."));
            return context.PlayingTarget?.IsMapTest == true && sameMap;
        }

        if (!GameProcess.IsSteamRunning())
        {
            if (Ui.Confirm(owner, L.T("Steam is not open, and the game logs in with the Steam account.\n\nOpen Steam now? After logging in, click Play test again.")))
                GameProcess.OpenSteam();
            return false;
        }

        // One server per map: another map (or an old copy of this one that is still up) is stopped first.
        if (test.IsActive && !sameMap) await Task.Run(() => test.StopAndWait(4000));
        if (!test.IsActive)
        {
            context.ServerNotice(L.F("map test: starting the test server for {0}", mapPath));
            if (!test.Start(context.GameFolder, out string error, "--test-map \"" + mapPath + "\"", "maptest"))
            {
                Ui.ShowError(owner, error);
                return false;
            }
            context.MapTestPath = mapPath;
        }

        // The server prints "[HTTP] listening on ..." when the login is ready (a few seconds). If it closes by itself, the map has an error.
        for (int i = 0; i < 120 && test.State != ServerState.Running; i++)
        {
            if (!test.IsActive) break;
            await Task.Delay(250);
        }
        if (test.State != ServerState.Running)
        {
            Ui.ShowError(owner, L.F("The test server did not start. The last lines of the Server tab console (marked [map test]) show why. Log: {0}",
                test.SessionLogPath ?? ""));
            return false;
        }

        var target = new GameTarget
        {
            IsThisPc = true,
            IsMapTest = true,
            Host = "localhost",
            HttpPort = MapTestPorts.Http,
            NexusPort = MapTestPorts.Nexus,
            PlayerName = context.EffectivePlayerName().Length > 0 ? context.EffectivePlayerName() : null,
            ServerName = L.T("Map test")
        };
        try
        {
            int processId = context.Game.Launch(folder, context.Settings.GameLocale, target);
            context.PlayingTarget = target;
            context.Presence.Refresh();
            context.ClientNotice(L.F("game open for the map test (process {0}): OZY_URL={1} Steambirds.exe {2}", processId, target.OzyUrl,
                GameProcess.Arguments(context.Settings.GameLocale, target)));
            return true;
        }
        catch (Exception e)
        {
            Ui.ShowError(owner, L.F("Could not open the game: {0}", e.Message));
            return false;
        }
    }

    // Stops the test server (the game, if open, loses its connection).
    public static void Stop(LauncherContext context)
    {
        if (context.MapTestServer.IsActive) context.MapTestServer.Stop();
    }
}
