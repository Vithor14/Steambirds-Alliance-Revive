using Lidgren.Network;

// Items of a retired plane (hangar "Retire plane", ServerRetirePlane).
public static partial class InventoryHandler
{
    // The plane leaves the account on purpose, so (unlike a fall) its equipped items are not lost: they go to the storage,
    // and what does not fit goes to the free cargo slots. The inventory of the plane is deleted. Returns a log summary.
    public static string PlaneRetired(NetServer server, NetConnection connection, string role, string characterId)
    {
        List<string> remainingPlanes = Accounts.Planes().Select(a => a.CharacterId).Where(id => id != characterId).ToList();

        string summary = InventoryStore.Change(d =>
        {
            InvAccess? plane = InventoryStore.Resolve(d, characterId);
            if (plane == null) return "the plane had no inventory";
            InvAccess storage = InventoryStore.Resolve(d, d.StorageCharacterId)!;

            var items = plane.Inv.Equipped.Concat(plane.Inv.Alternative).Select(p => p.Value).ToList();
            // Own cargo of the plane (with the shared pod the cargo belongs to the account and stays where it is).
            if (!ItemSettingsFile.Current.SharedCargoForAllPlanes)
            {
                items.AddRange(plane.Cargo.Where(i => i != null).Select(i => i!));
                for (int i = 0; i < plane.Cargo.Count; i++) plane.Cargo[i] = null;
            }

            int toStorage = 0, toCargo = 0, lost = 0;
            InvAccess? cargoOwner = remainingPlanes.Count > 0 ? InventoryStore.Resolve(d, remainingPlanes[0]) : null;
            foreach (StoredItem item in items)
            {
                InvPos free = InventoryStore.FirstFreeCargo(storage);
                if (free.Valid)
                {
                    storage.Cargo[free.Index] = item;
                    toStorage++;
                    continue;
                }
                free = cargoOwner != null ? InventoryStore.FirstFreeCargo(cargoOwner) : InvPos.Invalid;
                if (free.Valid)
                {
                    cargoOwner!.Cargo[free.Index] = item;
                    toCargo++;
                }
                else
                {
                    lost++;
                }
            }

            d.Planes.Remove(characterId);
            return toStorage + " item(s) to storage" + (toCargo > 0 ? ", " + toCargo + " to the cargo" : "") + (lost > 0 ? ", " + lost + " did not fit and were lost" : "");
        });

        log("INVENTORY", "[" + role + "] plane retired " + characterId + ": " + summary);
        SendInventory(server, connection, InventoryStore.StorageCharacterId);
        foreach (string other in remainingPlanes) SendInventory(server, connection, other);
        return summary;
    }
}
