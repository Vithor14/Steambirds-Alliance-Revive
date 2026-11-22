using Lidgren.Network;

// Bytes of the store messages, in the order the client reads them. Copied from StoreShelveSerializer,
// StoreShelveItemSerializer, ListStoreShelveCopperSerializer and the Pack_* methods of GeneratedDispatcher.cs.
//
// Copper reminders: a lone bool is 1 bit, and the string after it starts in the middle of a byte (Lidgren handles
// that as long as the message is written with NetBuffer.Write(bool) like the client does: StoreShelveItem has a bool
// before the product id). A List<T> goes as (count + 1) in a variable uint, 0 = null.
public static class StoreWire
{
    public const ushort MsgClientReceiveStoreContent = 54290;
    public const ushort MsgClientNotificationGiveProduct = 54390;
    public const ushort MsgClientUpdatePlayerDataSlots = 48914;
    public const ushort MsgRpcReply = 52428;

    // ClientReceiveStoreContent(string storeId, List<StoreShelve> shelves).
    public static void WriteStoreContent(NetBuffer buf, string storeId, IReadOnlyList<Shelf> shelves)
    {
        buf.Write(MsgClientReceiveStoreContent);
        buf.Write(storeId);
        buf.WriteVariableUInt32((uint)shelves.Count + 1u);          // never 0: the client does a foreach on the list
        foreach (Shelf shelf in shelves)
        {
            buf.Write(shelf.StoreId);                                // StoreShelveSerializer.Write
            buf.Write(shelf.SectionId);
            buf.Write("");                                           // active_filler: none
            buf.Write(0);                                            // filler_end_timestamp: no timer
            buf.Write(shelf.Items.Count);
            foreach (ShelfItem item in shelf.Items)
            {
                buf.Write(item.Amount);                              // StoreShelveItemSerializer.Write
                buf.Write(0);                                        // expires_timestamp: never
                buf.Write(item.Product.SortOrder);
                buf.Write(item.HasLimitedAmount);                    // 1 bit
                buf.Write(item.Product.Id);
                buf.Write(item.Discount);
            }
        }
    }

    // ClientNotificationGiveProduct(GeneralID accountId, string planeId, string storeId, string sectionId, string productId).
    // The client adds the account items and unlocks of the product to its own PlayerData, so the slot count and the
    // escalating price (it counts the account items) are right at once.
    public static void WriteGiveProduct(NetBuffer buf, byte[] accountId, string planeId, string storeId, string sectionId, string productId)
    {
        buf.Write(MsgClientNotificationGiveProduct);
        InventoryWire.WriteGeneralId(buf, accountId);
        buf.Write(planeId);
        buf.Write(storeId);
        buf.Write(sectionId);
        buf.Write(productId);
    }

    // ClientUpdatePlayerDataSlots(int numHangarSlots, int numAdditionalStorageSlots, int numAdditionalInventorySlots, int numSkillLoadoutSlots).
    public static void WriteSlots(NetBuffer buf, int hangar, int additionalStorage, int additionalInventory, int loadouts)
    {
        buf.Write(MsgClientUpdatePlayerDataSlots);
        buf.Write(hangar);
        buf.Write(additionalStorage);
        buf.Write(additionalInventory);
        buf.Write(loadouts);
    }

    // Answer of an RPC that returns a string (ServerPurchaseInStore): bool true, then the string.
    public static void WriteRpcString(NetBuffer buf, ushort rpcId, string value)
    {
        buf.Write(MsgRpcReply);
        buf.Write(rpcId);
        buf.Write(true);
        buf.Write(value);
    }

    // Answer of an RPC that returns a list (ServerGetBuyCurrencyPackages): bool true, then an empty list (count + 1 = 1).
    public static void WriteRpcEmptyList(NetBuffer buf, ushort rpcId)
    {
        buf.Write(MsgRpcReply);
        buf.Write(rpcId);
        buf.Write(true);
        buf.WriteVariableUInt32(1u);
    }
}
