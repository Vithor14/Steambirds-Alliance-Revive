using System.Text.Json;
using System.Text.Json.Serialization;

// Host settings for progression: progression.json in the server folder (Server).
// Created with the default values the first time and read again when the file changes.
// These are the knobs the launcher "Server" tab will edit later.
public sealed class ProgressionSettings
{
    // Multiplies the XP of each kill (1 = original rebuilt formula).
    public float PlaneXpMultiplier { get; set; } = 1f;

    // Multiplies the pilot XP planes give (in real time and the death bonus). 1 = original.
    public float PilotXpMultiplier { get; set; } = 1f;

    // Original rule: one kill gives at most half of the current level, and at max level at most xpLimitForMaxLevel.
    public bool CapXpPerKill { get; set; } = true;

    // true = original: a destroyed plane leaves the account (the pilot keeps the XP it already gave in real time).
    // false = the plane comes back to the hangar and keeps counting for the pilot.
    public bool Permadeath { get; set; } = true;

    // Pilot XP is earned in real time: realTimePlaneXPProportion of the value of every plane (0.9 in this build,
    // 65% in older ones). The rest only came from retiring the plane, which this client does not offer.
    // This is the share of that rest the pilot gets when the plane is destroyed: 0 = original, 1 = all of it.
    public float DeathPilotXpBonus { get; set; }

    // Keys in progression.json that this version does not know (for example the old "deathPilotXpShare").
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownKeys { get; set; }

    // Skill loadouts per pilot. 0 = the game's base value (baseSkillLoadoutSlots). The original sold more
    // in the store_skill_loadouts store.
    public int SkillLoadoutSlots { get; set; } = 3;

    // Planes per account (hangar). The original had 2 and sold more.
    public int HangarSlots { get; set; } = 3;

    // Rank of a new pilot.
    public int StartingPilotLevel { get; set; } = 1;

    // Extra unlock items for every account (for example "playerclass_medic").
    public List<string> StartingUnlocks { get; set; } = new List<string>();

    // Every class unlocked from the start.
    public bool UnlockAllClasses { get; set; }

    // Classes unlocked by plane level, following the game's "unlocks" rules.
    public bool UnlockClassesByLevel { get; set; } = true;

    // "You did X% of the damage" text after bosses (ClientPostFightDetails).
    public bool PostFightDetails { get; set; } = true;

    // OnKill pilot skills fire when the player kills an enemy (TriggerPlayerSkill).
    public bool KillSkillTrigger { get; set; } = true;

    // Sorts a random portrait for pilots without one (needs portraits_config.txt from plugin 1.20.0+).
    public bool RandomPortrait { get; set; } = true;

    // Pilot skills are checked on the server (points, group requirements, max level).
    public bool ValidateSkills { get; set; } = true;

    // Career rewards shown in the Pilot School timeline and delivered at the rank.
    public List<CareerRewardSetting> CareerRewards { get; set; } = new List<CareerRewardSetting>();
}

public sealed class CareerRewardSetting
{
    // Text id of the reward (TextMachine.GetRewardTitle). The game's own ids are in career_rewards (see the log).
    public string Id { get; set; } = "";
    public int PilotLevel { get; set; }
    public List<CareerRewardItem> Rewards { get; set; } = new List<CareerRewardItem>();
}

public sealed class CareerRewardItem
{
    // QuestRewardType: Item, PlaneXp, PilotXp, Unlock, Quest, Currency, AccountItem, Ingredient.
    // The server delivers PilotXp, PlaneXp and Unlock; the others are only shown until inventory exists.
    public string Type { get; set; } = "PilotXp";
    public string Id { get; set; } = "";
    public int Amount { get; set; }
}

public static class ProgressionSettingsFile
{
    public const string FileName = "progression.json";

    private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static ProgressionSettings current = new ProgressionSettings();
    private static DateTime readAt = DateTime.MinValue;
    private static DateTime nextCheck = DateTime.MinValue;
    private static Action<string, string> log = (_, _) => { };
    private static readonly object gate = new object();

    public static string FilePath => Path.Combine(Directory.GetCurrentDirectory(), FileName);

    // Current settings. The file is checked again at most once per second.
    public static ProgressionSettings Current
    {
        get
        {
            lock (gate)
            {
                if (DateTime.UtcNow >= nextCheck)
                {
                    nextCheck = DateTime.UtcNow.AddSeconds(1);
                    ReloadIfChanged();
                }
                return current;
            }
        }
    }

    public static void Start(Action<string, string> log)
    {
        lock (gate)
        {
            ProgressionSettingsFile.log = log;
            if (!File.Exists(FilePath))
            {
                try
                {
                    File.WriteAllText(FilePath, DefaultFileText);
                    log("PROGRESSION", FilePath + " created with the default values (edit and save: the server reloads it by itself)");
                }
                catch (Exception e)
                {
                    log("PROGRESSION", "could not create " + FilePath + ": " + e.Message);
                }
            }
            ReloadIfChanged();
        }
    }

    private static void ReloadIfChanged()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            DateTime modified = File.GetLastWriteTimeUtc(FilePath);
            if (modified == readAt) return;
            readAt = modified;

            ProgressionSettings? read = JsonSerializer.Deserialize<ProgressionSettings>(File.ReadAllText(FilePath), ReadOptions);
            if (read == null) return;
            read.StartingUnlocks ??= new List<string>();
            read.CareerRewards ??= new List<CareerRewardSetting>();
            read.CareerRewards.RemoveAll(r => r == null || string.IsNullOrWhiteSpace(r.Id));
            foreach (CareerRewardSetting reward in read.CareerRewards) reward.Rewards ??= new List<CareerRewardItem>();
            current = read;
            log("PROGRESSION", FilePath + " read: XP x" + read.PlaneXpMultiplier + ", pilot XP x" + read.PilotXpMultiplier +
                ", permadeath " + (read.Permadeath ? "sim" : "no") + ", pilot bonus on fall " + read.DeathPilotXpBonus + ", " +
                read.SkillLoadoutSlots + " loadout(s), " + read.HangarSlots + " hangar slot(s), " + read.CareerRewards.Count +
                " career reward(s)");
            if (read.UnknownKeys != null && read.UnknownKeys.Count > 0)
            {
                log("PROGRESSION", "key(s) this version does not use in " + FileName + ": " + string.Join(", ", read.UnknownKeys.Keys) +
                    (read.UnknownKeys.Keys.Any(k => k.Equals("deathPilotXpShare", StringComparison.OrdinalIgnoreCase))
                        ? " (deathPilotXpShare became deathPilotXpBonus: pilot XP now comes in real time)" : ""));
            }
        }
        catch (Exception e)
        {
            // A broken file keeps the last good settings.
            log("PROGRESSION", "error in " + FilePath + " (" + e.Message + "): keeping the previous configuration");
        }
    }

    // Default file, with comments (the reader skips them).
    private const string DefaultFileText =
@"// Server progression (pilot, planes and skills). Save the file and the server reloads it by itself.
{
  // XP of each kill (1 = reconstructed formula: base health x the tier's xpTiers x the unit's xpMultiplier).
  ""planeXpMultiplier"": 1.0,

  // Multiplies the pilot XP that planes give (1 = original).
  ""pilotXpMultiplier"": 1.0,

  // Original rule: a kill gives at most half of the current level; at the maximum level, at most xpLimitForMaxLevel.
  ""capXpPerKill"": true,

  // true = original: a destroyed plane leaves the account (the pilot keeps the XP the plane already gave in real time).
  // false = the plane goes back to the hangar and keeps counting for the pilot.
  ""permadeath"": true,

  // Pilot XP comes in real time: a share of the value of each hangar plane (90% in this game version,
  // 65% in the old ones). The rest only came by retiring the plane, and this client has no such button.
  // How much of that rest the pilot gets when the plane falls: 0 = original, 1 = all of it.
  ""deathPilotXpBonus"": 0.0,

  // Skill loadouts per pilot (0 = the game's base value; the original sold more in the store).
  ""skillLoadoutSlots"": 3,

  // Planes per account (the original had 2 and sold more).
  ""hangarSlots"": 3,

  // Rank of a new pilot.
  ""startingPilotLevel"": 1,

  // Extra unlocks for all accounts, for example ""playerclass_medic"".
  ""startingUnlocks"": [],

  // All classes unlocked from the start.
  ""unlockAllClasses"": false,

  // Classes unlocked by plane level, using the game's ""unlocks"" rules.
  ""unlockClassesByLevel"": true,

  // ""you did X% of the damage"" text after the bosses.
  ""postFightDetails"": true,

  // ""On kill"" (OnKill) pilot skills fire when the player kills an enemy.
  ""killSkillTrigger"": true,

  // Picks a portrait for pilots without one (needs portraits_config.txt from plugin 1.20.0+).
  ""randomPortrait"": true,

  // Checks on the server the points, requirements and maximum level of the pilot skills.
  ""validateSkills"": true,

  // Career rewards (Pilot School timeline). Example:
  // { ""id"": ""career_reward_rank5"", ""pilotLevel"": 5, ""rewards"": [ { ""type"": ""PilotXp"", ""amount"": 10 } ] }
  // Types delivered today: PilotXp, PlaneXp and Unlock (id = unlock item).
  ""careerRewards"": []
}
";
}
