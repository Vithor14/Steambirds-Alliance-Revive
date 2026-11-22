// Pilot data kept in the account file (account.json, "Pilot" object). Same fields the client keeps in
// Pilot / PlayerData (PilotSerializer, PlayerDataSerializer), plus what the server needs to remember.
//
// The plane XP and level live in each plane of the account (Accounts.SavedPlane: Xp, Level, BankedPilotXp).
public sealed class PilotSave
{
    // Pilot rank. Goes up as soon as the pilot XP shown by the client (computedXP) reaches the next target.
    public int Level { get; set; } = 1;

    // Pilot.baselineXP. The client adds realTimePlaneXPProportion x the value of every hangar plane on top (computedXP);
    // the server moves this baseline when the rank changes or a plane is lost, so that total never jumps.
    public int BaselineXp { get; set; }

    // ServerRecordLevelupUIShown: what the client already showed. With LastLevelupShown != Level the
    // sergeant in the Nexus says there is a pending rank up.
    public int LastXpLevelupShown { get; set; }
    public int LastLevelupShown { get; set; } = 1;

    // Skill loadouts: skill id -> skill level. The active one is SkillLoadouts[SkillLoadoutIndex].
    public List<Dictionary<string, int>> SkillLoadouts { get; set; } = new List<Dictionary<string, int>> { new Dictionary<string, int>() };
    public int SkillLoadoutIndex { get; set; }
    public List<string> SkillLoadoutNames { get; set; } = new List<string> { "" };

    // Portrait: slot name (Base, Beak, Deco, Clothes, Eye, Hat, Mask, Pattern) -> part id.
    public Dictionary<string, int> Portrait { get; set; } = new Dictionary<string, int>();

    // Account buffs (BuffList).
    public List<BuffSave> Buffs { get; set; } = new List<BuffSave>();

    // Highest level reached by each plane class (PlayerData.highestLevels): class unlocks and skill groups use it.
    public Dictionary<string, int> HighestLevels { get; set; } = new Dictionary<string, int>();

    // Unlock items of the account (PlayerData.unlocks), like "playerclass_walker".
    public List<string> Unlocks { get; set; } = new List<string>();

    // Career rewards already delivered (ids).
    public List<string> CareerRewardsDelivered { get; set; } = new List<string>();

    // Statistics kept by the server (not sent to the client yet).
    public int Kills { get; set; }
    public int BossKills { get; set; }
    public int PlanesLost { get; set; }
    public int HighestPlaneLevel { get; set; }

    // Fills what an old or hand-edited account.json may have left null or out of range.
    public void Normalize()
    {
        if (Level < 1) Level = 1;
        if (BaselineXp < 0) BaselineXp = 0;
        SkillLoadouts ??= new List<Dictionary<string, int>>();
        SkillLoadouts.RemoveAll(l => l == null);
        if (SkillLoadouts.Count == 0) SkillLoadouts.Add(new Dictionary<string, int>());
        SkillLoadoutNames ??= new List<string>();
        while (SkillLoadoutNames.Count < SkillLoadouts.Count) SkillLoadoutNames.Add("");
        for (int i = 0; i < SkillLoadoutNames.Count; i++) SkillLoadoutNames[i] ??= "";
        if (SkillLoadoutIndex < 0 || SkillLoadoutIndex >= SkillLoadouts.Count) SkillLoadoutIndex = 0;
        Portrait ??= new Dictionary<string, int>();
        Buffs ??= new List<BuffSave>();
        Buffs.RemoveAll(b => b == null || string.IsNullOrEmpty(b.Id));
        HighestLevels ??= new Dictionary<string, int>();
        Unlocks ??= new List<string>();
        Unlocks.RemoveAll(string.IsNullOrEmpty);
        CareerRewardsDelivered ??= new List<string>();
    }

    // Loadout list with at least "count" entries (new ones empty and without a name).
    public void EnsureLoadouts(int count)
    {
        while (SkillLoadouts.Count < count) SkillLoadouts.Add(new Dictionary<string, int>());
        while (SkillLoadoutNames.Count < SkillLoadouts.Count) SkillLoadoutNames.Add("");
    }

    public Dictionary<string, int> ActiveSkills()
    {
        EnsureLoadouts(SkillLoadoutIndex + 1);
        return SkillLoadouts[SkillLoadoutIndex];
    }

    // Deep copy: the account lock is released before the copy is used.
    public PilotSave Clone()
    {
        return new PilotSave
        {
            Level = Level,
            BaselineXp = BaselineXp,
            LastXpLevelupShown = LastXpLevelupShown,
            LastLevelupShown = LastLevelupShown,
            SkillLoadouts = SkillLoadouts.Select(l => new Dictionary<string, int>(l)).ToList(),
            SkillLoadoutIndex = SkillLoadoutIndex,
            SkillLoadoutNames = new List<string>(SkillLoadoutNames),
            Portrait = new Dictionary<string, int>(Portrait),
            Buffs = Buffs.Select(b => b.Clone()).ToList(),
            HighestLevels = new Dictionary<string, int>(HighestLevels),
            Unlocks = new List<string>(Unlocks),
            CareerRewardsDelivered = new List<string>(CareerRewardsDelivered),
            Kills = Kills,
            BossKills = BossKills,
            PlanesLost = PlanesLost,
            HighestPlaneLevel = HighestPlaneLevel
        };
    }
}

// One account buff (BuffSerializer: id, startTime ticks, startProcId, startCharacterId).
public sealed class BuffSave
{
    public string Id { get; set; } = "";
    public long StartTicks { get; set; }
    public string StartProcId { get; set; } = "";
    public string StartCharacterId { get; set; } = "";

    public BuffSave Clone()
    {
        return new BuffSave { Id = Id, StartTicks = StartTicks, StartProcId = StartProcId, StartCharacterId = StartCharacterId };
    }
}
