using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Lidgren.Network;

// Acceptance of the EULA (the game's LegalNoticeAcceptance)
public class LegalAcceptance
{
    public long Ticks { get; set; }
    public int TextHash { get; set; }
    public string Locale { get; set; } = "";
    public string BuildVersion { get; set; } = "";
    public string BuildCommit { get; set; } = "";
}

// Mirror of what the client reads. Order checked in:
// PlayerDataSerializer.Write, PilotSerializer.Write, PortraitSerializer.Write,
// StandardInventorySerializer.Write, BuffListSerializer.Write,
// LegalNoticeAcceptancesSerializer.Write, DictionaryStringIntSerializer.Write,
// GeneralIDSerializer.Write, GeneratedDispatcher.Pack_ClientWelcomeMessage,
// Pack_ClientUpdateLegalNoticeAcceptances, Pack_ClientSwitchToServer, Pack_ClientSwitchToServerWithPlane,
// Pack_ClientPortalResult and NetBufferRegisteredHostExtensions.Write.
public static class PlayerData
{
    public const ushort MsgServerRequestCreateCharacter = 54849; // 0xD641
    public const ushort MsgClientAddPlane = 55110;               // 0xD746
    public const ushort MsgClientRemovePlane = 55111;            // 0xD747
    public const ushort MsgClientWelcomeMessage = 48913;         // 0xBF11
    public const ushort MsgServerAcceptLegalNotice = 54200;      // 0xD3B8
    public const ushort MsgClientUpdateLegalNoticeAcceptances = 54201; // 0xD3B9
    public const ushort MsgClientSwitchToServer = 48573;         // 0xBDBD
    public const ushort MsgClientSwitchToServerWithPlane = 48574; // 0xBDBE
    public const ushort MsgClientPortalResult = 59188;           // 0xE734
    public const ushort MsgClientReceiveRemotePlayerData = 11177; // 0x2BA9

    // Level of a newly created plane. The client starts the PlaneRecord at 1 (PlaneRecord._level = 1).
    public const int StartingPlaneLevel = 1;

    // numHangarSlots of the PlayerData: how many planes the account can have = the host's base value
    // (progression.json: hangarSlots) plus the hangar slots the account bought in the store (Stores\StoreAccounts).
    public static int HangarSlots =>
        Math.Clamp(ProgressionSettingsFile.Current.HangarSlots, 1, StoreRules.MaxHangarSlots) + StoreAccounts.Count(StoreAccounts.HangarSlot);

    // netIdShort of each class and variant, copied from the runtime_values.txt of this build.
    // The client identifies class and variant by these numbers inside the unit options.
    public static readonly Dictionary<string, short> ClassesNetIdShort = new Dictionary<string, short>
    {
        ["assault"] = -7007,
        ["medic"] = -24946,
        ["rapier"] = -31614,
        ["tank"] = -14153,
        ["walker"] = 25083,
        ["olfaithful"] = -22350,
        ["duster"] = -20135,
        ["support"] = -30671,
        ["paladin"] = 6175,
        ["wizard"] = 14140,
        ["engineer"] = -17816,
        ["pirate"] = 14968,
        ["siegetank"] = -23904,
        ["daggerknight"] = -25958,
        ["vampire"] = -9258,
    };

    public static readonly Dictionary<string, short> VariantNetIdShort = new Dictionary<string, short>
    {
        ["EpicC"] = -30300,
        ["EpicB"] = -30299,
        ["EpicD"] = -30297,
        ["EpicA"] = -30294,
        ["default"] = -13285,
        ["flames"] = -12646,
        ["VariantA"] = -9592,
        ["spikes"] = -7484,
        ["gold"] = -6105,
        ["Tiger"] = -1995,
        ["stars"] = 1335,
        ["VariantB"] = 14724,
        ["Camo"] = 29433,
        ["VarientA"] = -9716,
    };

    // Capacity of the plane's cargo. The client reads Cargo[i] for each item read, so the list
    // needs this capacity. With 0, EquipItemInCargoSlot(0) blows up with ArgumentOutOfRange.
    // 12 = default cargoSize of CombatSimCharBuildSimple.
    public const int CargoCapacity = 12;

    // Builds the starting items of a class (slot -> defId).
    static Dictionary<string, string> Items(string mainGun, string engine, string secondaryAttack, string armor)
    {
        return new Dictionary<string, string>
        {
            ["MainGun"] = mainGun,
            ["Engine"] = engine,
            ["SecondaryAttack"] = secondaryAttack,
            ["Armor"] = armor,
        };
    }

    // Starting items per class, from the startingItem of the runtime_values.txt of this build.
    // Each item becomes an InventoryItem equipped in the matching slot when entering the game.
    public static readonly Dictionary<string, Dictionary<string, string>> StartingItemsByClass =
        new Dictionary<string, Dictionary<string, string>>
        {
            ["assault"] = Items("shotgun_0", "engine_0", "damage_0", "armor_0"),
            ["medic"] = Items("triple_0", "engine_0", "heal_0", "armor_0"),
            ["rapier"] = Items("laser_0", "engine_0", "mine_0", "armor_0"),
            ["tank"] = Items("machinegun_0", "engine_0", "poison_blast_0", "armor_0"),
            ["walker"] = Items("machinegun_0", "engine_0", "damageboost_blast_0", "armor_0"),
            ["olfaithful"] = Items("triple_0", "engine_0", "poison_0", "armor_0"),
            ["duster"] = Items("sniper_0", "engine_0", "damage_0", "armor_0"),
            ["support"] = Items("strike_0", "engine_0", "resist_aura_0", "armor_0"),
            ["paladin"] = Items("sword_0", "engine_0", "healaura_0", "armor_0"),
            ["wizard"] = Items("strike_0", "engine_0", "vortex_0", "armor_0"),
            ["engineer"] = Items("wasp_0", "engine_0", "turret_0", "armor_0"),
            ["pirate"] = Items("shotgun_0", "engine_0", "stunner_0", "armor_0"),
            ["siegetank"] = Items("laser_0", "engine_0", "flak_0", "armor_0"),
            ["daggerknight"] = Items("sniper_0", "engine_0", "shield_blast_0", "armor_0"),
            ["vampire"] = Items("sword_0", "engine_0", "bite_0", "armor_0"),
        };

    // GeneralIDType: Character = 7, CharacterItem = 8 (GeneralIDType.cs).
    private const int CharacterType = 7;
    private const int CharacterItemType = 8;

    // 16 valid bytes for GeneralID: type in the top 6 bits of the first byte (highbits >> 58)
    // and non-zero lowbits (IsValid).
    public static byte[] NewId(int type)
    {
        var id = RandomNumberGenerator.GetBytes(16);
        return FixId(id, type);
    }

    private static byte[] FixId(byte[] id, int type)
    {
        id[0] = (byte)((type << 2) | (id[0] & 0x03));
        bool lowZeroed = true;
        for (int i = 8; i < 16; i++)
        {
            if (id[i] != 0) { lowZeroed = false; break; }
        }
        if (lowZeroed) id[15] = 1;
        return id;
    }

    public static byte[] NewCharacterId() => NewId(CharacterType);

    // Same format as GeneralID.ToBase64: base64url without '=' (22 characters).
    public static string ToBase64Url(byte[] id16)
    {
        return Convert.ToBase64String(id16).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    // Inverse of ToBase64Url. Returns null if the text is not a 16-byte id.
    public static byte[]? FromBase64Url(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length != 22) return null;
        try
        {
            var bytes = Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + "==");
            return bytes.Length == 16 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // Ids and bytes of the items: Items/InventoryStore.cs (StartingItemId, NewItemId) and Items/InventoryWire.cs (WriteItem).

    // StandardInventorySerializer.Write for the plane's inventory (Owner = InventoryOwner.Plane = 1).
    // The items come from the authoritative inventory (Items/InventoryStore, inventory.json): equipped, alternate gun,
    // cargo (shared between planes, as in the original), upgrades. A plane without an inventory gets the class's
    // starting items, with the same ids as before.
    static void WritePlaneInventory(NetBuffer buf, byte[] accountId, string characterId, string planeClass)
    {
        bool exists = InventoryStore.Read(d => d.Planes.ContainsKey(characterId));
        if (!exists) InventoryStore.Change(d => InventoryStore.EnsurePlane(d, characterId, planeClass));
        InventoryStore.Read(d =>
        {
            InvAccess? inventory = InventoryStore.Resolve(d, characterId);
            if (inventory != null) InventoryWire.WriteInventory(buf, accountId, inventory);
            else WriteStorageInventory(buf, accountId);
            return 0;
        });
    }

    // StandardInventorySerializer.Write of the account's storage (Owner = Storage), with the stored items.
    static void WriteAccountStorage(NetBuffer buf, byte[] accountId)
    {
        InventoryStore.Read(d =>
        {
            InventoryWire.WriteInventory(buf, accountId, InventoryStore.Resolve(d, d.StorageCharacterId)!);
            return 0;
        });
    }

    // PlaneRecordSerializer.Write. level and XP come from the account (Progression/PilotProgression).
    public static void WritePlaneRecord(NetBuffer buf, byte[] accountId, string characterId, string planeClass, string planeVariant,
        int level = StartingPlaneLevel, int xp = 0)
    {
        WriteGeneralId(buf, accountId);       // AccountId
        buf.Write(characterId);               // characterId
        buf.Write(planeClass);                // planeClass
        buf.Write(planeVariant);              // planeVariant
        buf.Write((byte)Math.Clamp(level, 1, 255)); // level (byte; the client starts at 1)
        buf.WriteVariableInt32(Math.Max(0, xp));    // XP
        PlaneDecoration.Write(buf, Accounts.GetPlane(characterId)?.Decoration);   // decoration (DecorationSerializer)
        buf.Write("");                        // combatSimMode
        buf.Write(0);                         // combatSimVer
        buf.Write(0);                         // combatSimSeed
        WritePlaneInventory(buf, accountId, characterId, planeClass);
    }

    // GeneratedDispatcher.Pack_ClientReceiveRemotePlayerData (ID 11177): answer to ServerRequestRemotePlayerData.
    // Order: int unitShortId, GeneralID accountId, string playerName, GeneralID flockId, string flockName,
    // bool isAdmin, bool isTester (1 bit each), int planeLevel, Pilot, StandardInventory, string serverName, bool isJoinable.
    // Without a plane (null characterId) the inventory goes empty, in the storage format.
    public static void WriteClientReceiveRemotePlayerData(NetBuffer buf, int unitShortId, byte[] accountId, string playerName,
        bool isAdmin, string? characterId, string? planeClass, string serverName, int planeLevel, PilotSave pilot)
    {
        buf.Write(MsgClientReceiveRemotePlayerData);
        buf.Write(unitShortId);               // unit of the plane (0 = no plane)
        WriteGeneralId(buf, accountId);       // accountId
        buf.Write(playerName);                // playerName
        WriteNoneGeneralId(buf);              // flockId = none
        buf.Write("");                        // flockName
        buf.Write(isAdmin);                   // isAdmin (1 bit)
        buf.Write(false);                     // isTester (1 bit)
        buf.Write(planeLevel);                // planeLevel
        WritePilot(buf, accountId, pilot);   // Pilot
        if (!string.IsNullOrEmpty(characterId) && !string.IsNullOrEmpty(planeClass))
            WritePlaneInventory(buf, accountId, characterId, planeClass);
        else
            WriteStorageInventory(buf, accountId);
        buf.Write(serverName);                // serverName
        buf.Write(false);                     // isJoinable
    }

    // GeneratedDispatcher.Pack_ClientAddPlane (ID 55110)
    public static void WriteClientAddPlane(NetBuffer buf, byte[] accountId, string characterId, string planeClass, string planeVariant,
        int level = StartingPlaneLevel, int xp = 0)
    {
        buf.Write(MsgClientAddPlane);
        WritePlaneRecord(buf, accountId, characterId, planeClass, planeVariant, level, xp);
    }

    // GeneratedDispatcher.Pack_ClientRemovePlane (ID 55111): the plane leaves the client's hangar (same record as ClientAddPlane).
    // Write it before the plane's inventory is removed: the record carries the inventory.
    public static void WriteClientRemovePlane(NetBuffer buf, byte[] accountId, string characterId, string planeClass, string planeVariant,
        int level = StartingPlaneLevel, int xp = 0)
    {
        buf.Write(MsgClientRemovePlane);
        WritePlaneRecord(buf, accountId, characterId, planeClass, planeVariant, level, xp);
    }

    // GeneralIDSerializer.Write writes highbits and lowbits (ulong).
    // GeneralID.FromBytes reads the 16 bytes in big-endian.
    static void WriteGeneralId(NetBuffer buf, byte[] id16)
    {
        buf.Write(BinaryPrimitives.ReadUInt64BigEndian(id16.AsSpan(0, 8)));
        buf.Write(BinaryPrimitives.ReadUInt64BigEndian(id16.AsSpan(8, 8)));
    }

    static void WriteNoneGeneralId(NetBuffer buf)
    {
        buf.Write(0UL);
        buf.Write(0UL);
    }

    // PilotSerializer.Write: rank, XP, what the interface already showed, skill loadouts and portrait.
    // At most numSkillLoadoutSlots loadouts go (the client erases the extra ones in Pilot.UpdateAfterPersisting).
    // Empty name: the client shows "Loadout N" in its language.
    public static void WritePilot(NetBuffer buf, byte[] accountId, PilotSave pilot)
    {
        int slots = PilotProgression.SkillLoadoutSlots();
        int count = Math.Clamp(pilot.SkillLoadouts.Count, 1, slots);
        int active = pilot.SkillLoadoutIndex >= 0 && pilot.SkillLoadoutIndex < count ? pilot.SkillLoadoutIndex : 0;

        WriteGeneralId(buf, accountId);           // AccountId
        buf.Write(Math.Max(1, pilot.Level));     // level
        buf.Write(Math.Max(0, pilot.BaselineXp)); // baselineXP
        buf.Write(pilot.LastXpLevelupShown);     // lastXPLevelupShown
        buf.Write(pilot.LastLevelupShown);       // lastLevelupShown
        buf.Write(count);                    // skillLoadouts.Count
        for (int i = 0; i < count; i++)
        {
            Dictionary<string, int> skills = i < pilot.SkillLoadouts.Count ? pilot.SkillLoadouts[i] : new Dictionary<string, int>();
            ProgressionMessages.WriteStringIntDictionary(buf, skills); // DictionaryStringIntSerializer
        }
        buf.Write(active);                         // skillLoadoutIndex
        buf.Write(count);                    // skillLoadoutNames.Count
        for (int i = 0; i < count; i++)
            buf.Write(i < pilot.SkillLoadoutNames.Count ? pilot.SkillLoadoutNames[i] ?? "" : "");
        ProgressionMessages.WritePortrait(buf, pilot.Portrait); // PortraitSerializer
    }

    // Slots of the account's storage: GlobalConfig.baseNumStorageSlots (16 in the configBinary) + numAdditionalStorageSlots (0).
    // With 0 slots the Storage window broke when opened (UGUIStorage reads Cargo[0] even with no slot:
    // "IndexOutOfRangeException: Cargo Index Out of Range [Cargo 0] 0") and the plane stayed stuck docked.
    public const int StorageCapacity = 16;

    // StandardInventorySerializer.Write: empty storage inventory (capacity slots, all empty)
    static void WriteStorageInventory(NetBuffer buf, byte[] accountId, int capacity = 0)
    {
        buf.Write(true);                 // inv != null
        WriteGeneralId(buf, accountId);  // AccountId
        buf.Write("");                   // CharacterId
        buf.WriteVariableInt32(capacity); // CargoCapacity
        buf.Write((byte)0);              // Owner = InventoryOwner.Storage
        for (int i = 0; i < 4; i++)      // Equipped: null
            buf.Write("");               // InventoryItemSerializer: null item = empty string
        buf.Write((byte)0);              // WriteBoolBits: no EquippedAlternative
        buf.Write((ushort)capacity);   // Cargo.Count
        for (int i = 0; i < capacity; i++)
            buf.Write("");               // empty slot
        buf.Write((ushort)0);            // Permanents_.Count
        buf.Write(0);                    // _accumulatedItems (DictionaryStringInt)
    }

    // LegalNoticeAcceptancesSerializer.Write: int count; per entry:
    //     // string key, long ticks, int textHash, string locale, string buildVersion, string buildCommit
    public static void WriteLegalNoticeAcceptances(NetBuffer buf, Dictionary<string, LegalAcceptance> acceptances)
    {
        buf.Write(acceptances.Count);
        foreach (var pair in acceptances)
        {
            buf.Write(pair.Key);
            buf.Write(pair.Value.Ticks);
            buf.Write(pair.Value.TextHash);
            buf.Write(pair.Value.Locale);
            buf.Write(pair.Value.BuildVersion);
            buf.Write(pair.Value.BuildCommit);
        }
    }

    // GeneratedDispatcher.Pack_ClientUpdateLegalNoticeAcceptances (ID 54201)
    public static void WriteClientUpdateLegalNotices(NetBuffer buf, Dictionary<string, LegalAcceptance> acceptances)
    {
        buf.Write(MsgClientUpdateLegalNoticeAcceptances);
        WriteLegalNoticeAcceptances(buf, acceptances);
    }

    // PlayerDataSerializer.Write. The account's planes go (planes, with XP and level), the pilot, the unlocks,
    // the highest levels per class and the finished dungeons (finishedDungeonTypes): without "DungeonTutorialWalker"
    // in it, the client asks for the tutorial when entering the Nexus.
    public static void WritePlayerData(NetBuffer buf, byte[] accountId, string playerName, Dictionary<string, LegalAcceptance> acceptances,
        IReadOnlyList<Accounts.SavedPlane> planes, IReadOnlyList<string> finishedDungeons, PilotSave pilot)
    {
        WriteGeneralId(buf, accountId);        // accountId
        buf.Write(playerName);                 // playerName
        // unlocks: HasUnlockedPlane testa unlocks.Contains(unlockItemId); walker = playerclass_walker
        List<string> unlocks = pilot.Unlocks.Count > 0 ? pilot.Unlocks : new List<string> { "playerclass_walker" };
        buf.WriteVariableInt32(unlocks.Count);
        foreach (string item in unlocks) buf.Write(item);
        buf.WriteVariableInt32(pilot.HighestLevels.Count); // highestLevels: class -> highest level
        foreach (KeyValuePair<string, int> pair in pilot.HighestLevels)
        {
            buf.Write(pair.Key);
            buf.Write(pair.Value);
        }
        buf.Write(0);                          // currentQuests
        buf.Write(0);                          // prevQuests
        buf.Write("");                         // activeQuestId
        WriteLegalNoticeAcceptances(buf, acceptances); // legalNoticeAcceptances
        // accumulatedItems: what the account bought in the stores (hangar_slot, storage_slot...). The client counts them to
        // price the next slot, so they must be the same ones the server counts (Stores\StoreAccounts).
        ProgressionMessages.WriteStringIntDictionary(buf, StoreAccounts.AccountItems());
        buf.WriteVariableInt32(ItemSettingsFile.Current.ExtraStorageSlots + StoreAccounts.Count(StoreAccounts.StorageSlot)); // numAdditionalStorageSlots (items.json + store)
        buf.WriteVariableInt32(ItemSettingsFile.Current.ExtraCargoSlots + StoreAccounts.Count(StoreAccounts.InventorySlot)); // numAdditionalInventorySlots (items.json + store)
        buf.WriteVariableInt32(HangarSlots); // numHangarSlots (progression.json: hangarSlots + store)
        buf.WriteVariableInt32(PilotProgression.SkillLoadoutSlots()); // numSkillLoadoutSlots (progression.json: skillLoadoutSlots + store)
        // planeXpAdditionalPercentage and, when it is not 0, its "until" (unix seconds): the boost of the xp consumables
        int boost = PlaneXpBoost.ActivePercent(pilot);
        buf.WriteVariableInt32(boost);
        if (boost != 0) buf.WriteVariableInt32(pilot.PlaneXpBoostUntil);

        buf.WriteVariableInt32(planes.Count);  // planes
        foreach (Accounts.SavedPlane plane in planes)
            WritePlaneRecord(buf, accountId, plane.CharacterId, plane.Class, plane.Variant, plane.Level, plane.Xp);

        WritePilot(buf, accountId, pilot);    // pilot
        // buffs: only the ones the client knows (BuffSerializer.Read stops reading at an unknown id)
        ProgressionMessages.WriteBuffList(buf, PilotExtras.BuffsToSend(pilot));
        WriteAccountStorage(buf, accountId);   // storageInv (Items/InventoryStore)

        buf.WriteVariableInt32(finishedDungeons.Count); // finishedDungeonTypes
        foreach (string dungeon in finishedDungeons) buf.Write(dungeon);

        // currencies: recycling scrap and other currencies (inventory.json). VarInt count, (string, Int32).
        Dictionary<string, int> currencies = InventoryStore.Read(d => new Dictionary<string, int>(d.Currencies));
        buf.WriteVariableInt32(currencies.Count);
        foreach (KeyValuePair<string, int> currency in currencies)
        {
            buf.Write(currency.Key);
            buf.Write(currency.Value);
        }
        buf.WriteVariableInt32(0);             // nonStandartItems
        buf.WriteVariableInt32(0);             // lootReceivedCount
        buf.WriteVariableInt32(0);             // lastServerIDs
        buf.WriteVariableInt32(0);             // lastSnapshotTime
        buf.Write(0);                          // combatSimAttempts
        WriteNoneGeneralId(buf);               // defaultFlockId = GeneralID.None
    }

    // GeneratedDispatcher.Pack_ClientWelcomeMessage (ID 48913)
    // serverRole: 0 = Nexus, 2 = Dungeon, 3 = Tutorial (ServerRole)
    // terrainName: TerrainConfig the client loads (territories[].name in the configBinary, e.g. "DungeonBomb");
    // null = the default of the role. scenario: GameClient.ConnectedServerScenario. The location label of the HUD
    // shows TextMachine.GetDungeonNameOrNull(scenario), so a dungeon sends its terrain name there too.
    public static void WriteClientWelcome(NetBuffer buf, byte[] accountId, string playerName, Dictionary<string, LegalAcceptance> acceptances,
        byte serverRole, string procId, IReadOnlyList<Accounts.SavedPlane> planes, IReadOnlyList<string> finishedDungeons,
        PilotSave pilot, string? terrainName = null, string scenario = "")
    {
        // real names: territories[].name in the configBinary
        var terrain = !string.IsNullOrEmpty(terrainName) ? terrainName : serverRole == 3 ? "DungeonTutorialWalker" : "Nexus";

        buf.Write(MsgClientWelcomeMessage);
        buf.Write(terrain);                    // terrainName
        buf.Write(12.0f);                      // terrainCellSize = WorldLayout.cellSize (= GroundPlacement.tileSize)
        buf.Write(650.0f);                     // terrainWorldRadius = WorldLayout.worldRadius default
        buf.Write(0);                          // wallsPerCell
        buf.Write(serverRole);                 // serverRole
        buf.Write(procId);                     // procId
        buf.Write("revive");                   // procName
        buf.Write(scenario ?? "");             // scenario
        buf.Write(1);                          // clientHostSpace (guess)
        WritePlayerData(buf, accountId, playerName, acceptances, planes, finishedDungeons, pilot);
        buf.Write("revive-world");             // worldId
        buf.Write(0);                          // bossesInitial
        buf.Write(0);                          // bossesRemaining
    }

    // NetBufferRegisteredHostExtensions.Write: byte (1 | count<<4), long id, IPEndPoint[], string hostName,
    // string version, var numPlayers, var maxPlayers, byte tags.Count (+ strings), byte fields.Count (+ pairs).
    // The "nexus" tag makes the client show the Nexus loading screen.
    public static void WriteRegisteredHost(NetBuffer buf, IPEndPoint endpoint, long id, string hostName, string version,
        int maxPlayers, string[]? tags)
    {
        buf.Write((byte)(1 | (1 << 4)));       // present, 1 endpoint
        buf.Write(id);                         // host id (PeerId; 0 = none)
        buf.Write(endpoint);                   // endpoint (IP:port)
        buf.Write(hostName);
        buf.Write(version);
        buf.WriteVariableInt32(0);             // numPlayers
        buf.WriteVariableInt32(maxPlayers);    // maxPlayers
        int count = tags == null ? 0 : Math.Min(tags.Length, 127);
        buf.Write((byte)count);           // tags.Count
        for (int i = 0; i < count; i++) buf.Write(tags![i]);
        buf.Write((byte)0);                    // fields.Count
    }

    // GeneratedDispatcher.Pack_ClientSwitchToServer (ID 48573): so o RegisteredHost.
    public static void WriteClientSwitchToServer(NetBuffer buf, IPEndPoint endpoint, long id, string hostName, string version,
        string[]? tags = null)
    {
        buf.Write(MsgClientSwitchToServer);
        WriteRegisteredHost(buf, endpoint, id, hostName, version, 10, tags);
    }

    // GeneratedDispatcher.Pack_ClientSwitchToServerWithPlane (ID 48574): RegisteredHost and PlaneRecord.
    // The client stores the characterId (m_storedPlaneCharacterId) and, on the new server, requests the spawn
    // of that plane by itself (GameClient: PlaneSelection state with a stored plane).
    public static void WriteClientSwitchToServerWithPlane(NetBuffer buf, IPEndPoint endpoint, long id, string hostName,
        string version, string[]? tags, byte[] accountId, string characterId, string planeClass, string planeVariant,
        int level = StartingPlaneLevel, int xp = 0)
    {
        buf.Write(MsgClientSwitchToServerWithPlane);
        WriteRegisteredHost(buf, endpoint, id, hostName, version, 10, tags);
        WritePlaneRecord(buf, accountId, characterId, planeClass, planeVariant, level, xp);
    }

    // GeneratedDispatcher.Pack_ClientPortalResult (ID 59188): int portalShortId, bool result (1 bit).
    // false makes the client leave the EnteringPortal state and show "error_portal" in the chat.
    public static void WriteClientPortalResult(NetBuffer buf, int portalShortId, bool result)
    {
        buf.Write(MsgClientPortalResult);
        buf.Write(portalShortId);
        buf.Write(result);
    }

    // Options of the player's unit, in the order PlayerScript.ApplyEntOptions reads
    // (the same order as the client's PlayerScript.CreateEntOptions):
    // short class, short variant, GeneralID account (16 bytes), GeneralID character (16 bytes), decoration.
    // Decoration with source = 0 (1 byte), the same as WritePlaneRecord already sends.
    public static void WritePlaneOptions(NetBuffer buf, short netIdClass, short netIdVariant, byte[] accountId, byte[] characterId,
        SavedDecoration? decoration = null)
    {
        buf.Write(netIdClass);
        buf.Write(netIdVariant);
        WriteGeneralId(buf, accountId);
        WriteGeneralId(buf, characterId);
        PlaneDecoration.Write(buf, decoration);
    }
}
