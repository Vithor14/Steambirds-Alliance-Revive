using Lidgren.Network;

// Hands items of the server to a player: career rewards of the "Item" type and anything else the server gives for free.
// The item goes to the cargo of the live plane (or to the account storage when there is no plane or the cargo is full);
// what finds no room is returned, so the caller can try again later.
public static class ItemDelivery
{
    private sealed class Placed
    {
        public string CharacterId = "";
        public StoredItem Item = null!;
        public InvPos Pos;
        public int Audit;
    }

    // Tries to place each defId. Returns the ones that did NOT fit (in the same order). Tells the client about the others.
    public static List<string> Give(NetServer server, NetConnection connection, string role, IEnumerable<string> defIds, string reason)
    {
        var wanted = defIds.Where(id => ItemCatalog.Get(id) != null).ToList();
        var unknown = defIds.Where(id => ItemCatalog.Get(id) == null).ToList();
        if (unknown.Count > 0)
            InventoryHandler.Log("ITEM", "[" + role + "] " + reason + ": unknown item(s) " + string.Join(", ", unknown) + " (skipped)");
        if (wanted.Count == 0) return new List<string>();

        string? planeId = Game.GetPlaneCharacterId(connection);
        Accounts.SavedPlane? plane = planeId == null ? null : Accounts.GetPlane(planeId);

        var placed = new List<Placed>();
        var leftover = new List<string>();
        InventoryStore.Change(d =>
        {
            InvAccess? planeAccess = null;
            if (plane != null)
            {
                if (!d.Planes.ContainsKey(plane.CharacterId)) InventoryStore.EnsurePlane(d, plane.CharacterId, plane.Class);
                planeAccess = InventoryStore.Resolve(d, plane.CharacterId);
            }
            InvAccess storage = InventoryStore.Resolve(d, d.StorageCharacterId)!;

            foreach (string defId in wanted)
            {
                InvAccess? target = null;
                if (planeAccess != null && InventoryStore.FirstFreeCargo(planeAccess).Valid) target = planeAccess;
                else if (InventoryStore.FirstFreeCargo(storage).Valid) target = storage;
                if (target == null)
                {
                    leftover.Add(defId);
                    continue;
                }
                StoredItem item = InventoryStore.NewItem(defId);
                InvPos pos = InventoryStore.FirstFreeCargo(target);
                InventoryStore.Set(target, pos, item);
                placed.Add(new Placed { CharacterId = target.CharacterId, Item = item, Pos = pos, Audit = InventoryStore.NextAudit(d) });
            }
            return 0;
        });

        foreach (Placed p in placed)
            InventoryHandler.Send(server, connection, m => InventoryWire.WriteClientInvAdd(m, p.CharacterId, p.Item, p.Pos, 0f, 0f, p.Audit));
        InventoryHandler.Log("ITEM", "[" + role + "] " + reason + ": " + (placed.Count > 0 ? "delivered " + string.Join(", ", placed.Select(p => p.Item.DefId)) : "nothing delivered") +
            (leftover.Count > 0 ? "; no room for " + string.Join(", ", leftover) + " (kept for later)" : ""));
        return leftover;
    }
}
