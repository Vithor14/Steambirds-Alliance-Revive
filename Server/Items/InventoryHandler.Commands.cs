using System.Globalization;
using Lidgren.Network;

// Chat commands of the inventory (/item, /loot...) and the text that lists the inventory.
public static partial class InventoryHandler
{
    public static bool Command(NetServer server, NetConnection? connection, string role, List<string> pieces, double now, Action<string, string> logTo)
    {
        if (connection == null || pieces.Count == 0) return false;
        string command = pieces[0].Trim().ToLowerInvariant();
        if (!command.StartsWith("/")) command = "/" + command;
        string? argument = pieces.Count > 1 ? pieces[1].Trim() : null;
        int number = pieces.Count > 2 && int.TryParse(pieces[2], NumberStyles.Integer, Inv, out int n) ? n : 1;
        string tag = "[" + role + "]";

        switch (command)
        {
            case "/items":
                Chat(server, connection, "Items: /item ID [N] (to cargo), /loot [unit] [tier], /inventory, /clear_cargo, " +
                    "/currency ID N, /ingredient ID N, /bags, /clear_bags, /export_items. Settings in items.json; tables in loot_tables.");
                return true;

            case "/item":
            {
                if (string.IsNullOrEmpty(argument)) { Chat(server, connection, "Use /item ID [count]. E.g.: /item armor_3"); return true; }
                ItemDef? def = ItemCatalog.Get(argument);
                if (def == null) { Chat(server, connection, "Item '" + argument + "' does not exist. See catalog\\item_catalog.md."); return true; }
                string? characterId = Game.GetPlaneCharacterId(connection);
                if (characterId == null) { Chat(server, connection, "You need a live plane."); return true; }
                EnsureKnownPlane(characterId);
                int given = 0;
                for (int i = 0; i < Math.Clamp(number, 1, 20); i++)
                {
                    StoredItem item = InventoryStore.NewItem(def.Id);
                    (InvPos pos, int audit) = InventoryStore.Change(d =>
                    {
                        InvAccess? a = InventoryStore.Resolve(d, characterId);
                        if (a == null) return (InvPos.Invalid, Refused);
                        InvPos free = InventoryStore.FirstFreeCargo(a);
                        if (!free.Valid) return (free, Refused);
                        InventoryStore.Set(a, free, item);
                        return (free, InventoryStore.NextAudit(d));
                    });
                    if (!pos.Valid) break;
                    Send(server, connection, m => InventoryWire.WriteClientInvAdd(m, characterId, item, pos, 0f, 0f, audit));
                    given++;
                }
                Chat(server, connection, given > 0 ? given + "x " + ItemCatalog.Name(def.Id) + " in the cargo." : "Cargo full.");
                logTo("INVENTORY", tag + " /item " + def.Id + ": " + given + " in the cargo");
                return true;
            }

            case "/loot":
            {
                if (!Game.GetPlane(connection.RemoteUniqueIdentifier, out float x, out float y, out bool alive, out _) || !alive)
                {
                    Chat(server, connection, "You need a live plane.");
                    return true;
                }
                string unit = string.IsNullOrEmpty(argument) ? "test" : argument;
                int tier = pieces.Count > 2 && int.TryParse(pieces[2], NumberStyles.Integer, Inv, out int t) ? t : 1;
                bool boss = unit.Contains("boss", StringComparison.OrdinalIgnoreCase) || unit == "boss";
                lock (Game.Lock) LootDrops.EnemyKilled(server, connection, role, unit, tier, boss, x + 3f, y, now, logTo);
                Chat(server, connection, "Loot rolled for '" + unit + "' tier " + tier + (boss ? " (boss)" : "") + " in front of you.");
                return true;
            }

            case "/inventory":
                Chat(server, connection, InventoryText(Game.GetPlaneCharacterId(connection)));
                return true;

            case "/clear_cargo":
            {
                string? characterId = Game.GetPlaneCharacterId(connection);
                if (characterId == null) { Chat(server, connection, "You need a live plane."); return true; }
                int removed = InventoryStore.Change(d =>
                {
                    InvAccess? a = InventoryStore.Resolve(d, characterId);
                    if (a == null) return 0;
                    int count = a.Cargo.Count(i => i != null);
                    for (int i = 0; i < a.Cargo.Count; i++) a.Cargo[i] = null;
                    return count;
                });
                SendInventory(server, connection, characterId);
                Chat(server, connection, removed + " item(s) taken from the cargo.");
                return true;
            }

            case "/currency":
            case "/currencies":
            {
                if (string.IsNullOrEmpty(argument))
                {
                    Dictionary<string, int> now2 = InventoryStore.Read(d => new Dictionary<string, int>(d.Currencies));
                    Chat(server, connection, "Currencies: " + (now2.Count == 0 ? "none" : string.Join(", ", now2.Select(c => c.Key + " " + c.Value))) +
                        ". Use /currency ID N (recycling: " + ItemCatalog.MulchCurrency + ").");
                    return true;
                }
                Dictionary<string, int> currencies = InventoryStore.Change(d =>
                {
                    InventoryStore.AddCurrency(d, argument, number);
                    return new Dictionary<string, int>(d.Currencies);
                });
                SendCurrencies(server, connection, currencies);
                Chat(server, connection, argument + ": " + (currencies.TryGetValue(argument, out int v) ? v : 0));
                return true;
            }

            case "/ingredient":
            {
                if (string.IsNullOrEmpty(argument))
                {
                    Chat(server, connection, "Use /ingredient ID N. Game ingredients: " +
                        (ItemCatalog.Ingredients.Count == 0 ? "none in this version" : string.Join(", ", ItemCatalog.Ingredients.Keys.Take(30))));
                    return true;
                }
                int added = InventoryStore.Change(d => InventoryStore.AddIngredient(d, argument, number));
                Send(server, connection, m => InventoryWire.WriteClientUpdateIngredients(m, null, new Dictionary<string, int> { [argument] = added }));
                Chat(server, connection, argument + " " + (added >= 0 ? "+" : "") + added);
                return true;
            }

            case "/bags":
                Chat(server, connection, LootDrops.CountFor(connection) + " of your loot bag(s) in the world. Tables: " +
                    string.Join(", ", LootTables.All.Keys));
                return true;

            case "/clear_bags":
                Chat(server, connection, LootDrops.ClearFor(server, connection, now) + " bag(s) removed.");
                return true;

            case "/export_items":
            {
                string? file = ItemCatalogExport.Export(logTo);
                Chat(server, connection, file == null ? "Could not write the catalog (see the log)." : "Catalog written to " + file);
                return true;
            }
        }
        return false;
    }

    private static string InventoryText(string? characterId)
    {
        return InventoryStore.Read(d =>
        {
            var parts = new List<string>();
            InvAccess? plane = characterId == null ? null : InventoryStore.Resolve(d, characterId);
            if (plane != null)
            {
                parts.Add("Equipped: " + string.Join(", ", plane.Inv.Equipped.Select(p => p.Key + "=" + p.Value.DefId)));
                if (plane.Inv.Alternative.Count > 0) parts.Add("Alternate: " + string.Join(", ", plane.Inv.Alternative.Select(p => p.Value.DefId)));
                parts.Add("Cargo " + plane.Cargo.Count(i => i != null) + "/" + plane.Cargo.Count + ": " +
                    string.Join(", ", plane.Cargo.Where(i => i != null).Select(i => i!.DefId)));
                if (plane.Inv.Accumulated.Count > 0) parts.Add("Upgrades: " + string.Join(", ", plane.Inv.Accumulated.Select(p => p.Key + " x" + p.Value)));
            }
            InvAccess storage = InventoryStore.Resolve(d, d.StorageCharacterId)!;
            parts.Add("Storage " + storage.Cargo.Count(i => i != null) + "/" + storage.Cargo.Count);
            if (d.Currencies.Count > 0) parts.Add("Currencies: " + string.Join(", ", d.Currencies.Select(c => c.Key + " " + c.Value)));
            return string.Join(" | ", parts);
        });
    }

    // ------------------------------------------------------------------ sending

}
