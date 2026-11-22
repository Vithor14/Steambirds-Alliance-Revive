using System.Globalization;
using Lidgren.Network;

// Entry point of the stores. Game.cs hands over here:
//  - ServerEnterStore (54295)             -> OpenStore: ClientReceiveStoreContent with the shelves of the store;
//  - ServerPurchaseInStore (54296)        -> the purchase (StorePurchase), answered as the client expects;
//  - ServerGetBuyCurrencyPackages (54297) -> empty list: the gold packages were Steam microtransactions and do not exist here;
//  - ServerRequestBuyCurrencyPackage (54298) -> refused, with a chat note;
//  - the /store chat commands for tests.
//
// Where the pieces are: StoreCatalog (the game's store config), StoreSettings (stores.json), StoreShelves (what each
// section shows), StoreRules (limits and prices), StorePurchase, StoreAccounts (what the account bought), StoreStock
// (server stock) and StoreWire (bytes). docs\stores.md describes the protocol.
public static class StoreHandler
{
    public const ushort MsgServerEnterStore = 54295;
    public const ushort MsgServerPurchaseInStore = 54296;
    public const ushort MsgServerGetBuyCurrencyPackages = 54297;
    public const ushort MsgServerRequestBuyCurrencyPackage = 54298;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static Action<string, string> log = (_, _) => { };

    // After InventoryHandler.Start (the automatic tags of the products look at the item catalog) and PilotProgression.Start
    // (the configBinary).
    public static void Start(Action<string, string> log)
    {
        StoreHandler.log = log;
        StoreAccounts.Start(log);
        StoreStock.Start(log);
        StoreCatalog.Load(log);
        StoreSettingsFile.Start(log);
    }

    public static bool IsStoreRpc(ushort id)
    {
        return id == MsgServerPurchaseInStore || id == MsgServerGetBuyCurrencyPackages || id == MsgServerRequestBuyCurrencyPackage;
    }

    // ------------------------------------------------------------------ opening a store

    // ServerEnterStore(string storeId). tag: "[nexus]".
    public static void OpenStore(NetServer server, NetConnection connection, string tag, string storeId)
    {
        List<Shelf> shelves = StoreShelves.Build(storeId);
        NetOutgoingMessage message = server.CreateMessage();
        StoreWire.WriteStoreContent(message, storeId, shelves);
        server.SendMessage(message, connection, NetDeliveryMethod.ReliableOrdered, 0);

        int products = shelves.Sum(s => s.Items.Count);
        string state = !StoreCatalog.Loaded ? " (the game's store config was not read)" :
            StoreCatalog.Store(storeId) == null ? " (unknown store)" : "";
        log("STORE", tag + " store '" + storeId + "' opened -> ClientReceiveStoreContent sent (" + message.LengthBytes + " bytes, " +
            shelves.Count + " shelf(ves), " + products + " product(s))" + state);
    }

    // ------------------------------------------------------------------ RPCs

    // The message is positioned after the message id. tag: "[nexus]".
    public static void HandleRpc(NetServer server, NetIncomingMessage msg, ushort id, string tag)
    {
        NetConnection connection = msg.SenderConnection;
        string role = tag.Trim('[', ']');

        if (id == MsgServerPurchaseInStore)
        {
            string storeId = msg.ReadString() ?? "";
            string sectionId = msg.ReadString() ?? "";
            string productId = msg.ReadString() ?? "";
            string priceId = msg.ReadString() ?? "";
            string postAction = msg.ReadString() ?? "";
            ushort rpc = msg.ReadUInt16();

            PurchaseResult result = StorePurchase.Buy(server, connection, role, storeId, sectionId, productId, priceId, postAction);
            if (result.Ok)
            {
                // The shelves first (stock and limits changed), then the answer: the window reacts to the answer.
                StorePurchase.SendStoreContent(server, connection, storeId, StoreShelves.Build(storeId));
                NetOutgoingMessage ok = server.CreateMessage();
                StoreWire.WriteRpcString(ok, rpc, productId);
                server.SendMessage(ok, connection, NetDeliveryMethod.ReliableOrdered, 0);
                log("STORE", tag + " bought " + productId + " in " + storeId + "/" + sectionId + " (" +
                    (string.IsNullOrEmpty(priceId) ? "first price" : "price '" + priceId + "'") + ", " +
                    (string.IsNullOrEmpty(postAction) ? "no action" : "'" + postAction + "'") + "): " + result.Summary);
                return;
            }

            string key = StoreRules.FaultKey(result.Refusal);
            NetOutgoingMessage fail = server.CreateMessage();
            InventoryWire.WriteRpcFault(fail, rpc, key);
            server.SendMessage(fail, connection, NetDeliveryMethod.ReliableOrdered, 0);
            // The game's text says what went wrong in general; this line says why here.
            WorldObjects.SendChat(server, connection, "Store: " + result.Note + ".");
            log("STORE", tag + " purchase of " + productId + " in " + storeId + "/" + sectionId + " refused -> '" + key + "': " + result.Note);
            return;
        }

        if (id == MsgServerGetBuyCurrencyPackages)
        {
            string payment = msg.ReadString() ?? "";
            ushort rpc = msg.ReadUInt16();
            NetOutgoingMessage empty = server.CreateMessage();
            StoreWire.WriteRpcEmptyList(empty, rpc);
            server.SendMessage(empty, connection, NetDeliveryMethod.ReliableOrdered, 0);
            log("STORE", tag + " currency packages ('" + payment + "') -> empty list (the Steam packages do not exist here; " +
                "give gold with /currency gold N, items.json startingCurrencies or the launcher account editor)");
            return;
        }

        if (id == MsgServerRequestBuyCurrencyPackage)
        {
            string package = msg.ReadString() ?? "";
            msg.ReadString();                         // extra (the Steam app id)
            ushort rpc = msg.ReadUInt16();
            NetOutgoingMessage fail = server.CreateMessage();
            InventoryWire.WriteRpcFault(fail, rpc, "purchase_error_payment_failed");
            server.SendMessage(fail, connection, NetDeliveryMethod.ReliableOrdered, 0);
            WorldObjects.SendChat(server, connection, "Currency packages are not sold on this server. Ask the host for gold (/currency gold N).");
            log("STORE", tag + " currency package '" + package + "' refused (no Steam payments)");
        }
    }

    // ------------------------------------------------------------------ chat commands

    // /store (or /loja, /shop): test commands. role: "nexus", "tutorial"...
    public static bool Command(NetServer server, NetConnection? connection, string role, List<string> pieces, double now, Action<string, string> logTo)
    {
        if (connection == null || pieces.Count == 0) return false;
        string command = pieces[0].Trim().ToLowerInvariant();
        if (!command.StartsWith("/")) command = "/" + command;
        if (command != "/store" && command != "/loja" && command != "/shop") return false;

        string sub = pieces.Count > 1 ? pieces[1].Trim().ToLowerInvariant() : "";
        string? arg = pieces.Count > 2 ? pieces[2].Trim() : null;
        string? arg2 = pieces.Count > 3 ? pieces[3].Trim() : null;
        string tag = "[" + role + "]";

        switch (sub)
        {
            case "info":
                Chat(server, connection, InfoText());
                return true;

            case "list":
            {
                string storeId = string.IsNullOrEmpty(arg) ? "store_general" : arg;
                List<Shelf> shelves = StoreShelves.Build(storeId);
                if (shelves.Count == 0)
                {
                    Chat(server, connection, "Nothing on the shelves of '" + storeId + "' (stores: " +
                        string.Join(", ", StoreCatalog.Stores.Select(s => s.Id)) + ").");
                    return true;
                }
                foreach (Shelf shelf in shelves)
                {
                    string line = storeId + "/" + shelf.SectionId + " (" + shelf.Items.Count + "): " + string.Join(", ",
                        shelf.Items.Take(6).Select(i => i.Product.Id + " " + StorePurchase.PriceText(i.Product, i.Discount)));
                    if (shelf.Items.Count > 6) line += ", ... (+" + (shelf.Items.Count - 6) + ")";
                    Chat(server, connection, line);
                }
                return true;
            }

            case "buy":
            {
                if (string.IsNullOrEmpty(arg))
                {
                    Chat(server, connection, "Use /store buy PRODUCT [PRICE]. E.g.: /store buy hangar_slot (PRICE: normal or iron; /store list shows the ids).");
                    return true;
                }
                foreach (StoreDef store in StoreCatalog.Stores)
                {
                    foreach (Shelf shelf in StoreShelves.Build(store.Id))
                    {
                        if (shelf.Items.All(i => i.Product.Id != arg)) continue;
                        PurchaseResult result = StorePurchase.Buy(server, connection, role, store.Id, shelf.SectionId, arg, arg2 ?? "", "nothing");
                        if (result.Ok)
                        {
                            StorePurchase.SendStoreContent(server, connection, store.Id, StoreShelves.Build(store.Id));
                            Chat(server, connection, "Bought " + result.ProductName + " (" + result.Summary + ").");
                            logTo("STORE", tag + " /store buy " + arg + " in " + store.Id + "/" + shelf.SectionId + ": " + result.Summary);
                        }
                        else
                        {
                            Chat(server, connection, "Not bought: " + result.Note + ".");
                            logTo("STORE", tag + " /store buy " + arg + " refused (" + result.Refusal + "): " + result.Note);
                        }
                        return true;
                    }
                }
                Chat(server, connection, "'" + arg + "' is not on any shelf right now (/store list [store]).");
                return true;
            }

            case "reset":
                StoreAccounts.Reset();
                StorePurchase.SendSlots(server, connection);
                Chat(server, connection, "What you bought in the stores was forgotten (slots go back to the base value). Items and currency stay. Log in again to refresh the prices.");
                logTo("STORE", tag + " /store reset");
                return true;

            case "restock":
                StoreStock.Reset();
                Chat(server, connection, "The server stock of every limited product was refilled.");
                logTo("STORE", tag + " /store restock");
                return true;

            default:
                Chat(server, connection, "Stores: /store info, /store list [store], /store buy PRODUCT [PRICE], /store reset (forget your purchases), " +
                    "/store restock (refill the stock). Gold: /currency gold N. Settings in stores.json.");
                return true;
        }
    }

    private static string InfoText()
    {
        StoreSettings settings = StoreSettingsFile.Current;
        ItemSettings items = ItemSettingsFile.Current;
        int hangarBought = StoreAccounts.Count(StoreAccounts.HangarSlot);
        int loadoutBought = StoreAccounts.Count(StoreAccounts.SkillLoadoutSlot);
        int storageBought = StoreAccounts.Count(StoreAccounts.StorageSlot);
        int cargoBought = StoreAccounts.Count(StoreAccounts.InventorySlot);
        Dictionary<string, int> currencies = InventoryStore.Read(d => new Dictionary<string, int>(d.Currencies));
        return "Stores: purchases " + (settings.PurchasesEnabled ? "on" : "OFF") + ", prices " + (settings.Free ? "free" : "in gold/iron" +
            (settings.DiscountPercent > 0 ? " (-" + settings.DiscountPercent + "%)" : "")) +
            ". Hangar " + PlayerData.HangarSlots + " (bought " + hangarBought + "), loadouts " + PilotProgression.SkillLoadoutSlots() +
            " (bought " + loadoutBought + "), storage +" + (items.ExtraStorageSlots + storageBought) + " (bought " + storageBought +
            "), cargo +" + (items.ExtraCargoSlots + cargoBought) + " (bought " + cargoBought + "). You have: " +
            (currencies.Count == 0 ? "no currency" : string.Join(", ", currencies.Select(c => c.Key + " " + c.Value.ToString(Inv)))) + ".";
    }

    private static void Chat(NetServer server, NetConnection connection, string text) => WorldObjects.SendChat(server, connection, text);
}
