using System.Text.Json.Nodes;

namespace SteambirdsLauncher.Core;

// What the Rich Presence is built from (read by DiscordPresence each turn).
internal sealed class PresenceInput
{
    // The player's game is open (the plane, the level and the place come from the server the game joined).
    public bool Playing { get; init; }
    public FoundServer? PlayedServer { get; init; }
    public string PlayerName { get; init; } = "";

    // The server of this PC is running (the launcher's own).
    public bool Hosting { get; init; }
    public FoundServer? HostedServer { get; init; }
    public string HostedName { get; init; } = "";

    // When this kind of activity began (the timer Discord shows).
    public DateTime Since { get; init; } = DateTime.UtcNow;
}

// Builds the activity of Discord's Rich Presence. Only game facts go out: plane, levels, place, how many players are online,
// and the name the host gave the server. Never an IP address, a port, the Steam name/ID or the name of the player.
internal static class PresenceBuilder
{
    // Art asset (Developer Portal > Rich Presence > Art Assets) shown as the big picture. Optional: without it Discord shows none.
    public const string LargeImage = "logo";

    private const int MaxText = 120;

    // Names of the plane classes (the ids of the game's classes).
    private static readonly Dictionary<string, string> PlaneNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["assault"] = "Assault", ["medic"] = "Medic", ["rapier"] = "Rapier", ["tank"] = "Tank", ["walker"] = "Walker",
        ["olfaithful"] = "Ol' Faithful", ["duster"] = "Duster", ["support"] = "Support", ["paladin"] = "Paladin",
        ["wizard"] = "Wizard", ["engineer"] = "Engineer", ["pirate"] = "Pirate", ["siegetank"] = "Siege Tank",
        ["daggerknight"] = "Dagger Knight", ["vampire"] = "Vampire"
    };

    public static string PlaneName(string id)
    {
        if (id.Length == 0) return "";
        if (PlaneNames.TryGetValue(id, out string? name)) return name;
        return char.ToUpperInvariant(id[0]) + id.Substring(1);
    }

    // null = nothing to show (the activity is removed from the profile).
    public static JsonObject? Build(PresenceInput input)
    {
        string details;
        string state;
        int online = 0;
        int max = 0;
        string largeText = "Steambirds Alliance Revive";

        if (input.Playing)
        {
            FoundServer? server = input.PlayedServer;
            PlayerDetail? me = FindMe(server, input.PlayerName);
            if (me != null && me.Plane.Length > 0)
            {
                details = L.F("{0}, plane level {1}", PlaneName(me.Plane), Math.Max(1, me.PlaneLevel));
                var parts = new List<string>();
                string place = PlaceText(me);
                if (place.Length > 0) parts.Add(place);
                if (me.PilotLevel > 0) parts.Add(L.F("Pilot rank {0}", me.PilotLevel));
                state = parts.Count > 0 ? string.Join(" · ", parts) : L.T("In the game");
            }
            else
            {
                details = L.T("Playing Steambirds Alliance Revive");
                state = L.T("In the game");
            }
            if (server != null)
            {
                online = server.Players;
                max = server.MaxPlayers;
            }
            if (input.Hosting && input.HostedName.Length > 0) largeText = L.F("Hosting {0}", input.HostedName);
        }
        else if (input.Hosting)
        {
            string name = input.HostedServer?.Name.Length > 0 ? input.HostedServer.Name : input.HostedName;
            details = L.F("Hosting {0}", name);
            FoundServer? hosted = input.HostedServer;
            if (hosted == null)
            {
                state = L.T("Starting up");
            }
            else
            {
                online = hosted.Players;
                max = hosted.MaxPlayers;
                if (online == 0)
                {
                    state = L.T("Waiting for pilots");
                }
                else
                {
                    state = L.F("{0} of {1} pilots online", online, Math.Max(online, max));
                    int inDungeons = hosted.PlayerDetails.Count(p => p.Place == "dungeon");
                    if (inDungeons > 0) state += " · " + L.F("{0} in dungeons", inDungeons);
                }
            }
        }
        else
        {
            return null;
        }

        var activity = new JsonObject
        {
            ["type"] = 0,
            ["details"] = Clip(details),
            ["state"] = Clip(state),
            ["timestamps"] = new JsonObject { ["start"] = new DateTimeOffset(DateTime.SpecifyKind(input.Since, DateTimeKind.Utc)).ToUnixTimeSeconds() },
            ["assets"] = new JsonObject { ["large_image"] = LargeImage, ["large_text"] = Clip(largeText) },
            ["buttons"] = new JsonArray(new JsonObject { ["label"] = Clip(L.T("Join the Discord"), 32), ["url"] = LauncherInfo.DiscordUrl })
        };
        // "3 of 8" next to the state. Only with someone online (Discord does not take 0 of N).
        if (online > 0 && max >= online)
            activity["party"] = new JsonObject { ["id"] = "steambirds-revive", ["size"] = new JsonArray(online, max) };
        return activity;
    }

    // The entry of this player in the server's list: by the name the game uses; when the name is not known (the account's name),
    // the only player of the server.
    private static PlayerDetail? FindMe(FoundServer? server, string playerName)
    {
        if (server == null || server.PlayerDetails.Count == 0) return null;
        string name = playerName.Trim();
        if (name.Length > 0)
        {
            PlayerDetail? byName = server.PlayerDetails.FirstOrDefault(p => string.Equals(p.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;
        }
        return server.PlayerDetails.Count == 1 ? server.PlayerDetails[0] : null;
    }

    private static string PlaceText(PlayerDetail player)
    {
        if (player.Where.Length == 0) return "";
        return player.Place switch
        {
            "nexus" => L.F("In {0}", player.Where),
            "tutorial" => L.F("Training: {0}", player.Where),
            "world" => L.F("Exploring {0}", player.Where),
            "dungeon" => L.F("In dungeon: {0}", player.Where),
            _ => ""
        };
    }

    // Discord takes 2 to 128 bytes per line; labels of buttons up to 32 characters.
    private static string Clip(string text, int max = MaxText)
    {
        text = text.Trim();
        if (text.Length > max) text = text.Substring(0, max - 1).TrimEnd() + "…";
        return text.Length < 2 ? text + "  " : text;
    }
}

// Discord Rich Presence of the launcher: who is playing shows the plane, the levels and the place; who hosts shows the server
// (name and how many are online). A turn every 15 seconds (and at once when Refresh is called); the activity is only sent when it
// changed (Discord takes about 5 updates per 20 seconds). Discord not running is not an error: the next turn tries again.
internal sealed class DiscordPresence : IDisposable
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(15);

    private readonly Func<bool> enabled;
    private readonly Func<string> applicationId;
    private readonly Func<CancellationToken, Task<PresenceInput?>> gather;
    private readonly DiscordIpc ipc = new DiscordIpc();
    private readonly SemaphoreSlim wake = new SemaphoreSlim(0);
    private readonly CancellationTokenSource stop = new CancellationTokenSource();
    private Task? loop;
    private string? lastSent;
    private string status = "";

    // gather: reads the state of the launcher (runs on a background thread; it may do HTTP calls).
    public DiscordPresence(Func<bool> enabled, Func<string> applicationId, Func<CancellationToken, Task<PresenceInput?>> gather)
    {
        this.enabled = enabled;
        this.applicationId = applicationId;
        this.gather = gather;
    }

    // Raised on a background thread.
    public event Action? StatusChanged;

    public string Status => status;

    public void Start()
    {
        loop ??= Task.Run(RunAsync);
    }

    // The settings changed (or the game opened/closed): do a turn now.
    public void Refresh()
    {
        if (wake.CurrentCount == 0) wake.Release();
    }

    private async Task RunAsync()
    {
        CancellationToken cancel = stop.Token;
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                await TickAsync(cancel);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                SetStatus(e.Message);
            }
            try
            {
                await wake.WaitAsync(Period, cancel);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Closing the launcher: takes the activity off the profile (Discord also drops it when the pipe closes).
        try
        {
            using var limit = new CancellationTokenSource(1500);
            if (lastSent != null && ipc.Connected) await ipc.SetActivityAsync(null, limit.Token);
        }
        catch
        {
        }
        ipc.Dispose();
    }

    private async Task TickAsync(CancellationToken cancel)
    {
        string id = applicationId().Trim();
        PresenceInput? input = enabled() && id.Length > 0 ? await gather(cancel) : null;
        JsonObject? activity = input == null ? null : PresenceBuilder.Build(input);

        if (!enabled())
        {
            SetStatus(L.T("Off."));
        }
        else if (id.Length == 0)
        {
            SetStatus(L.T("Needs the Discord application ID (LauncherInfo.DiscordApplicationId or \"discordApplicationId\" in launcher.json)."));
        }

        string key = activity?.ToJsonString() ?? "";
        if (activity == null)
        {
            // Nothing to show: if something is up, remove it; otherwise do not even open the pipe.
            if (lastSent != null && ipc.Connected && await ipc.SetActivityAsync(null, cancel)) lastSent = null;
            else if (lastSent != null && !ipc.Connected) lastSent = null;
            if (enabled() && id.Length > 0) SetStatus(L.T("Ready: it shows up when you play or host a server."));
            return;
        }

        if (key == lastSent && ipc.Connected) return;
        if (!await ipc.ConnectAsync(id, cancel))
        {
            lastSent = null;
            SetStatus(ipc.Error);
            return;
        }
        if (await ipc.SetActivityAsync(activity, cancel))
        {
            lastSent = key;
            SetStatus(L.F("On Discord: {0}", activity["details"]?.GetValue<string>() ?? ""));
        }
        else
        {
            lastSent = null;
            SetStatus(ipc.Error);
        }
    }

    private void SetStatus(string text)
    {
        if (text == status) return;
        status = text;
        StatusChanged?.Invoke();
    }

    public void Dispose()
    {
        stop.Cancel();
        try
        {
            loop?.Wait(2500);
        }
        catch
        {
        }
        stop.Dispose();
    }
}
