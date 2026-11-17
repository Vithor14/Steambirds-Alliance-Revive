using Lidgren.Network;

// The "actions" of consumable items (ItemConfig.boundActions) when an item is consumed or devoured on pickup.
// The client runs the item's actions on its own plane BEFORE asking the server (GameClient: Perform, then
// invTransport.Consume): health/steam, clouds, guns, maneuvers, buffs and summons need nothing here.
// The server delivers what lives in the account: unlocks, plane variants, currencies, account buffs and
// ingredients. Dyes, dungeon keys and the XP boost still have no server side, so items with them are refused
// (the item stays) instead of being used up for nothing.
public sealed class ItemActionEffect
{
    public string Kind = "";      // unlock, currency, buff, ingredient
    public string Id = "";
    public int Amount;
}

public static class ItemActions
{
    // Actions the client already ran, or that only mark the item: nothing to deliver.
    private static readonly HashSet<string> NoEffect = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "soulbound", "healthenergy", "cloud", "gunmount", "maneuver", "buff", "spawn"
    };

    // Reads the actions of an item. unsupported = the first action the server cannot deliver (null when all can).
    public static List<ItemActionEffect> Read(ItemDef def, out string? unsupported)
    {
        unsupported = null;
        var effects = new List<ItemActionEffect>();
        bool any = false;
        bool clientSide = false;

        if (def.IngredientId.Length > 0)
        {
            effects.Add(new ItemActionEffect { Kind = "ingredient", Id = def.IngredientId, Amount = IngredientAmount(def) });
            return effects;
        }

        foreach (ConfigNode action in def.Actions)
        {
            foreach (KeyValuePair<string, ConfigNode> pair in action.Pairs)
            {
                any = true;
                string kind = pair.Key.ToLowerInvariant();
                ConfigNode v = pair.Value;
                switch (kind)
                {
                    case "unlock":
                    {
                        string id = v["item_id"].AsString("");
                        if (id.Length == 0) { unsupported ??= "unlock without item_id"; break; }
                        effects.Add(new ItemActionEffect { Kind = "unlock", Id = id });
                        break;
                    }
                    case "variant":
                    {
                        // VariantAction: unlocks the variant of the class (playerclasses variants[].unlockItemId).
                        string planeClass = v["planeClass"].AsString("");
                        string variant = v["variantId"].AsString("");
                        PlaneClassInfo? cls = ItemCatalog.GetClass(planeClass);
                        string? unlock = null;
                        if (cls != null && cls.VariantUnlocks.TryGetValue(variant, out string? u) && !string.IsNullOrEmpty(u)) unlock = u;
                        if (unlock == null) { unsupported ??= "variant " + planeClass + "/" + variant + " unknown"; break; }
                        effects.Add(new ItemActionEffect { Kind = "unlock", Id = unlock });
                        break;
                    }
                    case "currency":
                    {
                        string id = v["currency_id"].AsString("");
                        int amount = v["inc_amount"].AsInt(0);
                        if (id.Length == 0 || amount == 0) { unsupported ??= "currency without id/amount"; break; }
                        effects.Add(new ItemActionEffect { Kind = "currency", Id = id, Amount = amount });
                        break;
                    }
                    case "playerbuff":
                    {
                        string id = v["id"].AsString("");
                        if (id.Length == 0) { unsupported ??= "buff without id"; break; }
                        effects.Add(new ItemActionEffect { Kind = "buff", Id = id });
                        break;
                    }
                    case "ingredient":
                    {
                        string id = v["ingredient_id"].AsString("");
                        if (id.Length == 0) { unsupported ??= "ingredient without id"; break; }
                        effects.Add(new ItemActionEffect { Kind = "ingredient", Id = id, Amount = Math.Max(1, v["amount"].AsInt(1)) });
                        break;
                    }
                    default:
                        if (NoEffect.Contains(kind)) clientSide = true;
                        else unsupported ??= kind;
                        break;
                }
            }
        }

        if (!any) unsupported = "the item has no action";
        else if (unsupported == null && effects.Count == 0 && !clientSide) unsupported = "no action with an effect";
        return effects;
    }

    // Amount of an ingredient pickup (the template's action), 1 when the config does not say.
    public static int IngredientAmount(ItemDef def)
    {
        foreach (ConfigNode action in def.Actions)
        {
            foreach (KeyValuePair<string, ConfigNode> pair in action.Pairs)
            {
                int amount = pair.Value["amount"].AsInt(0);
                if (amount > 0) return amount;
            }
        }
        return Math.Max(1, def.Amount);
    }

    // Delivers the effects and tells the client. Returns a short text for the log.
    public static string Apply(NetServer server, NetConnection connection, string role, List<ItemActionEffect> effects)
    {
        var texts = new List<string>();
        List<string> unlocks = effects.Where(e => e.Kind == "unlock").Select(e => e.Id).ToList();
        if (unlocks.Count > 0)
        {
            List<string> added = InventoryHandler.AddUnlocks(server, connection, unlocks);
            texts.Add(added.Count > 0 ? "unlocked " + string.Join(", ", added) : "already had " + string.Join(", ", unlocks));
        }

        List<ItemActionEffect> currencies = effects.Where(e => e.Kind == "currency").ToList();
        if (currencies.Count > 0)
        {
            Dictionary<string, int> now = InventoryStore.Change(d =>
            {
                foreach (ItemActionEffect c in currencies) InventoryStore.AddCurrency(d, c.Id, c.Amount);
                return new Dictionary<string, int>(d.Currencies);
            });
            InventoryHandler.SendCurrencies(server, connection, now);
            texts.Add(string.Join(", ", currencies.Select(c => "+" + c.Amount + " " + c.Id)));
        }

        var ingredientDeltas = new Dictionary<string, int>();
        foreach (ItemActionEffect ing in effects.Where(e => e.Kind == "ingredient"))
        {
            int added = InventoryStore.Change(d => InventoryStore.AddIngredient(d, ing.Id, ing.Amount));
            if (added != 0) ingredientDeltas[ing.Id] = (ingredientDeltas.TryGetValue(ing.Id, out int v) ? v : 0) + added;
        }
        if (ingredientDeltas.Count > 0)
        {
            InventoryHandler.Send(server, connection, m => InventoryWire.WriteClientUpdateIngredients(m, null, ingredientDeltas));
            texts.Add(string.Join(", ", ingredientDeltas.Select(p => "+" + p.Value + " " + p.Key)));
        }

        foreach (ItemActionEffect buff in effects.Where(e => e.Kind == "buff"))
        {
            string result = "";
            lock (Game.Lock) result = PilotExtras.AddBuff(server, connection, role, buff.Id);
            texts.Add("buff " + buff.Id + ": " + result);
        }
        return string.Join("; ", texts);
    }
}
