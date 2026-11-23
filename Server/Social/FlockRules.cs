// The numbers of the flock rules: the game's own ("guilds" file of the configBinary, as the original server used them) unless the
// host changed them in social.json.
public static class FlockRules
{
    private static ConfigNode Config => ConfigBinary.Get("guilds");
    private static SocialSettings Host => SocialSettingsFile.Current;

    public static bool Enabled => Host.FlocksEnabled;
    public static bool RequireFounderToken => Host.RequireFounderToken;

    public static int MaxMembers
    {
        get
        {
            int fromHost = Host.MaxMembers;
            return fromHost > 0 ? fromHost : Math.Max(1, Config["maxMembersPerFlock"].AsInt(60));
        }
    }

    public static int MaxFlocksPerPlayer
    {
        get
        {
            int fromHost = Host.MaxFlocksPerPlayer;
            return fromHost > 0 ? fromHost : Math.Max(1, Config["maxFlocksPerPlayer"].AsInt(3));
        }
    }

    // Seconds between two joins once the player joined RejoinAfterJoins flocks (0 = no limit).
    public static int RejoinSeconds => Host.RejoinSeconds >= 0 ? Host.RejoinSeconds : Math.Max(0, Config["joinTimeout"].AsInt(86400));

    public static int RejoinAfterJoins => Host.RejoinAfterJoins >= 0 ? Host.RejoinAfterJoins : Math.Max(0, Config["minJoinCountBeforeTimeoutCheck"].AsInt(5));

    public static int AutoBanMutes => Host.AutoBanMutes >= 0 ? Host.AutoBanMutes : Math.Max(0, Config["autoBanOnNMutes"].AsInt(5));

    public static int InactiveSeconds => Host.InactiveSeconds > 0 ? Host.InactiveSeconds : Math.Max(60, Config["inactivityTimeout"].AsInt(604800));

    // A member who played in the last day counts for the health of the flock.
    public static int ActiveSeconds => Math.Max(60, Config["activePlayerTimeout"].AsInt(86400));

    // The items that pay for creating a flock (the player needs one in the plane's cargo).
    public static List<string> FounderTokens
    {
        get
        {
            List<string> tokens = Config["itemIdsRequiredToCreate"].AsStringList();
            return tokens.Count > 0 ? tokens : new List<string> { "create_flock_token", "test_create_flock" };
        }
    }

    // GuildsHelpers.CalculateHealth: the first few active members count more than the next ones.
    public static float Health(Flock flock, long now)
    {
        ConfigNode config = Config;
        int minActive = Math.Max(0, config["healthMinActivePlayer"].AsInt(5));
        float factorMin = config["healthFactorMinActive"].AsFloat(0.1f);
        float factorMore = config["healthFactorMoreActive"].AsFloat(0.05f);
        int active = flock.CountActive(now, ActiveSeconds);
        return Math.Min(active, minActive) * factorMin + Math.Max(0, active - minActive) * factorMore;
    }

    // The client's ValidatorName for flock names: 2 to 12 characters and none of < > / \ " ' ` &.
    public static bool ValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string trimmed = name.Trim();
        if (trimmed.Length < 2 || trimmed.Length > 12) return false;
        return trimmed.IndexOfAny(new[] { '<', '>', '/', '\\', '"', '\'', '`', '&' }) < 0;
    }
}
