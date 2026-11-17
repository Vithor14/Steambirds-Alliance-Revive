using Lidgren.Network;

// Loot bags in the world (prefab "lootbag", LootBagScript in the client).
//
// The server owns the bags: it creates them (SpawnUnitsBatch with the item in the unit options), knows which
// item each one holds and removes them (KillUnitsBatch) when they are picked up or their time runs out.
// The client picks a bag up by itself (LootBagScript.PickupItems): it sends ServerRequestAddInventoryItem with
// the item id (the server answers ClientInvAdd, or ClientNoRoomForPickup), ServerReportItemPickup (also for
// health and steam) and then ServerRequestKill(LootBagDestroyed) for the bag.
//
// Like in the original, every player gets their own bags (each connection here is one player's instance).
public static class LootDrops
{
    public const int PrefabLootBag = 8;                 // prefab_table.txt: 8 lootbag
    private const byte KillReasonLootBagDestroyed = 4;  // KillReason.LootBagDestroyed
    private const byte KillReasonFizzled = 11;          // KillReason.Fizzled
    private const ushort MsgSpawnUnitsBatch = 45862;
    private const ushort MsgKillUnitsBatch = 45863;

    private sealed class Bag
    {
        public int ShortId;
        public NetServer Server = null!;
        public NetConnection Connection = null!;
        public StoredItem Item = null!;
        public float X;
        public float Y;
        public double ExpiresAt;
        public bool Collected;
        public string Source = "";
    }

    private static readonly object gate = new object();
    private static readonly Dictionary<int, Bag> bags = new Dictionary<int, Bag>();

    // Ids of the bags: far from the plane (1000), clouds (2000+), objects (50000+) and enemies (100000+, growing).
    private static int nextShortId = 5000000;

    // ------------------------------------------------------------------ kills

    // An enemy died (Enemies). Rolls its loot and drops the bags where it fell.
    public static void EnemyKilled(NetServer server, NetConnection connection, string role, string templateName, int tier, bool boss,
        float x, float y, double now, Action<string, string> log)
    {
        // The update loop of the enemies also calls this (chests opening): the loot goes to this player's account.
        using Accounts.Scope accountScope = Accounts.UseFor(connection);
        ItemSettings settings = ItemSettingsFile.Current;
        if (!settings.LootEnabled || !ItemCatalog.Loaded) return;
        if (connection.Status != NetConnectionStatus.Connected) return;

        LootContext ctx = InventoryHandler.BuildLootContext(connection);
        DrawnLoot drawn = LootTables.RollForKill(templateName, tier, boss, ctx, settings);
        if (drawn.IsEmpty) return;

        foreach (InstantLootDef instant in drawn.Instants)
            InventoryHandler.DeliverInstant(server, connection, role, instant, "loot of " + templateName, log);

        int count = drawn.Items.Count;
        for (int i = 0; i < count; i++)
        {
            // Bags spread around the place the enemy fell, so they do not stack.
            double angle = count == 1 ? Random.Shared.NextDouble() * Math.PI * 2 : (Math.PI * 2 * i / count) + Random.Shared.NextDouble() * 0.5;
            double distance = count == 1 ? Random.Shared.NextDouble() * 0.6 : 0.8 + Random.Shared.NextDouble() * 0.7;
            float bx = x + (float)(Math.Cos(angle) * distance);
            float by = y + (float)(Math.Sin(angle) * distance);
            SpawnBag(server, connection, InventoryStore.NewItem(drawn.Items[i]), bx, by, now, templateName);
        }

        log("LOOT", "[" + role + "] " + templateName + " (tier " + tier + (boss ? ", boss" : "") + ") dropped " +
            (count > 0 ? count + " bag(s): " + string.Join(", ", drawn.Items) : "no bag") +
            (drawn.Instants.Count > 0 ? " + " + string.Join("; ", drawn.Instants) : "") + " | " + string.Join(", ", drawn.Sources.Distinct()));
    }

    // ------------------------------------------------------------------ bags

    // Creates a bag holding the item at (x, y). Also used to drop an item from the inventory.
    public static int SpawnBag(NetServer server, NetConnection connection, StoredItem item, float x, float y, double now, string source)
    {
        float lifetime = ItemSettingsFile.Current.LootBagLifetime > 0f ? ItemSettingsFile.Current.LootBagLifetime : ItemCatalog.LootBagLifetime;
        int shortId;
        lock (gate)
        {
            shortId = nextShortId++;
            if (nextShortId > 5999999) nextShortId = 5000000;
            bags[shortId] = new Bag
            {
                ShortId = shortId,
                Server = server,
                Connection = connection,
                Item = item,
                X = x,
                Y = y,
                ExpiresAt = now + lifetime + 1.0,
                Source = source
            };
        }

        // LootBagScript.GenerateEntOptions / ApplyEntOptions: float lifetime, string defId, string id,
        // bool soulbound, bool used (1 bit each), variable int uses, variable int decayTimestamp.
        NetBuffer options = server.CreateMessage();
        options.Write(lifetime);
        options.Write(item.DefId);
        options.Write(item.Id);
        options.Write(item.Soulbound);
        options.Write(item.Used);
        options.WriteVariableInt32(item.Uses);
        options.WriteVariableInt32(item.DecayTimestamp);

        // SpawnUnitsBatch with one entry (same order as PlaneSpawn / WorldObjects). The bag stays still
        // (velocity 0): the client extrapolates server units with the velocity it was given.
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgSpawnUnitsBatch);
        m.WriteVariableUInt32(2u);                              // 1 item + 1 (0 = null list)
        m.Write((float)now);                                    // spawnTime
        m.WriteVariableInt32(PrefabLootBag);                    // prefabId
        m.WriteVariableInt32(shortId);                          // shortId
        m.Write((short)0);                                      // templateHash (no UnitConfig)
        m.Write((short)0);                                      // familyRootTemplateHash
        m.WriteVariableInt32(0);                                // parentShortId
        m.WriteVariableInt32(PlaneSpawn.PlayerTeamIndex);     // team
        m.Write((short)0);                                      // artReplacement
        m.Write(server.UniqueIdentifier);                       // authority = server
        // unitOptions (NetBufferCopperSerializer): LengthBytes + 1, then ALL LengthBytes bytes.
        // Do NOT use m.Write(options): Lidgren's Write(NetBuffer) drops the unused bits of the last byte, and
        // these options carry two 1-bit bools (soulbound, used), so 6 bits went missing. The client always reads whole bytes,
        // so the FieldUpdate after it was read 6 bits off and the bag spawned at a garbage position
        // (invisible and impossible to pick up).
        m.WriteVariableInt32(options.LengthBytes + 1);
        m.Write(options.Data, 0, options.LengthBytes);
        m.WriteVariableUInt32(1u | 2u | 4u);                    // FieldUpdate: position, velocity, rotation
        WriteScaled(m, x, 1024f);
        WriteScaled(m, y, 1024f);
        WriteScaled(m, 0f, 1024f);
        WriteScaled(m, 0f, 1024f);
        WriteScaled(m, 0f, 32f);
        if (connection.Status == NetConnectionStatus.Connected)
            server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
        return shortId;
    }

    // ServerRequestAddInventoryItem: the bag must belong to this player and hold this item. The bag is marked
    // as collected (the ServerRequestKill that follows removes it).
    public static bool TryTakeForPickup(NetConnection connection, int bagShortId, string itemId, out StoredItem item, out float x, out float y)
    {
        lock (gate)
        {
            item = null!;
            x = 0f;
            y = 0f;
            if (!bags.TryGetValue(bagShortId, out Bag? bag) || bag.Connection != connection || bag.Collected) return false;
            if (!string.Equals(bag.Item.Id, itemId, StringComparison.Ordinal)) return false;
            bag.Collected = true;
            item = bag.Item;
            x = bag.X;
            y = bag.Y;
            return true;
        }
    }

    // The item went back to the bag (cargo full): a new bag is dropped in the same place, so it is not lost.
    public static void ReturnToWorld(NetServer server, NetConnection connection, StoredItem item, float x, float y, double now)
    {
        SpawnBag(server, connection, item, x, y, now, "returned (cargo full)");
    }

    // What a bag holds (ServerReportItemPickup checks). Null when the bag is unknown.
    public static StoredItem? Peek(NetConnection connection, int bagShortId)
    {
        lock (gate)
        {
            return bags.TryGetValue(bagShortId, out Bag? bag) && bag.Connection == connection ? bag.Item : null;
        }
    }

    // ServerRequestKill for a bag (LootBagDestroyed after the pickup): the bag leaves the world.
    // Returns false when the shortId is not a bag (the request goes on to Game).
    public static bool HandleKillRequest(NetServer server, NetConnection connection, int shortId, double now, Action<string, string> log)
    {
        Bag? bag;
        lock (gate)
        {
            if (!bags.TryGetValue(shortId, out bag) || bag.Connection != connection) return false;
            bags.Remove(shortId);
        }
        SendKill(bag, KillReasonLootBagDestroyed, now);
        if (!bag.Collected)
        {
            // Health and steam are picked up by the client alone; equipment without ServerRequestAddInventoryItem
            // means the item was lost.
            ItemDef? def = ItemCatalog.Get(bag.Item.DefId);
            bool stat = def != null && (def.SlotValue == ItemCatalog.SlotHealth || def.SlotValue == ItemCatalog.SlotEnergy);
            if (!stat) log("LOOT", "bag " + shortId + " (" + bag.Item.DefId + ") collected without an inventory request: item discarded");
        }
        return true;
    }

    // Bags whose time ran out (LootBagScript.CheckLifetime runs on the server in the original).
    public static void Update(NetServer server, double now)
    {
        List<Bag>? expired = null;
        lock (gate)
        {
            if (bags.Count == 0) return;
            foreach (Bag bag in bags.Values)
            {
                if (bag.Server == server && now >= bag.ExpiresAt) (expired ??= new List<Bag>()).Add(bag);
            }
            if (expired == null) return;
            foreach (Bag bag in expired) bags.Remove(bag.ShortId);
        }
        foreach (Bag bag in expired) SendKill(bag, KillReasonFizzled, now);
    }

    public static void Disconnected(NetConnection? connection)
    {
        if (connection == null) return;
        lock (gate)
        {
            foreach (int id in bags.Values.Where(b => b.Connection == connection).Select(b => b.ShortId).ToList()) bags.Remove(id);
        }
    }

    public static int CountFor(NetConnection connection)
    {
        lock (gate) return bags.Values.Count(b => b.Connection == connection);
    }

    // Removes every bag of this player (admin command).
    public static int ClearFor(NetServer server, NetConnection connection, double now)
    {
        List<Bag> mine;
        lock (gate)
        {
            mine = bags.Values.Where(b => b.Connection == connection).ToList();
            foreach (Bag bag in mine) bags.Remove(bag.ShortId);
        }
        foreach (Bag bag in mine) SendKill(bag, KillReasonFizzled, now);
        return mine.Count;
    }

    // KillUnitsBatch with one bag (KillInfoSerializer: reason, x and y in 16 bits, rotation in 8 bits, hit result).
    private static void SendKill(Bag bag, byte reason, double now)
    {
        if (bag.Connection.Status != NetConnectionStatus.Connected) return;
        NetOutgoingMessage m = bag.Server.CreateMessage();
        m.Write(MsgKillUnitsBatch);
        m.WriteVariableUInt32(2u);
        m.Write((float)now);
        m.WriteVariableInt32(bag.ShortId);
        m.Write(reason);
        m.WriteRangedSingle(Math.Clamp(bag.X, -2000f, 2000f), -2000f, 2000f, 16);
        m.WriteRangedSingle(Math.Clamp(bag.Y, -2000f, 2000f), -2000f, 2000f, 16);
        m.WriteRangedSingle(0f, -180f, 180f, 8);
        m.Write((byte)0);
        bag.Server.SendMessage(m, bag.Connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    // ScaledHelper.WriteScaled: variable int of floor(value * scale).
    private static void WriteScaled(NetBuffer buf, float value, float scale)
    {
        buf.WriteVariableInt32((int)Math.Floor((double)value * scale));
    }
}
