using System.Globalization;
using Lidgren.Network;

// Client -> server inventory messages and the item chat commands. Program.cs calls Handle before
// Game.Handle; Game calls Command for the "/" commands.
//
// RPCs (Inventory.ClientToServerTransport): the client locks the slots, waits for the answer and only then
// changes its own copy (swap, remove, accumulate, consume, sort, favorite). The int they return is an audit id:
// -1 (AuditLog.INVALID_ID) means "refused" and nothing changes on the client. Afterwards the client sends
// ServerAuditInvAction with that id.
//
// Messages: ServerRequestAddInventoryItem (picking a bag up), ServerReportItemPickup (report only),
// ServerReforgeItemFromIngredients (Workshop), ServerAuditInvAction (report only) and ServerRequestKill for
// loot bags.
public static partial class InventoryHandler
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const ushort MsgServerReportItemPickup = 4422;         // PeerId, InventoryItem, Vector3, int bag, float, float
    private const ushort MsgServerReforgeItemFromIngredients = 12157;
    private const ushort MsgServerInvSwapItems = 17407;            // RPC int: string, pos, string, pos
    private const ushort MsgServerRequestAddInventoryItem = 21818; // PeerId, InventoryId, Vector3, int bag
    private const ushort MsgServerRequestKill = 25386;             // int shortId, int reason, float x, float y
    private const ushort MsgServerInvRemoveItem = 60901;           // RPC int: string, pos, byte dest
    private const ushort MsgServerInvConsumeItem = 60902;          // RPC ConsumeItemResult: string, pos
    private const ushort MsgServerInvApplySort = 60903;            // RPC bool: string, int sortType
    private const ushort MsgServerInvAccumulateItem = 60904;       // RPC int: string, pos
    private const ushort MsgServerInvFavoriteItem = 60905;         // RPC int: string, pos, bool
    private const ushort MsgServerAuditInvAction = 61999;          // int, float, float, ALInvAction

    private const int Refused = -1;                                // AuditLog.INVALID_ID
    private const byte DestinationDrop = 0;                        // GameServer.ItemDestination.Drop
    private const byte DestinationTrash = 1;                       // GameServer.ItemDestination.Trash
    private const int SortByType = 1;                              // InventorySortType.Type
    private const int SortByTier = 2;                              // InventorySortType.Tier

    private static Action<string, string> log = (_, _) => { };

    // Server log (same format as the rest of the server).
    public static void Log(string tag, string text) => log(tag, text);

    private static readonly Dictionary<string, int> counters = new Dictionary<string, int>();

    // ------------------------------------------------------------------ start

    // Items, inventories and loot tables. Called by Program.cs after PilotProgression.Start (configBinary).
    // Nothing here depends on an account: the inventories are per account (InventoryStore).
    public static void Start(Action<string, string> log)
    {
        InventoryHandler.log = log;
        ItemSettingsFile.Start(log);
        ItemCatalog.Load(log);
        InventoryStore.Start(log);
        LootTables.Start(log);
        if (ItemSettingsFile.Current.ExportItemCatalog) ItemCatalogExport.ExportIfChanged(log);
        // The inventory of each account (and of its planes) is read at its first use: InventoryStore.Current.
    }

    // ------------------------------------------------------------------ hooks called by the rest of the server

    // Before the ClientWelcomeMessage: items whose time ran out (decaysInSeconds) leave the inventories.
    public static void PrepareForWelcome(string role)
    {
        int gone = InventoryStore.Change(d => InventoryStore.PurgeDecayed(d));
        if (gone > 0) log("INVENTORY", "[" + role + "] " + gone + " item(s) past their deadline left the inventory");
    }

    // After the ClientWelcomeMessage: the client gets the ingredient wallet (it is not part of the PlayerData).
    public static void PlayerIdentified(NetServer server, NetConnection connection, string role)
    {
        Dictionary<string, int> ingredients = InventoryStore.Read(d => new Dictionary<string, int>(d.Ingredients));
        Send(server, connection, m => InventoryWire.WriteClientUpdateIngredients(m, ingredients, null));
    }

    // ServerRequestCreateCharacter: the new plane gets its starting items. Planes that left the account (a full
    // hangar drops the oldest one) take their inventory with them.
    public static void PlaneCreated(string characterId, string planeClass)
    {
        HashSet<string> planes = new HashSet<string>(Accounts.Planes().Select(a => a.CharacterId), StringComparer.Ordinal);
        InventoryStore.Change(d =>
        {
            foreach (string gone in d.Planes.Keys.Where(k => !planes.Contains(k) && k != characterId).ToList()) d.Planes.Remove(gone);
            return InventoryStore.EnsurePlane(d, characterId, planeClass);
        });
    }

    // Hangar plane change: with the shared cargo pod, the client's copy of the new plane's cargo may be old.
    public static void PlaneChanged(NetServer server, NetConnection connection, string characterId)
    {
        if (!ItemSettingsFile.Current.SharedCargoForAllPlanes) return;
        SendInventory(server, connection, characterId);
    }

    // The plane was destroyed (after PilotProgression.PlaneDestroyed). With permadeath the plane already left the
    // account: its equipped items and cargo are lost, except what items.json keeps or salvages to the storage.
    public static void PlaneDestroyed(NetServer server, NetConnection connection, string role, string? characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return;
        // Account of the player of this connection (also when called outside a message of the player).
        using Accounts.Scope accountScope = Accounts.UseFor(connection);
        bool planeGone = Accounts.GetPlane(characterId) == null;
        if (!planeGone) return; // permadeath off: the plane comes back with everything it had
        ItemSettings settings = ItemSettingsFile.Current;
        List<string> remainingPlanes = Accounts.Planes().Select(a => a.CharacterId).ToList();

        string summary = InventoryStore.Change(d =>
        {
            InvAccess? plane = InventoryStore.Resolve(d, characterId);
            if (plane == null) return "the plane had no inventory";
            InvAccess storage = InventoryStore.Resolve(d, d.StorageCharacterId)!;
            var notes = new List<string>();

            // DeathLootConfig.salvageRandomEquipment: random equipped items go to the storage, worn out.
            // Tier 0 items are never salvaged (they are not candidates, so they do not use up a salvage).
            var equipped = plane.Inv.Equipped.Concat(plane.Inv.Alternative).Select(p => p.Value).ToList();
            var candidates = equipped.Where(i => !ItemCatalog.IsTierZero(i)).ToList();
            var salvaged = new List<StoredItem>();
            int toSalvage = settings.KeepEquippedOnDeath ? candidates.Count : Math.Min(settings.DeathSalvageRandomEquipment, candidates.Count);
            foreach (StoredItem item in candidates.OrderBy(_ => Random.Shared.Next()).Take(toSalvage))
            {
                if (!settings.KeepEquippedOnDeath && settings.DegradeSalvagedItems)
                {
                    ItemDef? def = ItemCatalog.Get(item.DefId);
                    if (def != null && ItemCatalog.CanBeDegraded(def))
                    {
                        if (item.Condition > 0) item.Supercharged.Clear();
                        item.Condition = ItemCatalog.DegradedCondition(item.Condition);
                    }
                    item.Salvaged = true;
                }
                salvaged.Add(item);
            }
            int lostEquipped = equipped.Count - salvaged.Count;

            // Cargo: lost with the plane, unless keepCargoOnDeath. The shared pod belongs to every plane.
            var cargo = plane.Cargo.Where(i => i != null).Select(i => i!).ToList();
            int lostCargo = 0;
            if (!settings.KeepCargoOnDeath)
            {
                lostCargo = cargo.Count;
                for (int i = 0; i < plane.Cargo.Count; i++) plane.Cargo[i] = null;
            }
            else if (!settings.SharedCargoForAllPlanes)
            {
                // Own cargo kept with the plane goes to the storage, except tier 0 items (discarded).
                foreach (StoredItem item in cargo)
                {
                    if (ItemCatalog.IsTierZero(item)) lostCargo++;
                    else salvaged.Add(item);
                }
                for (int i = 0; i < plane.Cargo.Count; i++) plane.Cargo[i] = null;
            }

            int overflow = 0;
            foreach (StoredItem item in salvaged)
            {
                InvPos free = InventoryStore.FirstFreeCargo(storage);
                if (free.Valid) storage.Cargo[free.Index] = item;
                else overflow++;
            }

            d.Planes.Remove(characterId);
            if (salvaged.Count > 0) notes.Add((salvaged.Count - overflow) + " item(s) to storage (" + string.Join(", ", salvaged.Select(s => s.ToString())) + ")");
            if (overflow > 0) notes.Add(overflow + " did not fit in storage and were lost");
            if (lostEquipped > 0) notes.Add(lostEquipped + " equipped item(s) lost");
            if (lostCargo > 0) notes.Add(lostCargo + " cargo item(s) lost");
            return notes.Count == 0 ? "nothing to save" : string.Join(", ", notes);
        });

        log("INVENTORY", "[" + role + "] plane fell " + characterId + ": " + summary);
        SendInventory(server, connection, InventoryStore.StorageCharacterId);
        if (!settings.KeepCargoOnDeath && settings.SharedCargoForAllPlanes)
        {
            foreach (string other in remainingPlanes) SendInventory(server, connection, other);
        }
    }

    // What the loot draw needs to know about the player.
    public static LootContext BuildLootContext(NetConnection connection)
    {
        var ctx = new LootContext { Difficulty = ItemSettingsFile.Current.DungeonDifficulty, IsAdmin = true };
        Accounts.SavedPlane? plane = Accounts.GetPlane(Game.GetPlaneCharacterId(connection));
        if (plane != null)
        {
            ctx.PlaneClass = plane.Class;
            ctx.PlaneLevel = plane.Level;
        }
        PilotSave pilot = Accounts.Pilot();
        ctx.PilotLevel = pilot.Level;
        foreach (BuffSave buff in pilot.Buffs) ctx.Buffs.Add(buff.Id);
        return ctx;
    }

    // InstantLoot: currencies, ingredients, plane and pilot XP, unlocks and buffs, delivered right away.
    public static void DeliverInstant(NetServer server, NetConnection connection, string role, InstantLootDef instant, string reason, Action<string, string> logTo)
    {
        if (instant.CurrencyAmount != 0 && instant.CurrencyId.Length > 0)
        {
            Dictionary<string, int> currencies = InventoryStore.Change(d =>
            {
                InventoryStore.AddCurrency(d, instant.CurrencyId, instant.CurrencyAmount);
                return new Dictionary<string, int>(d.Currencies);
            });
            SendCurrencies(server, connection, currencies);
        }
        if (instant.IngredientAmount != 0 && instant.IngredientId.Length > 0)
        {
            int added = InventoryStore.Change(d => InventoryStore.AddIngredient(d, instant.IngredientId, instant.IngredientAmount));
            if (added != 0)
                Send(server, connection, m => InventoryWire.WriteClientUpdateIngredients(m, null, new Dictionary<string, int> { [instant.IngredientId] = added }));
        }
        if (instant.PlaneXp > 0)
        {
            string? characterId = Game.GetPlaneCharacterId(connection);
            if (characterId != null) PilotProgression.AwardPlaneXp(server, connection, role, characterId, instant.PlaneXp, false, reason);
        }
        if (instant.PilotXp > 0)
        {
            Accounts.Change(d => PilotProgression.AddPilotXp(d, instant.PilotXp));
            PilotProgression.SendPilotUpdate(server, connection);
        }
        if (instant.Unlocks.Count > 0) AddUnlocks(server, connection, instant.Unlocks);
        if (instant.Buff.Length > 0)
        {
            lock (Game.Lock) PilotExtras.AddBuff(server, connection, role, instant.Buff);
        }
        logTo("LOOT", "[" + role + "] " + reason + ": " + instant);
    }

    // ------------------------------------------------------------------ messages

    // Returns true when the message is an inventory message (handled here).
    public static bool Handle(NetServer server, NetIncomingMessage msg, ushort id, string role, double now, Action<string, string> logTo)
    {
        NetConnection connection = msg.SenderConnection;
        string tag = "[" + role + "]";
        switch (id)
        {
            case MsgServerInvSwapItems:
            {
                string sourceId = msg.ReadString() ?? "";
                InvPos sourcePos = InventoryWire.ReadPosition(msg);
                string destId = msg.ReadString() ?? "";
                InvPos destPos = InventoryWire.ReadPosition(msg);
                ushort rpc = msg.ReadUInt16();
                Swap(server, connection, tag, rpc, sourceId, sourcePos, destId, destPos);
                return true;
            }
            case MsgServerInvRemoveItem:
            {
                string characterId = msg.ReadString() ?? "";
                InvPos pos = InventoryWire.ReadPosition(msg);
                byte destination = msg.ReadByte();
                ushort rpc = msg.ReadUInt16();
                Remove(server, connection, tag, rpc, characterId, pos, destination, now);
                return true;
            }
            case MsgServerInvConsumeItem:
            {
                string characterId = msg.ReadString() ?? "";
                InvPos pos = InventoryWire.ReadPosition(msg);
                ushort rpc = msg.ReadUInt16();
                Consume(server, connection, role, rpc, characterId, pos);
                return true;
            }
            case MsgServerInvApplySort:
            {
                string characterId = msg.ReadString() ?? "";
                int sortType = msg.ReadInt32();
                ushort rpc = msg.ReadUInt16();
                Sort(server, connection, tag, rpc, characterId, sortType);
                return true;
            }
            case MsgServerInvAccumulateItem:
            {
                string characterId = msg.ReadString() ?? "";
                InvPos pos = InventoryWire.ReadPosition(msg);
                ushort rpc = msg.ReadUInt16();
                Accumulate(server, connection, tag, rpc, characterId, pos);
                return true;
            }
            case MsgServerInvFavoriteItem:
            {
                string characterId = msg.ReadString() ?? "";
                InvPos pos = InventoryWire.ReadPosition(msg);
                bool favorite = msg.ReadBoolean();
                ushort rpc = msg.ReadUInt16();
                Favorite(server, connection, tag, rpc, characterId, pos, favorite);
                return true;
            }
            case MsgServerRequestAddInventoryItem:
            {
                msg.ReadInt64();                       // PeerId
                string itemId = msg.ReadString() ?? "";
                float x = msg.ReadSingle();
                float y = msg.ReadSingle();
                msg.ReadSingle();                      // z
                int bag = msg.ReadInt32();
                Pickup(server, connection, role, itemId, bag, x, y, now);
                return true;
            }
            case MsgServerReportItemPickup:
            {
                msg.ReadInt64();                       // PeerId
                StoredItem? item = InventoryWire.ReadItem(msg);
                int count = Count(role + " pickup");
                if (count <= 5 || count % 50 == 0)
                    logTo("LOOT", tag + " ServerReportItemPickup: " + (item?.DefId ?? "(empty)") + " x" + count);
                return true;
            }
            case MsgServerRequestKill:
            {
                long start = msg.Position;
                int shortId = msg.ReadInt32();
                if (LootDrops.HandleKillRequest(server, connection, shortId, now, logTo)) return true;
                msg.Position = start;                  // not a bag: Game reads it
                return false;
            }
            case MsgServerAuditInvAction:
            {
                int auditId = msg.ReadInt32();
                int count = Count(role + " audit");
                if (count <= 3 || count % 100 == 0) logTo("INVENTORY", tag + " ServerAuditInvAction " + auditId + " x" + count);
                // The client says what it did with the id the server gave: Admin\InventoryAudit.cs compares and counts the failures.
                InventoryAudit.Reported(server, connection, role, auditId, msg, logTo);
                return true;
            }
            case MsgServerReforgeItemFromIngredients:
            {
                string characterId = msg.ReadString() ?? "";
                InvPos pos = InventoryWire.ReadPosition(msg);
                var ids = new List<string>();
                uint idsPlusOne = msg.ReadVariableUInt32();
                for (uint i = 0; i + 1 < idsPlusOne && i < 64; i++) ids.Add(msg.ReadString() ?? "");
                var positions = new List<InvPos>();
                uint positionsPlusOne = msg.ReadVariableUInt32();
                for (uint i = 0; i + 1 < positionsPlusOne && i < 64; i++) positions.Add(InventoryWire.ReadPosition(msg));
                Workshop.Reforge(server, connection, role, characterId, pos, ids, positions);
                return true;
            }
        }
        return false;
    }

    private static int Count(string key)
    {
        lock (counters)
        {
            counters.TryGetValue(key, out int value);
            value++;
            counters[key] = value;
            return value;
        }
    }

    // ------------------------------------------------------------------ swap (move, equip, unequip, storage)

}
