namespace SteambirdsLauncher.Core;

// Fixed facts about the launcher, the game build and the server.
public static class LauncherInfo
{
    public const string Title = "Steambirds Alliance Revive — Launcher";

    // Game client the server was written for (depot 386011, manifest 58813776496064724).
    public const string ExpectedGameBuild = "201219.1413";
    public const string DepotCommand = "download_depot 386010 386011 58813776496064724";

    // The ports of the server are in server\network.json (ServerNetworkFile), edited in Server > Network and multiplayer.

    public static string Version => typeof(LauncherInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
