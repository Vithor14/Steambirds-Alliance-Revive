using System.Globalization;
using Lidgren.Network;

// Pilot portrait, account buffs and career rewards.
//
// Portrait (Portrait, PortraitSerializer, PortraitDatabase): this client build has no screen to pick a portrait
// (UGUIPortraitSelector is not used), so the original server sorted one (GameServer.portraitDatabase,
// Portrait.GenerateRandomPortrait). The parts are Unity prefabs of the "PortraitParts" pool, not configs:
// plugin 1.20.0+ writes them to portraits_config.txt (part id, slot, species, excluded slot).
// ServerReceivePortrait (12150) is still accepted and echoed with ClientUpdatePlayerPortrait (12151).
//
// Buffs (BuffList, BuffInstance): kept in the account, sent in the welcome and with ClientAddPlayerBuff /
// ClientUpdatePlayerBuffs. The client confirms each one with ServerAuditBuffAction(auditId).
// Only ids that exist in the game's "buffs" config are sent: BuffSerializer.Read stops reading on an unknown
// id, which would break the rest of the message.
//
// Career rewards: ServerRequestVagueCareerRewards (12159) asks for the Pilot School timeline; the server sends
// the list of progression.json. This client never asks for the delivery (12161), so the server delivers them
// by itself when the pilot reaches the rank and tells the client with ClientReceiveCareerRewardDelivery (12162).
public static class PilotExtras
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed class PortraitPartInfo
    {
        public int Id;
        public int Slot;
        public string Species = "";
        public int ExcludedSlot = -1;   // PortraitSlotEnum, -1 = none
    }

    private static Action<string, string> log = (_, _) => { };
    private static List<PortraitPartInfo> portraitParts = new List<PortraitPartInfo>();
    private static DateTime portraitFileReadAt = DateTime.MinValue;
    private static bool warnedNoPortraitFile;
    private static int nextAuditId = 1;

    public static void Start(Action<string, string> log)
    {
        PilotExtras.log = log;
        PilotSkills.Start(log);
        LoadPortraitParts();
    }

    // Before each welcome: buffs that ended or end on server change leave, and a pilot without portrait gets one.
    public static void PrepareForWelcome()
    {
        PruneBuffs();
        EnsurePortrait();
    }

    // After each welcome: career rewards already reached and not delivered.
    public static void AfterWelcome(NetServer server, NetConnection connection, double now)
    {
        DeliverCareerRewards(server, connection, "login");
    }

    // ------------------------------------------------------------------ portrait

    private static string PortraitFile => GameDataFiles.FilePath("portraits_config.txt");

    // portraits_config.txt: "part <id> <slot> <slotName> <species> <excludedSlot+1 (0 = none)> <prefab>".
    private static void LoadPortraitParts()
    {
        string path = PortraitFile;
        try
        {
            if (!File.Exists(path))
            {
                if (!warnedNoPortraitFile)
                {
                    warnedNoPortraitFile = true;
                    log("PORTRAIT", path + " does not exist: the pilot gets no portrait. Plugin 1.20.0 writes this file in the " +
                        "BepInEx folder (copy it to Server\\data to play without the plugin).");
                }
                return;
            }
            DateTime modified = File.GetLastWriteTimeUtc(path);
            if (modified == portraitFileReadAt) return;
            portraitFileReadAt = modified;

            var parts = new List<PortraitPartInfo>();
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] p = line.Split('\t');
                if (p[0] != "part" || p.Length < 6) continue;
                if (!int.TryParse(p[1], NumberStyles.Integer, Inv, out int id) || id <= 0) continue;
                if (!int.TryParse(p[2], NumberStyles.Integer, Inv, out int slot)) continue;
                int.TryParse(p[5], NumberStyles.Integer, Inv, out int exclusion);
                parts.Add(new PortraitPartInfo { Id = id, Slot = slot, Species = p[4], ExcludedSlot = exclusion > 0 ? exclusion - 1 : -1 });
            }
            portraitParts = parts;
            log("PORTRAIT", path + ": " + parts.Count + " portrait piece(s), " + parts.Count(x => x.Slot == 0) + " base(s)");
        }
        catch (Exception e)
        {
            log("PORTRAIT", "could not read " + path + ": " + e.Message);
        }
    }

    private static void EnsurePortrait()
    {
        if (!ProgressionSettingsFile.Current.RandomPortrait) return;
        bool hasPortrait = Accounts.Read(d => d.Pilot.Portrait.Count > 0);
        if (hasPortrait) return;
        LoadPortraitParts();
        Dictionary<string, int>? portrait = RandomPortrait(AccountSeed());
        if (portrait == null) return;
        Accounts.Change(d =>
        {
            d.Pilot.Portrait = portrait;
            return 0;
        });
        log("PORTRAIT", "portrait picked: " + DescribePortrait(portrait));
    }

    // Admin command /portrait: sorts a new portrait and sends it.
    public static bool RerollPortrait(NetServer server, NetConnection connection, string role)
    {
        LoadPortraitParts();
        Dictionary<string, int>? portrait = RandomPortrait(Environment.TickCount);
        if (portrait == null) return false;
        Accounts.Change(d =>
        {
            d.Pilot.Portrait = portrait;
            return 0;
        });
        ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteClientUpdatePlayerPortrait(m, portrait));
        log("PORTRAIT", "[" + role + "] new portrait: " + DescribePortrait(portrait));
        return true;
    }

    // ServerReceivePortrait(Portrait): byte count, then (byte slot, ushort part).
    public static void PortraitReceived(NetServer server, NetConnection connection, string role, NetIncomingMessage msg)
    {
        var portrait = new Dictionary<string, int>();
        int count = msg.ReadByte();
        for (int i = 0; i < count; i++)
        {
            int slot = msg.ReadByte();
            int part = msg.ReadUInt16();
            if (slot >= 0 && slot < ProgressionMessages.PortraitSlots.Length && part > 0)
                portrait[ProgressionMessages.PortraitSlots[slot]] = part;
        }
        Accounts.Change(d =>
        {
            d.Pilot.Portrait = portrait;
            return 0;
        });
        ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteClientUpdatePlayerPortrait(m, portrait));
        log("PORTRAIT", "[" + role + "] ServerReceivePortrait: " + DescribePortrait(portrait));
    }

    private static int AccountSeed()
    {
        byte[] id = Accounts.AccountId;
        int seed = 17;
        foreach (byte b in id) seed = unchecked(seed * 31 + b);
        return seed;
    }

    // Portrait.GenerateRandomPortrait: a base, then the other slots of the same species in random order;
    // hat 70%, mask 30%, decoration 40%, the rest always (when the species has a part for the slot).
    private static Dictionary<string, int>? RandomPortrait(int seed)
    {
        List<PortraitPartInfo> parts = portraitParts;
        List<PortraitPartInfo> bases = parts.Where(p => p.Slot == 0).ToList();
        if (bases.Count == 0) return null;

        var random = new Random(seed);
        PortraitPartInfo chosenBase = bases[random.Next(bases.Count)];
        var chosen = new Dictionary<int, PortraitPartInfo> { [0] = chosenBase };

        List<int> slots = Enumerable.Range(1, ProgressionMessages.PortraitSlots.Length - 1).OrderBy(_ => random.Next()).ToList();
        foreach (int slot in slots)
        {
            if (slot == 5 && random.NextDouble() > 0.7) continue;   // Hat
            if (slot == 6 && random.NextDouble() > 0.3) continue;   // Mask
            if (slot == 2 && random.NextDouble() > 0.4) continue;   // Deco
            if (chosen.Values.Any(p => p.ExcludedSlot == slot)) continue;

            List<PortraitPartInfo> options = parts.Where(p => p.Slot == slot && p.Species == chosenBase.Species).ToList();
            if (options.Count == 0) continue;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                PortraitPartInfo option = options[random.Next(options.Count)];
                if (option.ExcludedSlot < 0 || !chosen.ContainsKey(option.ExcludedSlot))
                {
                    chosen[slot] = option;
                    break;
                }
            }
        }

        var result = new Dictionary<string, int>();
        foreach (KeyValuePair<int, PortraitPartInfo> pair in chosen)
            result[ProgressionMessages.PortraitSlots[pair.Key]] = pair.Value.Id;
        return result;
    }

    private static string DescribePortrait(Dictionary<string, int> portrait)
    {
        return portrait.Count == 0 ? "(empty)" : string.Join(", ", portrait.Select(p => p.Key + "=" + p.Value));
    }

    // ------------------------------------------------------------------ buffs

    // Buffs that can go to the client right now (known id, not expired).
    public static List<BuffSave> BuffsToSend(PilotSave pilot)
    {
        DateTime now = DateTime.UtcNow;
        return pilot.Buffs.Where(b => IsSendable(b, now)).Select(b => b.Clone()).ToList();
    }

    private static bool IsSendable(BuffSave buff, DateTime now)
    {
        if (!ProgressionData.Buffs.TryGetValue(buff.Id, out ProgressionData.BuffInfo? info)) return false;
        if (info.ExpireAfter > TimeSpan.Zero && new DateTime(buff.StartTicks, DateTimeKind.Utc) + info.ExpireAfter <= now) return false;
        return true;
    }

    private static void PruneBuffs()
    {
        DateTime now = DateTime.UtcNow;
        int removed = Accounts.Change(d =>
        {
            int before = d.Pilot.Buffs.Count;
            d.Pilot.Buffs.RemoveAll(b =>
            {
                if (!ProgressionData.Buffs.TryGetValue(b.Id, out ProgressionData.BuffInfo? info)) return ProgressionData.Buffs.Count > 0;
                if (info.ExpireOnServerLeave) return true;
                return info.ExpireAfter > TimeSpan.Zero && new DateTime(b.StartTicks, DateTimeKind.Utc) + info.ExpireAfter <= now;
            });
            return before - d.Pilot.Buffs.Count;
        });
        if (removed > 0) log("BUFF", removed + " account buff(s) ended (time or server change)");
    }

    // Admin command /buff <id>.
    public static string AddBuff(NetServer server, NetConnection connection, string role, string buffId)
    {
        if (!ProgressionData.Buffs.TryGetValue(buffId, out ProgressionData.BuffInfo? info))
            return "Buff '" + buffId + "' does not exist in the configBinary. Use /buffs to see the list.";

        var buff = new BuffSave
        {
            Id = buffId,
            StartTicks = DateTime.UtcNow.Ticks,
            StartProcId = role,
            StartCharacterId = Game.GetPlaneCharacterId(connection) ?? ""
        };

        bool added = Accounts.Change(d =>
        {
            List<BuffSave> same = d.Pilot.Buffs.Where(b => b.Id == buffId).OrderBy(b => b.StartTicks).ToList();
            if (info.Stacks > 0 && same.Count >= info.Stacks)
            {
                if (string.Equals(info.StackOverflowRule, "Ignore", StringComparison.OrdinalIgnoreCase)) return false;
                d.Pilot.Buffs.Remove(same[0]);
            }
            d.Pilot.Buffs.Add(buff);
            return true;
        });
        if (!added) return "Buff '" + buffId + "' is already at the limit of " + info.Stacks + " and the rule is Ignore.";

        int auditId = Interlocked.Increment(ref nextAuditId);
        ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteClientAddPlayerBuff(m, buff, auditId));
        log("BUFF", "[" + role + "] buff '" + buffId + "' added (auditId " + auditId + ", lasts " +
            (info.ExpireAfter > TimeSpan.Zero ? info.ExpireAfter.ToString() : "until leaving") + ")");
        return "Buff '" + buffId + "' added.";
    }

    // Admin command /no_buffs.
    public static void ClearBuffs(NetServer server, NetConnection connection, string role)
    {
        int removed = Accounts.Change(d =>
        {
            int count = d.Pilot.Buffs.Count;
            d.Pilot.Buffs.Clear();
            return count;
        });
        int auditId = Interlocked.Increment(ref nextAuditId);
        ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteClientUpdatePlayerBuffs(m, new List<BuffSave>(), auditId));
        log("BUFF", "[" + role + "] " + removed + " buff(s) removed (auditId " + auditId + ")");
    }

    public static string BuffListText()
    {
        if (ProgressionData.Buffs.Count == 0) return "No buff in the configBinary.";
        return "Game buffs (" + ProgressionData.Buffs.Count + "): " + string.Join(", ", ProgressionData.Buffs.Keys.OrderBy(k => k));
    }

    // ------------------------------------------------------------------ career rewards

    private static List<CareerRewardSetting> SortedRewards()
    {
        return ProgressionSettingsFile.Current.CareerRewards.Where(r => r.PilotLevel > 0).OrderBy(r => r.PilotLevel).ToList();
    }

    // ServerRequestVagueCareerRewards
    public static void SendVagueCareerRewards(NetServer server, NetConnection connection, string role)
    {
        List<CareerRewardSetting> rewards = SortedRewards();
        ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteClientReceiveVagueCareerRewards(m, rewards));
        log("CAREER", "[" + role + "] ClientReceiveVagueCareerRewards: " + rewards.Count + " reward(s)" +
            (rewards.Count == 0 ? " (progression.json: careerRewards is empty)" : ": " + string.Join(", ", rewards.Select(r => r.Id + "@" + r.PilotLevel))));
    }

    // Delivers every career reward the pilot rank already reached. Also answers ServerRequestCareerRewardDelivery.
    public static void DeliverCareerRewards(NetServer server, NetConnection connection, string role)
    {
        List<CareerRewardSetting> all = SortedRewards();
        if (all.Count == 0) return;

        List<CareerRewardSetting> delivered = Accounts.Change(d =>
        {
            var now = new List<CareerRewardSetting>();
            foreach (CareerRewardSetting reward in all)
            {
                if (reward.PilotLevel > d.Pilot.Level || d.Pilot.CareerRewardsDelivered.Contains(reward.Id)) continue;
                d.Pilot.CareerRewardsDelivered.Add(reward.Id);
                foreach (CareerRewardItem item in reward.Rewards)
                {
                    int type = ProgressionMessages.RewardTypeIndex(item.Type);
                    if (type == 2) PilotProgression.AddPilotXp(d, item.Amount);
                    else if (type == 3 && !string.IsNullOrWhiteSpace(item.Id) && !d.Pilot.Unlocks.Contains(item.Id)) d.Pilot.Unlocks.Add(item.Id);
                }
                now.Add(reward);
            }
            return now;
        });
        if (delivered.Count == 0) return;

        // Plane XP goes to the live plane, if there is one.
        int planeXp = delivered.SelectMany(r => r.Rewards).Where(i => ProgressionMessages.RewardTypeIndex(i.Type) == 1).Sum(i => i.Amount);
        string? characterId = Game.GetPlaneCharacterId(connection);
        if (planeXp > 0 && characterId != null)
            PilotProgression.AwardPlaneXp(server, connection, role, characterId, planeXp, false, "career reward");

        ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteClientReceiveCareerRewardDelivery(m, delivered));
        PilotProgression.SendPilotUpdate(server, connection);
        if (delivered.SelectMany(r => r.Rewards).Any(i => ProgressionMessages.RewardTypeIndex(i.Type) == 3))
            ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteClientUpdatePlayerDataUnlocks(m, PilotProgression.CurrentUnlocks()));

        log("CAREER", "[" + role + "] career rewards delivered: " + string.Join(", ", delivered.Select(r => r.Id + "@" + r.PilotLevel)));
    }
}
