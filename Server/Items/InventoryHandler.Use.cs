using System.Globalization;
using Lidgren.Network;

// Consuming items and picking up loot bags.
public static partial class InventoryHandler
{
    // Consumable items: the client already ran the actions on its plane (health, steam, clouds); the account
    // effects (unlocks, currencies, buffs, ingredients) are delivered here and the item loses one use (or leaves).
    // Actions with no server side make the RPC fail, so the item is not lost; dyes, dungeon keys and XP boosts are delivered in ItemActions.
    private static void Consume(NetServer server, NetConnection connection, string role, ushort rpc, string characterId, InvPos pos)
    {
        string tag = "[" + role + "]";
        EnsureKnownPlane(characterId);
        StoredItem? item = InventoryStore.Read(d =>
        {
            InvAccess? a = InventoryStore.Resolve(d, characterId);
            return a == null ? null : InventoryStore.Get(a, pos)?.Clone();
        });
        ItemDef? def = item == null ? null : ItemCatalog.Get(item.DefId);
        if (item == null || def == null)
        {
            Send(server, connection, m => InventoryWire.WriteRpcFault(m, rpc, "consumable_item_error"));
            log("INVENTORY", tag + " consume " + pos + ": item not found");
            return;
        }
        if (def.Uses > 0 && item.Uses <= 0)
        {
            Send(server, connection, m => InventoryWire.WriteRpcFault(m, rpc, "consumable_item_error_no_uses_left"));
            return;
        }

        List<ItemActionEffect> effects = ItemActions.Read(def, out string? unsupported);
        if (unsupported != null)
        {
            Send(server, connection, m => InventoryWire.WriteRpcFault(m, rpc, "consumable_item_error_not_supported"));
            log("INVENTORY", tag + " consume " + item.DefId + ": action '" + unsupported + "' does not exist on the server yet (the item stays)");
            return;
        }

        string? refused = ItemActions.Check(connection, role, effects, out string why);
        if (refused != null)
        {
            Send(server, connection, m => InventoryWire.WriteRpcFault(m, rpc, refused));
            log("INVENTORY", tag + " consume " + item.DefId + " refused: " + why + " (the item stays)");
            return;
        }

        bool delete = false;
        StoredItem? after = null;
        int audit = InventoryStore.Change(d =>
        {
            InvAccess? a = InventoryStore.Resolve(d, characterId);
            StoredItem? current = a == null ? null : InventoryStore.Get(a, pos);
            if (a == null || current == null || current.Id != item.Id) return Refused;
            if (def.Uses > 0)
            {
                current.Uses = Math.Max(0, current.Uses - 1);
                delete = current.Uses <= 0;
            }
            else
            {
                // Unlimited dyes and the trails stay after being used; every other item without uses is spent.
                delete = !ItemActions.IsReusable(def);
            }
            if (delete) InventoryStore.Set(a, pos, null);
            else after = current.Clone();
            return InventoryStore.NextAudit(d);
        });
        if (audit == Refused)
        {
            Send(server, connection, m => InventoryWire.WriteRpcFault(m, rpc, "consumable_item_error"));
            return;
        }

        Send(server, connection, m => InventoryWire.WriteRpcConsumeResult(m, rpc, audit, delete));
        if (after != null) Send(server, connection, m => InventoryWire.WriteClientUpdateInventoryItem(m, characterId, after));
        string result = ItemActions.Apply(server, connection, role, effects);
        log("INVENTORY", tag + " consumed " + item.DefId + (delete ? "" : " (left " + after?.Uses + " uso(s))") + (result.Length > 0 ? ": " + result : ""));
    }

    // ------------------------------------------------------------------ pickup

    // ServerRequestAddInventoryItem: the item of a bag goes to the first free cargo slot of the live plane.
    private static void Pickup(NetServer server, NetConnection connection, string role, string itemId, int bagShortId, float px, float py, double now)
    {
        string tag = "[" + role + "]";
        if (!LootDrops.TryTakeForPickup(connection, bagShortId, itemId, out StoredItem item, out float x, out float y))
        {
            log("LOOT", tag + " bag pickup request " + bagShortId + " (item " + itemId + "): unknown bag or already collected");
            return;
        }
        ItemDef? def = ItemCatalog.Get(item.DefId);

        // Ingredients and "devour on pickup" items never reach the cargo.
        if (def != null && (def.IngredientId.Length > 0 || def.DevourOnPickup))
        {
            Send(server, connection, m => InventoryWire.WriteClientConsumedItemOnPickup(m, item));
            if (def.IngredientId.Length > 0)
            {
                int amount = ItemActions.IngredientAmount(def);
                int added = InventoryStore.Change(d => InventoryStore.AddIngredient(d, def.IngredientId, amount));
                if (added != 0)
                    Send(server, connection, m => InventoryWire.WriteClientUpdateIngredients(m, null, new Dictionary<string, int> { [def.IngredientId] = added }));
                log("LOOT", tag + " ingredient " + def.IngredientId + " +" + added);
            }
            else
            {
                List<ItemActionEffect> effects = ItemActions.Read(def, out _);
                string result = ItemActions.Apply(server, connection, role, effects);
                log("LOOT", tag + " " + item.DefId + " consumed on pickup" + (result.Length > 0 ? ": " + result : ""));
            }
            return;
        }

        string? characterId = Game.GetPlaneCharacterId(connection);
        if (characterId == null)
        {
            LootDrops.ReturnToWorld(server, connection, item, x, y, now);
            log("LOOT", tag + " pickup without a live plane: " + item.DefId + " went back to the world");
            return;
        }
        EnsureKnownPlane(characterId);

        InvPos placed = InventoryStore.Change(d =>
        {
            InvAccess? a = InventoryStore.Resolve(d, characterId);
            if (a == null) return InvPos.Invalid;
            InvPos free = InventoryStore.FirstFreeCargo(a);
            if (free.Valid) InventoryStore.Set(a, free, item);
            return free;
        });
        if (!placed.Valid)
        {
            Send(server, connection, InventoryWire.WriteClientNoRoomForPickup);
            LootDrops.ReturnToWorld(server, connection, item, x, y, now);
            log("LOOT", tag + " cargo full: " + item.DefId + " went back to the world");
            return;
        }
        int audit = InventoryStore.Change(d => InventoryStore.NextAudit(d));
        Send(server, connection, m => InventoryWire.WriteClientInvAdd(m, characterId, item, placed, x, y, audit));
        log("LOOT", tag + " picked up " + item.DefId + " (" + (def != null ? def.Slot + " T" + def.Tier : "unknown item") + ") -> cargo " + placed.Index);
    }

    // ------------------------------------------------------------------ chat commands

}
