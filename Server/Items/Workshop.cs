using Lidgren.Network;

// The Workshop (WorkshopUI): reforging a worn item back to pristine and supercharging tier 9+ equipment.
//
// ServerReforgeItemFromIngredients(characterId, item position, characterIds, positions): the client sends the
// item and the items it chose to pay with (from the plane and the storage). The price is the game's
// ReforgeConfig (config "reforging"): items matching tags/tier/id and currencies, looked up with the same keys
// the client uses (InventoryItem.GetReforgeCost). The answer is ClientReforgeSucceeded or
// ClientReforgeFailed(reason): the client shows the text "reforge_failed_" + reason.
//
// Reforge (condition < 0): the item becomes pristine again (condition 0; the success window says so).
// Supercharge (condition >= 0, tier 9+): condition 1 and new stats rolled from SuperchargeNewStatProbabilities
// (InventoryItem.Supercharge: entries without itemId roll for everyone, the others only when that item was paid).
public static class Workshop
{
    public static void Reforge(NetServer server, NetConnection connection, string role, string characterId, InvPos pos,
        List<string> payIds, List<InvPos> payPositions)
    {
        string tag = "[" + role + "]";
        if (!ItemSettingsFile.Current.ReforgeEnabled)
        {
            Fail(server, connection, tag, "disabled", "reforging is off in items.json");
            return;
        }

        var removed = new List<(string CharacterId, InvPos Pos)>();
        StoredItem? result = null;
        Dictionary<string, int>? currencies = null;
        bool supercharged = false;
        string reason = "";
        string detail = "";

        bool ok = InventoryStore.Change(d =>
        {
            InvAccess? a = InventoryStore.Resolve(d, characterId);
            StoredItem? item = a == null ? null : InventoryStore.Get(a, pos);
            ItemDef? def = item == null ? null : ItemCatalog.Get(item.DefId);
            if (a == null || item == null || def == null) { reason = "item"; detail = "item not found in " + pos; return false; }

            supercharged = item.Condition >= 0;
            if (supercharged && !ItemCatalog.CanBeSupercharged(def)) { reason = "item"; detail = item.DefId + " cannot be supercharged"; return false; }
            if (!supercharged && !ItemCatalog.CanBeDegraded(def)) { reason = "item"; detail = item.DefId + " has no condition"; return false; }

            ReforgeCostInfo? cost = ItemCatalog.ReforgeCost(def, item.Condition);
            if (cost == null) { reason = "cost"; detail = "no reforge cost for " + item.DefId + " (config 'reforging')"; return false; }

            // The items offered as payment.
            var offered = new List<(InvAccess Inv, InvPos Pos, StoredItem Item, ItemDef Def)>();
            var seen = new HashSet<string>();
            for (int i = 0; i < Math.Min(payIds.Count, payPositions.Count); i++)
            {
                InvAccess? pa = InventoryStore.Resolve(d, payIds[i]);
                InvPos pp = payPositions[i];
                StoredItem? paid = pa == null ? null : InventoryStore.Get(pa, pp);
                ItemDef? paidDef = paid == null ? null : ItemCatalog.Get(paid.DefId);
                if (pa == null || paid == null || paidDef == null) { reason = "items"; detail = "payment " + pp + " not found"; return false; }
                if (pp.Location == InvPos.LocPermanent) { reason = "items"; detail = "permanent upgrade does not pay for a reforge"; return false; }
                if (paid.Id == item.Id || paid.Favorite) { reason = "items"; detail = "invalid payment (the item itself or a favorite)"; return false; }
                if (!seen.Add(paid.Id)) continue;
                offered.Add((pa, pp, paid, paidDef));
            }

            // Most specific requirements first (ReforgeCost.ItemsReforgeCost.CompareTo), one offered item each.
            var used = new List<(InvAccess Inv, InvPos Pos, StoredItem Item, ItemDef Def)>();
            foreach (ReforgeItemCost need in cost.Items.OrderByDescending(Constraints))
            {
                var matches = offered.Where(o => !used.Contains(o) && need.Matches(o.Def)).Take(need.Count).ToList();
                if (matches.Count < need.Count) { reason = "items"; detail = "missing items: " + need; return false; }
                used.AddRange(matches);
            }

            foreach (KeyValuePair<string, int> price in cost.Currencies)
            {
                if (InventoryStore.Currency(d, price.Key) < price.Value) { reason = "currency"; detail = "missing " + price.Value + " " + price.Key; return false; }
            }

            // Pay.
            foreach (var entry in used)
            {
                InventoryStore.Set(entry.Inv, entry.Pos, null);
                removed.Add((entry.Inv.CharacterId, entry.Pos));
            }
            foreach (KeyValuePair<string, int> price in cost.Currencies) InventoryStore.AddCurrency(d, price.Key, -price.Value);
            if (cost.Currencies.Count > 0) currencies = new Dictionary<string, int>(d.Currencies);

            if (supercharged)
            {
                item.Condition = 1;
                item.Supercharged.Clear();
                HashSet<string> paidIds = new HashSet<string>(used.Select(u => u.Item.DefId), StringComparer.Ordinal);
                foreach (SuperchargeChance chance in ItemCatalog.SuperchargeChances(def))
                {
                    if (chance.ItemId.Length > 0 && !paidIds.Contains(chance.ItemId)) continue;
                    if (Random.Shared.NextDouble() <= chance.Probability)
                        item.Supercharged.AddRange(chance.StatMods.Select(StoredStatMod.From));
                }
            }
            else
            {
                item.Condition = 0;
            }
            item.Salvaged = false;
            result = item.Clone();
            detail = string.Join(", ", used.Select(u => u.Item.DefId)) +
                     (cost.Currencies.Count > 0 ? " + " + string.Join(", ", cost.Currencies.Select(c => c.Value + " " + c.Key)) : "");
            return true;
        });

        if (!ok || result == null)
        {
            Fail(server, connection, tag, reason.Length > 0 ? reason : "item", detail);
            return;
        }

        foreach ((string id, InvPos p) in removed)
            InventoryHandler.Send(server, connection, m => InventoryWire.WriteClientRemoveInventoryItem(m, id, p));
        StoredItem done = result;
        InventoryHandler.Send(server, connection, m => InventoryWire.WriteClientUpdateInventoryItem(m, characterId, done));
        if (currencies != null) InventoryHandler.SendCurrencies(server, connection, currencies);
        InventoryHandler.Send(server, connection, InventoryWire.WriteClientReforgeSucceeded);
        InventoryHandler.Log("WORKSHOP", tag + " " + done.DefId + (supercharged
            ? " supercharged (" + done.Supercharged.Count + " stat(s) extra: " + string.Join(", ", done.Supercharged.Select(s => s.Name + " " + s.Amount)) + ")"
            : " reforged (condition 0)") + " | paid: " + detail);
    }

    // Number of constraints of a requirement (the most specific is matched first).
    private static int Constraints(ReforgeItemCost c)
    {
        int n = 0;
        if (c.Id.Length > 0) n++;
        if (c.Tier.HasValue) n++;
        if (c.MinTier.HasValue) n++;
        return n + c.Tags.Count;
    }

    private static void Fail(NetServer server, NetConnection connection, string tag, string reason, string detail)
    {
        InventoryHandler.Send(server, connection, m => InventoryWire.WriteClientReforgeFailed(m, reason));
        InventoryHandler.Log("WORKSHOP", tag + " reforge refused (" + reason + "): " + detail);
    }
}
