using Lidgren.Network;

// Server -> client messages of the pilot and plane progression.
// Field order copied from the Pack_* methods in GeneratedDispatcher.cs and the serializers they call
// (DictionaryStringIntSerializer, ListstringCopperSerializer, PortraitSerializer, BuffListSerializer,
// VagueCareerReward.Serializer, ActualCareerReward.Serializer, QuestRewardDataSerializer, DrawnLootSerializer,
// InstantLootSerializer).
//
// Copper rules used here: a lone bool is 1 bit; generated List<T> serializers write (count + 1) as a
// variable uint (0 = null); the generic ListCopperSerializer<T> (lists inside other types) writes just the
// count; Dictionary<string, int> writes an Int32 count (-1 = null) and (string, Int32) pairs.
public static class ProgressionMessages
{
    public const ushort MsgIncrementPlayerStats = 879;                 // PeerId, int shortId, float health, float energy, Vector3, bool cap
    public const ushort MsgClientWipePilot = 4870;
    public const ushort MsgClientWipePlaneProgress = 8951;
    public const ushort MsgClientUpdatePlayerPortrait = 12151;         // Portrait
    public const ushort MsgClientReceiveVagueCareerRewards = 12160;    // List<VagueCareerReward>
    public const ushort MsgClientReceiveCareerRewardDelivery = 12162;  // List<ActualCareerReward>
    public const ushort MsgClientUpdateHighestLevels = 21500;          // Dictionary<string, int>
    public const ushort MsgClientReceiveReliableStats = 35051;         // int idx, float time, float number
    public const ushort MsgClientDeathSummaryInformation = 43727;
    public const ushort MsgClientSetPlaneRecord = 44246;               // PlaneRecord
    public const ushort MsgClientUpdatePlayerDataUnlocks = 48915;      // List<string>
    public const ushort MsgClientUpdatePlayerBuffs = 48945;            // BuffList, int auditId
    public const ushort MsgClientAddPlayerBuff = 48946;                // BuffInstance, int auditId
    public const ushort MsgClientUpdatePlayer = 61589;
    public const ushort MsgClientPostFightDetails = 61590;
    public const ushort MsgUpdatePlayerSkills = 65037;                 // int loadoutIndex, string loadoutName, Dictionary<string, int>
    public const ushort MsgTriggerPlayerSkill = 65038;                 // byte trigger, float amount
    public const ushort MsgClientSkillChangeError = 65040;             // string id
    public const ushort MsgClientPilotCheatState = 65041;              // bool

    // PortraitSlotEnum
    public static readonly string[] PortraitSlots = { "Base", "Beak", "Deco", "Clothes", "Eye", "Hat", "Mask", "Pattern" };

    // QuestRewardType
    public static readonly string[] RewardTypes = { "Item", "PlaneXp", "PilotXp", "Unlock", "Quest", "Currency", "AccountItem", "Ingredient" };

    // ------------------------------------------------------------------ shared pieces

    public static void WriteStringList(NetBuffer buf, IReadOnlyList<string>? list)
    {
        if (list == null)
        {
            buf.WriteVariableUInt32(0u);
            return;
        }
        buf.WriteVariableUInt32((uint)(list.Count + 1));
        foreach (string item in list) buf.Write(item ?? "");
    }

    public static void WriteStringIntDictionary(NetBuffer buf, IReadOnlyDictionary<string, int>? dict)
    {
        if (dict == null)
        {
            buf.Write(-1);
            return;
        }
        buf.Write(dict.Count);
        foreach (KeyValuePair<string, int> pair in dict)
        {
            buf.Write(pair.Key);
            buf.Write(pair.Value);
        }
    }

    // PortraitSerializer: byte count, then (byte slot, ushort part id).
    public static void WritePortrait(NetBuffer buf, IReadOnlyDictionary<string, int>? parts)
    {
        var valid = new List<(byte Slot, ushort Part)>();
        if (parts != null)
        {
            foreach (KeyValuePair<string, int> pair in parts)
            {
                int slot = Array.FindIndex(PortraitSlots, s => string.Equals(s, pair.Key, StringComparison.OrdinalIgnoreCase));
                if (slot < 0 || pair.Value <= 0 || pair.Value > ushort.MaxValue) continue;
                valid.Add(((byte)slot, (ushort)pair.Value));
            }
        }
        buf.Write((byte)valid.Count);
        foreach ((byte slot, ushort part) in valid)
        {
            buf.Write(slot);
            buf.Write(part);
        }
    }

    // BuffSerializer: string id, long startTime ticks, string startProcId, string startCharacterId.
    public static void WriteBuff(NetBuffer buf, BuffSave buff)
    {
        buf.Write(buff.Id);
        buf.Write(buff.StartTicks);
        buf.Write(buff.StartProcId ?? "");
        buf.Write(buff.StartCharacterId ?? "");
    }

    // BuffListSerializer: Int32 count + buffs.
    public static void WriteBuffList(NetBuffer buf, IReadOnlyList<BuffSave> buffs)
    {
        buf.Write(buffs.Count);
        foreach (BuffSave buff in buffs) WriteBuff(buf, buff);
    }

    // ------------------------------------------------------------------ messages

    // ClientUpdatePlayer: XP and level of a plane, pilot XP (baseline) and rank, unlocks (null = unchanged),
    // fromPersistence (only changes how the XP bar animates) and currencies (null = unchanged).
    // An empty planeCharacterId updates only the pilot.
    public static void WriteClientUpdatePlayer(NetBuffer buf, string planeCharacterId, int planeXp, int planeLevel,
        int pilotXp, int pilotLevel, IReadOnlyList<string>? unlocks, bool fromPersistence)
    {
        buf.Write(MsgClientUpdatePlayer);
        buf.Write(planeCharacterId ?? "");
        buf.Write(planeXp);
        buf.Write(planeLevel);
        buf.Write(pilotXp);
        buf.Write(pilotLevel);
        WriteStringList(buf, unlocks);
        buf.Write(fromPersistence);
        WriteStringIntDictionary(buf, null);
    }

    // ClientDeathSummaryInformation: the death window waits for this (DeathWrapupStateMachine.WaitingForStats).
    public static void WriteClientDeathSummaryInformation(NetBuffer buf, int planeXp, int planeLevel, string planeClassName,
        int pilotLevelBefore, int pilotLevelAfter, int pilotXpDelta, int pilotXpTotal, bool lostSupercharge)
    {
        buf.Write(MsgClientDeathSummaryInformation);
        buf.Write(planeXp);
        buf.Write(planeLevel);
        buf.Write(planeClassName ?? "");
        buf.Write(pilotLevelBefore);
        buf.Write(pilotLevelAfter);
        buf.Write(pilotXpDelta);
        buf.Write(pilotXpTotal);
        buf.Write(lostSupercharge);
    }

    // ClientPostFightDetails: text near a defeated boss ("you did X% of the damage, loot / no loot, +XP").
    public static void WriteClientPostFightDetails(NetBuffer buf, short templateHash, int shortId, float x, float y,
        float damageShare, float damageThreshold, int xp)
    {
        buf.Write(MsgClientPostFightDetails);
        buf.Write(templateHash);
        buf.Write(shortId);
        buf.Write(x);
        buf.Write(y);
        buf.Write(damageShare);
        buf.Write(damageThreshold);
        buf.Write(xp);
    }

    public static void WriteClientSetPlaneRecord(NetBuffer buf, byte[] accountId, Accounts.SavedPlane plane)
    {
        buf.Write(MsgClientSetPlaneRecord);
        PlayerData.WritePlaneRecord(buf, accountId, plane.CharacterId, plane.Class, plane.Variant, plane.Level, plane.Xp);
    }

    public static void WriteClientUpdateHighestLevels(NetBuffer buf, IReadOnlyDictionary<string, int> highestLevels)
    {
        buf.Write(MsgClientUpdateHighestLevels);
        WriteStringIntDictionary(buf, highestLevels);
    }

    public static void WriteClientUpdatePlayerDataUnlocks(NetBuffer buf, IReadOnlyList<string> unlocks)
    {
        buf.Write(MsgClientUpdatePlayerDataUnlocks);
        WriteStringList(buf, unlocks);
    }

    public static void WriteUpdatePlayerSkills(NetBuffer buf, int loadoutIndex, string loadoutName, IReadOnlyDictionary<string, int> skills)
    {
        buf.Write(MsgUpdatePlayerSkills);
        buf.Write(loadoutIndex);
        buf.Write(loadoutName ?? "");
        WriteStringIntDictionary(buf, skills);
    }

    public static void WriteClientSkillChangeError(NetBuffer buf, string skillId)
    {
        buf.Write(MsgClientSkillChangeError);
        buf.Write(skillId ?? "");
    }

    // TriggerPlayerSkill: the client runs its pilot skills for this trigger (SkillTriggerType as a byte).
    public static void WriteTriggerPlayerSkill(NetBuffer buf, byte trigger, float amount)
    {
        buf.Write(MsgTriggerPlayerSkill);
        buf.Write(trigger);
        buf.Write(amount);
    }

    public static void WriteClientPilotCheatState(NetBuffer buf, bool state)
    {
        buf.Write(MsgClientPilotCheatState);
        buf.Write(state);
    }

    public static void WriteClientWipePilot(NetBuffer buf)
    {
        buf.Write(MsgClientWipePilot);
    }

    public static void WriteClientUpdatePlayerPortrait(NetBuffer buf, IReadOnlyDictionary<string, int> parts)
    {
        buf.Write(MsgClientUpdatePlayerPortrait);
        WritePortrait(buf, parts);
    }

    public static void WriteClientUpdatePlayerBuffs(NetBuffer buf, IReadOnlyList<BuffSave> buffs, int auditId)
    {
        buf.Write(MsgClientUpdatePlayerBuffs);
        WriteBuffList(buf, buffs);
        buf.Write(auditId);
    }

    public static void WriteClientAddPlayerBuff(NetBuffer buf, BuffSave buff, int auditId)
    {
        buf.Write(MsgClientAddPlayerBuff);
        WriteBuff(buf, buff);
        buf.Write(auditId);
    }

    // IncrementPlayerStats: heals / recharges a player plane that is authoritative on its own client.
    public static void WriteIncrementPlayerStats(NetBuffer buf, long peerId, int unitShortId, float health, float energy,
        float x, float y, bool capHealthEffect)
    {
        buf.Write(MsgIncrementPlayerStats);
        buf.Write(peerId);
        buf.Write(unitShortId);
        buf.Write(health);
        buf.Write(energy);
        buf.Write(x);
        buf.Write(y);
        buf.Write(0f);
        buf.Write(capHealthEffect);
    }

    // ClientReceiveReliableStats: stat 0 is the number of players near the plane, the plane included
    // (PlayerScript.GetNearbyOtherPlayers = value - 1). The client forgets it after a few seconds.
    public static void WriteClientReceiveReliableStats(NetBuffer buf, int index, float serverTime, float value)
    {
        buf.Write(MsgClientReceiveReliableStats);
        buf.Write(index);
        buf.Write(serverTime);
        buf.Write(value);
    }

    // ClientReceiveVagueCareerRewards: the rewards shown in the Pilot School timeline.
    public static void WriteClientReceiveVagueCareerRewards(NetBuffer buf, IReadOnlyList<CareerRewardSetting> rewards)
    {
        buf.Write(MsgClientReceiveVagueCareerRewards);
        buf.WriteVariableUInt32((uint)(rewards.Count + 1));
        foreach (CareerRewardSetting reward in rewards)
        {
            buf.Write(reward.Id ?? "");
            WriteCareerCondition(buf, reward.PilotLevel);
            buf.WriteVariableUInt32((uint)reward.Rewards.Count);     // ListCopperSerializer: count without +1
            foreach (CareerRewardItem item in reward.Rewards)
            {
                // QuestRewardDataSerializer: byte type, string id, int data, byte slot (0 = none), float weight, string category.
                buf.Write((byte)RewardTypeIndex(item.Type));
                buf.Write(item.Id ?? "");
                buf.Write(item.Amount);
                buf.Write((byte)0);
                buf.Write(1f);
                buf.Write("");
            }
        }
    }

    // ClientReceiveCareerRewardDelivery: what was delivered now (the client only writes it to its log).
    public static void WriteClientReceiveCareerRewardDelivery(NetBuffer buf, IReadOnlyList<CareerRewardSetting> rewards)
    {
        buf.Write(MsgClientReceiveCareerRewardDelivery);
        buf.WriteVariableUInt32((uint)(rewards.Count + 1));
        foreach (CareerRewardSetting reward in rewards)
        {
            buf.Write(reward.Id ?? "");
            WriteCareerCondition(buf, reward.PilotLevel);
            buf.WriteVariableUInt32(1u);                              // one DrawnLoot
            var items = reward.Rewards.Where(r => RewardTypeIndex(r.Type) == 0).Select(r => r.Id ?? "").ToList();
            buf.Write(items.Count);
            foreach (string item in items) buf.Write(item);
            buf.Write(1);                                             // one InstantLoot with the totals
            int planeXp = reward.Rewards.Where(r => RewardTypeIndex(r.Type) == 1).Sum(r => r.Amount);
            int pilotXp = reward.Rewards.Where(r => RewardTypeIndex(r.Type) == 2).Sum(r => r.Amount);
            var unlocks = reward.Rewards.Where(r => RewardTypeIndex(r.Type) == 3).Select(r => r.Id ?? "").ToList();
            buf.Write(planeXp);
            buf.Write(pilotXp);
            buf.Write("");                                            // currency_id
            buf.Write(0);                                             // currency_amount
            buf.Write("");                                            // ingredient_id
            buf.Write(0);                                             // ingredient_amount
            buf.Write(unlocks.Count);
            foreach (string unlock in unlocks) buf.Write(unlock);
            buf.Write(0);                                             // quests
            buf.Write("");                                            // popupSetpiece
            buf.Write("");                                            // conversation
            buf.Write("");                                            // buff
            buf.Write("");                                            // accountItem
        }
    }

    // CareerRewardCondition.Serializer: int pilotLevel, string unusualConditionId.
    private static void WriteCareerCondition(NetBuffer buf, int pilotLevel)
    {
        buf.Write(pilotLevel);
        buf.Write("");
    }

    public static int RewardTypeIndex(string? type)
    {
        if (string.IsNullOrEmpty(type)) return 2;
        int index = Array.FindIndex(RewardTypes, t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : 2;
    }

    // ------------------------------------------------------------------ sending

    public static void Send(NetServer server, NetConnection? connection, Action<NetOutgoingMessage> write)
    {
        if (connection == null || connection.Status != NetConnectionStatus.Connected) return;
        NetOutgoingMessage message = server.CreateMessage();
        write(message);
        server.SendMessage(message, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }
}
