using System.Globalization;
using Lidgren.Network;

// Moving, equipping and sorting items between the plane, the storage and the bags.
public static partial class InventoryHandler
{
    private static void Swap(NetServer server, NetConnection connection, string tag, ushort rpc, string sourceId, InvPos sourcePos,
        string destId, InvPos destPos)
    {
        EnsureKnownPlane(sourceId);
        EnsureKnownPlane(destId);
        string? why = null;
        string moved = "";
        int result = InventoryStore.Change(d =>
        {
            InvAccess? a = InventoryStore.Resolve(d, sourceId);
            InvAccess? b = InventoryStore.Resolve(d, destId);
            if (a == null || b == null) { why = "unknown inventory"; return Refused; }
            if (!InventoryStore.PositionExists(a, sourcePos) || !InventoryStore.PositionExists(b, destPos)) { why = "invalid position"; return Refused; }
            if (sourcePos.Location == InvPos.LocPermanent || destPos.Location == InvPos.LocPermanent) { why = "permanent upgrades cannot be moved"; return Refused; }
            bool sameList = a.Cargo == b.Cargo && sourcePos.Location == InvPos.LocCargo && destPos.Location == InvPos.LocCargo;
            if ((a == b || sameList) && sourcePos.Equals(destPos)) { why = "same position"; return Refused; }

            StoredItem? first = InventoryStore.Get(a, sourcePos);
            StoredItem? second = InventoryStore.Get(b, destPos);
            if (first == null && second == null) { why = "both positions are empty"; return Refused; }
            if (!FitsPosition(first, destPos, out why)) return Refused;
            if (!FitsPosition(second, sourcePos, out why)) return Refused;

            InventoryStore.Set(a, sourcePos, null);
            InventoryStore.Set(b, destPos, null);
            InventoryStore.Set(a, sourcePos, second);
            InventoryStore.Set(b, destPos, first);
            if (first != null && destPos.IsEquip) first.Used = true;
            if (second != null && sourcePos.IsEquip) second.Used = true;
            moved = (first?.DefId ?? "empty") + " " + Place(a, sourcePos) + " <-> " + (second?.DefId ?? "empty") + " " + Place(b, destPos);
            return InventoryStore.NextAudit(d);
        });

        Send(server, connection, m => InventoryWire.WriteRpcInt(m, rpc, result));
        if (result == Refused)
        {
            log("INVENTORY", tag + " swap refused: " + why + " (" + sourcePos + " -> " + destPos + ")");
            // Unequipping into a full cargo: the client prints "Cargo is full".
            if (sourcePos.IsEquip && destPos.Location == InvPos.LocCargo) Send(server, connection, InventoryWire.WriteClientCargoFull);
        }
        else
        {
            log("INVENTORY", tag + " swap #" + result + ": " + moved);
        }
    }

    // An item can go to an equipment slot only if it is equipment of that slot (alternative: main gun only).
    private static bool FitsPosition(StoredItem? item, InvPos pos, out string? why)
    {
        why = null;
        if (item == null || !pos.IsEquip) return true;
        ItemDef? def = ItemCatalog.Get(item.DefId);
        if (def == null) { why = "unknown item " + item.DefId; return false; }
        if (def.SlotValue != pos.Slot) { why = item.DefId + " e " + def.Slot + ", not " + ItemCatalog.SlotName(pos.Slot); return false; }
        if (pos.Location == InvPos.LocAlternative && pos.Slot != ItemCatalog.SlotMainGun) { why = "only the main gun has an alternate slot"; return false; }
        return true;
    }

    private static string Place(InvAccess a, InvPos pos) => (a.IsStorage ? "storage" : "plane") + pos;

    // A plane of the account that has no inventory yet (planes created before this version).
    private static void EnsureKnownPlane(string characterId)
    {
        if (string.IsNullOrEmpty(characterId) || characterId == InventoryStore.StorageCharacterId) return;
        Accounts.SavedPlane? plane = Accounts.GetPlane(characterId);
        if (plane == null) return;
        InventoryStore.Change(d => InventoryStore.EnsurePlane(d, plane.CharacterId, plane.Class));
    }

    // ------------------------------------------------------------------ remove (drop and trash = recycle)

    private static void Remove(NetServer server, NetConnection connection, string tag, ushort rpc, string characterId, InvPos pos,
        byte destination, double now)
    {
        EnsureKnownPlane(characterId);
        // This client's "Drop" entry of the inventory menu sends Trash (InventoryDisplay -> TrashItemInCargoSlot), and only a drag to the
        // in-world zone sends Drop. So Trash puts the item in the world too, unless the host turned trashDropsInWorld off (then it recycles).
        bool trash = destination == DestinationTrash;
        bool toWorld = !trash || ItemSettingsFile.Current.TrashDropsInWorld;
        bool mulch = !toWorld && ItemSettingsFile.Current.MulchOnTrash;
        string? why = null;
        StoredItem? removed = null;
        int value = 0;
        Dictionary<string, int>? currencies = null;

        // Dropping needs a live plane to put the bag next to: without one the item stays in the cargo (it is never lost).
        float planeX = 0f, planeY = 0f;
        bool planeAlive = Game.GetPlane(connection.RemoteUniqueIdentifier, out planeX, out planeY, out bool alive, out _) && alive;

        int result = InventoryStore.Change(d =>
        {
            if (toWorld && !planeAlive) { why = "no live plane to drop the item next to"; return Refused; }
            InvAccess? a = InventoryStore.Resolve(d, characterId);
            if (a == null) { why = "unknown inventory"; return Refused; }
            if (pos.Location != InvPos.LocCargo || !InventoryStore.PositionExists(a, pos)) { why = "only cargo items can be dropped"; return Refused; }
            StoredItem? item = InventoryStore.Get(a, pos);
            if (item == null) { why = "empty position"; return Refused; }
            InventoryStore.Set(a, pos, null);
            removed = item;
            if (mulch)
            {
                value = ItemCatalog.MulchValue(ItemCatalog.Get(item.DefId));
                if (value > 0)
                {
                    InventoryStore.AddCurrency(d, ItemCatalog.MulchCurrency, value);
                    currencies = new Dictionary<string, int>(d.Currencies);
                }
            }
            return InventoryStore.NextAudit(d);
        });

        Send(server, connection, m => InventoryWire.WriteRpcInt(m, rpc, result));
        if (result == Refused || removed == null)
        {
            log("INVENTORY", tag + " removal refused: " + why + " " + pos);
            return;
        }

        if (currencies != null) SendCurrencies(server, connection, currencies);

        if (toWorld)
        {
            // Drop: the item goes back to the world in a bag at the plane (it can be picked up again after a second).
            int bag = LootDrops.SpawnBag(server, connection, removed, planeX, planeY, now, "dropped by the player");
            log("INVENTORY", tag + " " + removed + " dropped into the world (bag " + bag + ") of " + pos);
        }
        else
        {
            log("INVENTORY", tag + " " + removed + " went to the trash" + (mulch ? ": +" + value + " " + ItemCatalog.MulchCurrency : "") + " of " + pos);
        }
    }

    // ------------------------------------------------------------------ favorite, accumulate, sort

    private static void Favorite(NetServer server, NetConnection connection, string tag, ushort rpc, string characterId, InvPos pos, bool favorite)
    {
        EnsureKnownPlane(characterId);
        int result = InventoryStore.Change(d =>
        {
            InvAccess? a = InventoryStore.Resolve(d, characterId);
            StoredItem? item = a == null ? null : InventoryStore.Get(a, pos);
            if (item == null) return Refused;
            item.Favorite = favorite;
            return favorite ? 1 : 0;   // the client sets favorite = result > 0
        });
        Send(server, connection, m => InventoryWire.WriteRpcInt(m, rpc, result));
        log("INVENTORY", tag + " favorite " + pos + ": " + (result == Refused ? "refused" : favorite ? "marked" : "unmarked"));
    }

    // Upgrade items ("upgrades"): the item leaves the cargo and becomes a permanent stat bonus of the plane
    //     // (StandardInventory.Accumulate / _accumulatedItems).
    private static void Accumulate(NetServer server, NetConnection connection, string tag, ushort rpc, string characterId, InvPos pos)
    {
        EnsureKnownPlane(characterId);
        string? why = null;
        string defId = "";
        int total = 0;
        int result = InventoryStore.Change(d =>
        {
            InvAccess? a = InventoryStore.Resolve(d, characterId);
            if (a == null || a.IsStorage) { why = "only planes accumulate upgrades"; return Refused; }
            StoredItem? item = InventoryStore.Get(a, pos);
            if (item == null) { why = "empty position"; return Refused; }
            ItemDef? def = ItemCatalog.Get(item.DefId);
            if (def != null && !def.IsUpgrade) { why = item.DefId + " is not an upgrade (" + def.Slot + ")"; return Refused; }
            InventoryStore.Set(a, pos, null);
            a.Inv.Accumulated.TryGetValue(item.DefId, out total);
            total++;
            a.Inv.Accumulated[item.DefId] = total;
            defId = item.DefId;
            return InventoryStore.NextAudit(d);
        });
        Send(server, connection, m => InventoryWire.WriteRpcInt(m, rpc, result));
        log("INVENTORY", tag + (result == Refused ? " upgrade refused: " + why : " upgrade " + defId + " applied to the plane (" + total + "x)"));
    }

    private static void Sort(NetServer server, NetConnection connection, string tag, ushort rpc, string characterId, int sortType)
    {
        EnsureKnownPlane(characterId);
        bool ok = InventoryStore.Change(d =>
        {
            InvAccess? a = InventoryStore.Resolve(d, characterId);
            if (a == null || (sortType != SortByType && sortType != SortByTier)) return false;
            // StandardInventory.Sort, with a stable order: tier high to low, or slot type; empty slots last.
            List<StoredItem?> sorted = sortType == SortByTier
                ? a.Cargo.OrderByDescending(i => i == null ? -2 : (ItemCatalog.Get(i.DefId)?.Tier ?? -1)).ToList()
                : a.Cargo.OrderBy(i => i == null ? 1000 : (ItemCatalog.Get(i.DefId)?.SlotValue ?? 999)).ToList();
            for (int i = 0; i < sorted.Count; i++) a.Cargo[i] = sorted[i];
            return true;
        });
        Send(server, connection, m => InventoryWire.WriteRpcBool(m, rpc, ok));
        // The client sorts its copy with List.Sort, which is not stable: the server's order goes right after.
        if (ok) SendInventory(server, connection, characterId);
        log("INVENTORY", tag + " sort by " + (sortType == SortByTier ? "tier" : sortType == SortByType ? "type" : sortType.ToString(Inv)) +
            (ok ? "" : ": refused"));
    }

    // ------------------------------------------------------------------ consume

}
