using System.Buffers.Binary;
using Lidgren.Network;

// Bytes of the inventory types and the inventory messages, in the order the client reads them.
// Copied from InventoryItemSerializer, InventoryPositionSerializer, StandardInventorySerializer,
// NetBufferExtensions.WriteBoolBits / WritePrecisionFloat and the Pack_* methods in GeneratedDispatcher.cs.
//
// Copper reminders: a lone bool is 1 bit; generated List<T> serializers write (count + 1) as a variable uint
// (0 = null) but List<int> (IntListCopperSerializer) writes the count and then deltas; Dictionary<string, int>
// writes an Int32 count (-1 = null) and the pairs.
public static class InventoryWire
{
    public const ushort MsgClientReforgeSucceeded = 12155;         // ()
    public const ushort MsgClientReforgeFailed = 12156;            // string reason
    public const ushort MsgClientCargoFull = 14528;                // ()
    public const ushort MsgClientReplaceInventory = 16147;         // StandardInventory
    public const ushort MsgClientReplaceInventoryAndPickup = 16148; // StandardInventory, List<int> newLocations
    public const ushort MsgClientNoRoomForPickup = 22110;          // ()
    public const ushort MsgClientConsumedItemOnPickup = 22111;     // InventoryItem
    public const ushort MsgClientInvAdd = 34491;                   // string characterId, InventoryItem, InventoryPosition, Vector3, int auditId
    public const ushort MsgClientUpdateInventoryItem = 37467;      // string characterId, InventoryItem
    public const ushort MsgClientRemoveInventoryItem = 37468;      // string characterId, InventoryPosition
    public const ushort MsgClientUpdateIngredients = 61591;        // Dictionary<string,int> full, Dictionary<string,int> deltas
    public const ushort MsgClientUpdatePlayer = 61589;
    public const ushort MsgRpcReply = 52428;                       // ushort rpcId, bool ok, value | string fault

    // InventoryItemSerializer.STATMOD_AMOUNT_PRECISION.
    private const float StatModPrecision = 999.99994f;

    // ------------------------------------------------------------------ types

    // GeneralIDSerializer: highbits and lowbits (ulong), the 16 bytes read big-endian (GeneralID.FromBytes).
    public static void WriteGeneralId(NetBuffer buf, byte[] id16)
    {
        buf.Write(BinaryPrimitives.ReadUInt64BigEndian(id16.AsSpan(0, 8)));
        buf.Write(BinaryPrimitives.ReadUInt64BigEndian(id16.AsSpan(8, 8)));
    }

    // InventoryItemSerializer.Write. Empty slot = empty string.
    public static void WriteItem(NetBuffer buf, StoredItem? item)
    {
        if (item == null || string.IsNullOrEmpty(item.DefId))
        {
            buf.Write("");
            return;
        }
        buf.Write(item.DefId);
        buf.Write(item.Id ?? "");
        buf.WriteVariableInt32(item.Uses);
        buf.WriteVariableInt32(item.DecayTimestamp);
        buf.Write(item.Soulbound);
        buf.Write(item.Used);
        buf.Write(item.Salvaged);
        buf.Write(item.Condition);
        buf.Write(item.Favorite);
        List<StoredStatMod> mods = item.Supercharged ?? new List<StoredStatMod>();
        buf.WriteVariableInt32(mods.Count);
        foreach (StoredStatMod mod in mods)
        {
            buf.Write(mod.Name ?? "");
            buf.WriteVariableInt32((int)Math.Round(mod.Amount * StatModPrecision, MidpointRounding.ToEven)); // Mathf.RoundToInt
            buf.WriteVariableInt32(mod.Math);
            buf.Write(mod.Leveling);
            buf.WriteVariableInt32(mod.LevelingSource);
        }
    }

    // InventoryItemSerializer.Read (ServerReportItemPickup carries an item). Null for an empty item.
    public static StoredItem? ReadItem(NetBuffer buf)
    {
        string defId = buf.ReadString() ?? "";
        if (defId.Length == 0) return null;
        var item = new StoredItem
        {
            DefId = defId,
            Id = buf.ReadString() ?? "",
            Uses = buf.ReadVariableInt32(),
            DecayTimestamp = buf.ReadVariableInt32()
        };
        item.Soulbound = buf.ReadBoolean();
        item.Used = buf.ReadBoolean();
        item.Salvaged = buf.ReadBoolean();
        item.Condition = buf.ReadInt32();
        item.Favorite = buf.ReadBoolean();
        int mods = buf.ReadVariableInt32();
        for (int i = 0; i < mods && i < 64; i++)
        {
            item.Supercharged.Add(new StoredStatMod
            {
                Name = buf.ReadString() ?? "",
                Amount = buf.ReadVariableInt32() / StatModPrecision,
                Math = buf.ReadVariableInt32(),
                Leveling = buf.ReadBoolean(),
                LevelingSource = buf.ReadVariableInt32()
            });
        }
        return item;
    }

    // InventoryPositionSerializer.Write: byte (0x80 valid | location << 5 | slot & 31), then the index for
    // Cargo and Permanent.
    public static void WritePosition(NetBuffer buf, InvPos pos)
    {
        byte head = (byte)((pos.Valid ? 0x80 : 0) | ((pos.Location & 3) << 5) | (pos.Slot & 0x1F));
        buf.Write(head);
        if (pos.Valid && pos.HasIndex) buf.WriteVariableInt32(pos.Index);
    }

    // InventoryPositionSerializer.Read.
    public static InvPos ReadPosition(NetBuffer buf)
    {
        byte head = buf.ReadByte();
        bool valid = (head & 0x80) != 0;
        int location = (head >> 5) & 3;
        int slot = head & 0x1F;
        bool hasIndex = location == InvPos.LocCargo || location == InvPos.LocPermanent;
        int index = -1;
        if (valid && hasIndex) index = buf.ReadVariableInt32();
        return new InvPos(valid, location, slot, index);
    }

    // StandardInventorySerializer.Write.
    public static void WriteInventory(NetBuffer buf, byte[] accountId, InvAccess a)
    {
        buf.Write(true);                                    // inv != null
        WriteGeneralId(buf, accountId);                     // AccountId
        buf.Write(a.CharacterId);                           // CharacterId
        buf.WriteVariableInt32(a.Cargo.Count);              // CargoCapacity
        buf.Write(a.Owner);                                 // InventoryOwner

        foreach (int slot in ItemCatalog.EquipSlotsInWireOrder)   // MainGun, SecondaryAttack, Engine, Armor
            WriteItem(buf, Equipped(a, slot, false));

        // WriteBoolBits(MainGun, SecondaryAttack, Engine, Armor): one byte, bit 0 = first.
        int bits = 0;
        for (int i = 0; i < ItemCatalog.EquipSlotsInWireOrder.Length; i++)
        {
            if (Equipped(a, ItemCatalog.EquipSlotsInWireOrder[i], true) != null) bits |= 1 << i;
        }
        buf.Write((byte)bits);
        foreach (int slot in ItemCatalog.EquipSlotsInWireOrder)
        {
            StoredItem? alt = Equipped(a, slot, true);
            if (alt != null) WriteItem(buf, alt);
        }

        buf.Write((ushort)a.Cargo.Count);                   // Cargo
        foreach (StoredItem? item in a.Cargo) WriteItem(buf, item);
        buf.Write((ushort)a.Inv.Permanents.Count);          // Permanents_
        foreach (StoredItem item in a.Inv.Permanents) WriteItem(buf, item);
        WriteStringIntDictionary(buf, a.Inv.Accumulated);   // _accumulatedItems
    }

    private static StoredItem? Equipped(InvAccess a, int slot, bool alternative)
    {
        if (a.IsStorage) return null;
        Dictionary<string, StoredItem> map = alternative ? a.Inv.Alternative : a.Inv.Equipped;
        return map.TryGetValue(ItemCatalog.SlotName(slot), out StoredItem? item) ? item : null;
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

    // Vector3Serializer: three floats.
    public static void WriteVector3(NetBuffer buf, float x, float y, float z)
    {
        buf.Write(x);
        buf.Write(y);
        buf.Write(z);
    }

    // ------------------------------------------------------------------ messages

    public static void WriteClientInvAdd(NetBuffer buf, string characterId, StoredItem item, InvPos pos, float x, float y, int auditId)
    {
        buf.Write(MsgClientInvAdd);
        buf.Write(characterId);
        WriteItem(buf, item);
        WritePosition(buf, pos);
        WriteVector3(buf, x, y, 0f);   // pickedupAtPos: zero = no flying-to-the-HUD animation
        buf.Write(auditId);
    }

    public static void WriteClientUpdateInventoryItem(NetBuffer buf, string characterId, StoredItem item)
    {
        buf.Write(MsgClientUpdateInventoryItem);
        buf.Write(characterId);
        WriteItem(buf, item);
    }

    public static void WriteClientRemoveInventoryItem(NetBuffer buf, string characterId, InvPos pos)
    {
        buf.Write(MsgClientRemoveInventoryItem);
        buf.Write(characterId);
        WritePosition(buf, pos);
    }

    public static void WriteClientReplaceInventory(NetBuffer buf, byte[] accountId, InvAccess a)
    {
        buf.Write(MsgClientReplaceInventory);
        WriteInventory(buf, accountId, a);
    }

    // ClientReplaceInventoryAndPickup: the inventory and the cargo slots that got new items (IntListCopperSerializer).
    public static void WriteClientReplaceInventoryAndPickup(NetBuffer buf, byte[] accountId, InvAccess a, IReadOnlyList<int> newCargoSlots)
    {
        buf.Write(MsgClientReplaceInventoryAndPickup);
        WriteInventory(buf, accountId, a);
        buf.WriteVariableUInt32((uint)newCargoSlots.Count);
        int previous = 0;
        foreach (int slot in newCargoSlots)
        {
            buf.WriteVariableInt32(slot - previous);
            previous = slot;
        }
    }

    public static void WriteClientNoRoomForPickup(NetBuffer buf) => buf.Write(MsgClientNoRoomForPickup);

    public static void WriteClientCargoFull(NetBuffer buf) => buf.Write(MsgClientCargoFull);

    public static void WriteClientConsumedItemOnPickup(NetBuffer buf, StoredItem item)
    {
        buf.Write(MsgClientConsumedItemOnPickup);
        WriteItem(buf, item);
    }

    public static void WriteClientReforgeSucceeded(NetBuffer buf) => buf.Write(MsgClientReforgeSucceeded);

    // The client shows the text "reforge_failed_" + reason.
    public static void WriteClientReforgeFailed(NetBuffer buf, string reason)
    {
        buf.Write(MsgClientReforgeFailed);
        buf.Write(reason ?? "");
    }

    // ClientUpdateIngredients: the whole wallet (deltas null) or only the changes (full null). With deltas the
    // client shows the "+N ingredient" notification.
    public static void WriteClientUpdateIngredients(NetBuffer buf, IReadOnlyDictionary<string, int>? full, IReadOnlyDictionary<string, int>? deltas)
    {
        buf.Write(MsgClientUpdateIngredients);
        WriteStringIntDictionary(buf, full);
        WriteStringIntDictionary(buf, deltas);
    }

    // ClientUpdatePlayer with no plane (only pilot data, which does not change) and the currencies.
    public static void WriteClientUpdatePlayerCurrencies(NetBuffer buf, PilotSave pilot, IReadOnlyDictionary<string, int> currencies)
    {
        buf.Write(MsgClientUpdatePlayer);
        buf.Write("");                     // planeCharacterId: none
        buf.Write(0);                      // planeXp
        buf.Write(0);                      // planeLevel
        buf.Write(pilot.BaselineXp);       // pilotXp (baseline)
        buf.Write(Math.Max(1, pilot.Level)); // pilotLevel
        buf.WriteVariableUInt32(0u);       // unlocks: null = unchanged
        buf.Write(false);                  // fromPersistence
        WriteStringIntDictionary(buf, currencies);
    }

    // ------------------------------------------------------------------ RPC answers

    public static void WriteRpcInt(NetBuffer buf, ushort rpcId, int value)
    {
        buf.Write(MsgRpcReply);
        buf.Write(rpcId);
        buf.Write(true);
        buf.Write(value);
    }

    public static void WriteRpcBool(NetBuffer buf, ushort rpcId, bool value)
    {
        buf.Write(MsgRpcReply);
        buf.Write(rpcId);
        buf.Write(true);
        buf.Write(value);
    }

    // GameServer.ConsumeItemResultSerializer: variable int auditId, bool deleteAfterUse.
    public static void WriteRpcConsumeResult(NetBuffer buf, ushort rpcId, int auditId, bool deleteAfterUse)
    {
        buf.Write(MsgRpcReply);
        buf.Write(rpcId);
        buf.Write(true);
        buf.WriteVariableInt32(auditId);
        buf.Write(deleteAfterUse);
    }

    // A failed RPC: the client gets IsFaulted with this text.
    public static void WriteRpcFault(NetBuffer buf, ushort rpcId, string fault)
    {
        buf.Write(MsgRpcReply);
        buf.Write(rpcId);
        buf.Write(false);
        buf.Write(fault ?? "");
    }
}
