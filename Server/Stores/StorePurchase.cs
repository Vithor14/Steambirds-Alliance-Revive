using System.Globalization;
using Lidgren.Network;

// ServerPurchaseInStore: validates the purchase, takes the currency from the account, delivers the product and tells the
// client. The account is the one of the connection that sent the message (Accounts scope of Program.cs).
//
// What a product can give (StoreProduct):
//  - items          -> inventory items, in the plane cargo or the storage (the player's choice, postPurchaseAction);
//  - unlocks        -> unlock items of the pilot (emotes, plane classes);
//  - account_items  -> the counters of the account: hangar, storage, cargo and skill loadout slots, and the markers
//                      of the beginner bundle (StoreAccounts). The slots of the account are the host's base value plus these.
//
// postPurchaseAction: "nothing" (plane cargo, then storage), "storage" and "use_and_storage" (storage first),
// "use_and_inventory" (cargo first) and "use_instantly". Using the item on the spot is only done when the server can
// deliver its effects (an unlock or a plane variant, see ItemActions). Dyes and trails ("deco" actions) run only on the
// client: with use_and_inventory / use_and_storage the item is delivered and then ClientConsumedItemOnPickup makes the
// client repaint the plane (the item stays, dyes are unlimited). Other items with no server side go to the inventory.
public sealed class PurchaseResult
{
    public StoreRefusal Refusal;
    public string Note = "";       // short reason for the log and the chat
    public string ProductName = "";
    public string Summary = "";    // what was charged and delivered, for the log
    public bool Ok => Refusal == StoreRefusal.None;
}

public static class StorePurchase
{
    private sealed class Placement
    {
        public string CharacterId = "";
        public StoredItem Item = null!;
        public InvPos Pos;
        public int Audit;
    }

    private sealed class Charged
    {
        public bool Ok;
        public StoreRefusal Refusal;
        public string Note = "";
        public List<Placement> Placements = new List<Placement>();
        public Dictionary<string, int> Currencies = new Dictionary<string, int>();
    }

    private static PurchaseResult Refuse(StoreRefusal refusal, string note, string productName = "")
    {
        return new PurchaseResult { Refusal = refusal, Note = note, ProductName = productName };
    }

    // tag: "[nexus]" and the like, for the log. role: "nexus" (for the buffs of the item actions).
    public static PurchaseResult Buy(NetServer server, NetConnection connection, string role, string storeId, string sectionId,
        string productId, string priceId, string postAction)
    {
        StoreSettings settings = StoreSettingsFile.Current;
        if (!settings.PurchasesEnabled) return Refuse(StoreRefusal.Unknown, "purchases are turned off on this server (stores.json: purchasesEnabled)");
        if (!StoreCatalog.Loaded) return Refuse(StoreRefusal.Unknown, "the game's store config was not read");

        StoreProductDef? product = StoreCatalog.Product(productId);
        if (product == null) return Refuse(StoreRefusal.Unknown, "no product '" + productId + "' in the game's store config");
        string name = product.Name;

        StoreShopRules? shop = settings.ShopRules(storeId);
        if (StoreCatalog.Store(storeId) == null) return Refuse(StoreRefusal.Unknown, "no store '" + storeId + "'", name);
        if (shop != null && !shop.Enabled) return Refuse(StoreRefusal.Unknown, "the store " + storeId + " is turned off (stores.json)", name);

        // Same reasons that take the product off the shelf: sold out, the host's limit, the top of the hangar / loadouts.
        StoreRefusal blocker = StoreRules.ShelfBlocker(product, settings);
        if (blocker == StoreRefusal.SoldOut) return Refuse(StoreRefusal.Unknown, "sold out (stores.json: stock)", name);
        if (blocker != StoreRefusal.None) return Refuse(blocker, "limit reached (stores.json: maxPerAccount, or the most hangar slots / skill loadouts)", name);

        ShelfItem? onShelf = StoreShelves.Find(storeId, sectionId, productId);
        if (onShelf == null) return Refuse(StoreRefusal.Unknown, "'" + productId + "' is not on the shelf " + storeId + "/" + sectionId, name);

        StoreRefusal limit = StoreRules.GameLimit(product);
        if (limit != StoreRefusal.None) return Refuse(limit, "the account already has the most of this product the game allows", name);

        StorePriceTag? price = string.IsNullOrEmpty(priceId)
            ? product.PriceTags.FirstOrDefault()
            : product.PriceTags.FirstOrDefault(t => t.Id == priceId);
        if (price == null) return Refuse(StoreRefusal.Unknown, "the product has no price '" + priceId + "'", name);

        int amount = Math.Max(0, StoreRules.PriceAmount(price, onShelf.Discount));
        string currency = price.Price.Currency;

        // What goes where.
        string action = string.IsNullOrEmpty(postAction) ? "nothing" : postAction;
        bool toStorage = action == "storage" || action == "use_and_storage";
        var itemsToPlace = new List<string>();
        var effects = new List<ItemActionEffect>();
        var notes = new List<string>();
        var applyOnClient = new HashSet<string>(StringComparer.Ordinal);
        foreach (string defId in StoreRules.ItemsOf(product))
        {
            ItemDef? def = ItemCatalog.Get(defId);
            if (def == null) return Refuse(StoreRefusal.Unknown, "item '" + defId + "' does not exist in this game version", name);
            if (action == "use_instantly")
            {
                List<ItemActionEffect> read = ItemActions.Read(def, out string? unsupported);
                // Dyes need the client to repaint the plane and keys send the player away: they stay in the inventory for the player.
                bool needsPlayer = read.Any(e => e.Kind == "deco" || e.Kind == "dungeonkey");
                if (unsupported == null && read.Count > 0 && !needsPlayer)
                {
                    effects.AddRange(read);
                    continue;
                }
                notes.Add(defId + (needsPlayer ? " is used by the player" : " cannot be used by the server yet (" + unsupported + ")") +
                          ": it went to the inventory");
            }
            else if (action.StartsWith("use_", StringComparison.Ordinal))
            {
                // Dyes and trails: the item is delivered, then the client is told to perform its actions (repaint the plane).
                if (ItemActions.IsClientSideUse(def)) applyOnClient.Add(defId);
                else notes.Add(defId + ": the server does not use it for you yet, it is in the " + (toStorage ? "storage" : "inventory"));
            }
            itemsToPlace.Add(defId);
        }

        string? planeId = Game.GetPlaneCharacterId(connection);
        Accounts.SavedPlane? plane = planeId == null ? null : Accounts.GetPlane(planeId);
        if (planeId != null && plane == null) planeId = null;

        // The currency and the items change together under the inventory lock: either everything happens or nothing.
        Charged charged = InventoryStore.Change(d =>
        {
            var result = new Charged();
            if (amount > 0 && InventoryStore.Currency(d, currency) < amount)
            {
                result.Refusal = StoreRefusal.NoCurrency;
                result.Note = "needs " + amount + " " + currency + ", the account has " + InventoryStore.Currency(d, currency);
                return result;
            }

            InvAccess? planeAccess = null;
            if (planeId != null)
            {
                if (!d.Planes.ContainsKey(planeId)) InventoryStore.EnsurePlane(d, planeId, plane!.Class);
                planeAccess = InventoryStore.Resolve(d, planeId);
            }
            InvAccess storage = InventoryStore.Resolve(d, d.StorageCharacterId)!;

            int free = (planeAccess?.Cargo.Count(i => i == null) ?? 0) + storage.Cargo.Count(i => i == null);
            if (itemsToPlace.Count > free)
            {
                result.Refusal = StoreRefusal.NoInventorySpace;
                result.Note = "needs " + itemsToPlace.Count + " free slot(s) in the cargo or the storage, there are " + free;
                return result;
            }

            if (amount > 0) InventoryStore.AddCurrency(d, currency, -amount);
            foreach (string defId in itemsToPlace)
            {
                StoredItem item = InventoryStore.NewItem(defId);
                InvAccess first = toStorage || planeAccess == null ? storage : planeAccess;
                InvAccess second = ReferenceEquals(first, storage) ? (planeAccess ?? storage) : storage;
                InvAccess target = InventoryStore.FirstFreeCargo(first).Valid ? first : second;
                InvPos pos = InventoryStore.FirstFreeCargo(target);
                if (!pos.Valid) continue;   // cannot happen after the check above
                InventoryStore.Set(target, pos, item);
                result.Placements.Add(new Placement { CharacterId = target.CharacterId, Item = item, Pos = pos, Audit = InventoryStore.NextAudit(d) });
            }
            result.Currencies = new Dictionary<string, int>(d.Currencies);
            result.Ok = true;
            return result;
        });
        if (!charged.Ok) return Refuse(charged.Refusal, charged.Note, name);

        // Paid and delivered in the inventory: the account side.
        string summary = (amount > 0 ? amount + " " + currency : "free") +
                         (itemsToPlace.Count > 0 ? ", " + string.Join(" + ", itemsToPlace) : "") +
                         (product.AccountItems.Count > 0 ? ", account items " + string.Join(" ", product.AccountItems) : "") +
                         (product.Unlocks.Count > 0 ? ", unlock " + string.Join(" ", product.Unlocks) : "");
        StoreAccounts.Record(product.Id, product.AccountItems, product.Id + " (" + storeId + "/" + sectionId + ") " + summary);
        StoreStock.Sold(product.Id);

        // The client.
        if (amount > 0) InventoryHandler.SendCurrencies(server, connection, charged.Currencies);
        foreach (Placement p in charged.Placements)
            InventoryHandler.Send(server, connection, m => InventoryWire.WriteClientInvAdd(m, p.CharacterId, p.Item, p.Pos, 0f, 0f, p.Audit));
        // "Use and keep": the item is already in the inventory or the storage; now the client performs its actions on the plane.
        foreach (Placement p in charged.Placements)
        {
            if (!applyOnClient.Contains(p.Item.DefId)) continue;
            StoredItem used = p.Item;
            InventoryHandler.Send(server, connection, m => InventoryWire.WriteClientConsumedItemOnPickup(m, used));
            ItemDef? usedDef = ItemCatalog.Get(used.DefId);
            if (usedDef != null) ItemActions.Apply(server, connection, role, ItemActions.Read(usedDef, out _));   // the server keeps the paint too
            notes.Add(used.DefId + " applied to the plane");
        }
        if (product.Unlocks.Count > 0) InventoryHandler.AddUnlocks(server, connection, product.Unlocks);
        if (effects.Count > 0) notes.Add(ItemActions.Apply(server, connection, role, effects));
        if (product.AccountItems.Count > 0 || product.Unlocks.Count > 0)
        {
            byte[] account = Accounts.AccountId;
            InventoryHandler.Send(server, connection, m => StoreWire.WriteGiveProduct(m, account, planeId ?? "", storeId, sectionId, productId));
            // The client recalculates its slots from ITS base values when it gets the notification above (hangar 2, loadouts 1),
            // so the real counts (host base + bought) always go right after it, whatever the product was.
            SendSlots(server, connection);
        }

        string detail = summary + (notes.Count > 0 ? " [" + string.Join("; ", notes.Where(n => n.Length > 0)) + "]" : "");
        return new PurchaseResult { Refusal = StoreRefusal.None, ProductName = name, Summary = detail };
    }

    // The slot counts of the account (host base + bought) go to the client; the hangar and Pilot School windows refresh.
    public static void SendSlots(NetServer server, NetConnection connection)
    {
        ItemSettings items = ItemSettingsFile.Current;
        int hangar = PlayerData.HangarSlots;
        int storage = items.ExtraStorageSlots + StoreAccounts.Count(StoreAccounts.StorageSlot);
        int cargo = items.ExtraCargoSlots + StoreAccounts.Count(StoreAccounts.InventorySlot);
        int loadouts = PilotProgression.SkillLoadoutSlots();
        InventoryHandler.Send(server, connection, m => StoreWire.WriteSlots(m, hangar, storage, cargo, loadouts));
    }

    // Shelves of a store, as the client reads them (also after a purchase: stock and limits change).
    public static void SendStoreContent(NetServer server, NetConnection connection, string storeId, List<Shelf> shelves)
    {
        InventoryHandler.Send(server, connection, m => StoreWire.WriteStoreContent(m, storeId, shelves));
    }

    // "750 gold" or "750 gold / 4500 iron": the price of a product, for the chat commands.
    public static string PriceText(StoreProductDef product, int discount)
    {
        return string.Join(" / ", product.PriceTags.Select(t =>
            StoreRules.PriceAmount(t, discount).ToString(CultureInfo.InvariantCulture) + " " + t.Price.Currency));
    }
}
