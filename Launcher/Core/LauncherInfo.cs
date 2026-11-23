namespace SteambirdsLauncher.Core;

// Fixed facts about the launcher, the game build and the server.
public static class LauncherInfo
{
    public const string Title = "Steambirds Alliance Revive — Launcher";

    // Game client the server was written for (depot 386011, manifest 58813776496064724).
    public const string ExpectedGameBuild = "201219.1413";
    public const string DepotCommand = "download_depot 386010 386011 58813776496064724";

    // The ports of the server are in server\network.json (ServerNetworkFile), edited in Server > Network and multiplayer.

    // Invite to the community Discord of the project.
    public const string DiscordUrl = "https://discord.gg/sNwTk7T4mu";

    // Application ID of the project's app in the Discord Developer Portal (Rich Presence). It is public, not a secret.
    // Create the app at https://discord.com/developers/applications, copy its "Application ID" here and, in the app's
    // Rich Presence > Art Assets, upload Branding\icon.png with the key "logo". Empty = no Rich Presence (launcher.json
    // "discordApplicationId" can set it without building again).
    public const string DiscordApplicationId = "1574506755315204216";

    public static string Version => typeof(LauncherInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
