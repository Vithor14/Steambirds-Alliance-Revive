using Lidgren.Network;

// Safe trade: the swap, when both players accepted the same version of the offer.
//
// Everything is checked again on the real inventories (the items may have been used, thrown away or sold since they were offered),
// then each account is changed in one step (InventoryStore.Change): the offered items leave the cargo of the live plane, the items of the
// other player enter the first free cargo slots, and the gold or iron (offer and fee) is moved. If the second account fails after the
// first one changed, the first is changed back, so nothing is lost or created. Only after both worked the clients are told.
public static partial class SecureTrade
{
    private sealed class Move
    {
        public List<string> Out = new List<string>();            // ids of the items that leave
        public List<StoredItem> In = new List<StoredItem>();     // items that enter
        public Dictionary<string, int> Pay = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> Receive = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    private sealed class Applied
    {
        public string CharacterId = "";
        public List<InvPos> Removed = new List<InvPos>();
        public List<(InvPos Pos, StoredItem Item, int Audit)> Added = new List<(InvPos, StoredItem, int)>();
        public Dictionary<string, int> Currencies = new Dictionary<string, int>();
    }

    // "" = done; otherwise why it was not (nothing changed).
    private static string Execute(Trade t)
    {
        if (!SecureTradeRules.Enabled) return "trading is off on this server";
        string? planeA = Game.GetPlaneCharacterId(t.Initiator.Connection);
        string? planeB = Game.GetPlaneCharacterId(t.Target.Connection);
        if (planeA == null || planeB == null) return "a plane is no longer in the air";

        // Who pays the fee.
        int fee = t.FeeValue;
        int feeA = t.Fee == SecureTradeRules.FeeInitiator ? fee : t.Fee == SecureTradeRules.FeeTarget ? 0 : (fee + 1) / 2;
        int feeB = fee - feeA;
        string main = SecureTradeRules.MainCurrency;
        string feeKey = SecureTradeRules.FeeCurrencyKey(t.FeeCurrency);

        var payA = new Dictionary<string, int>(StringComparer.Ordinal);
        var payB = new Dictionary<string, int>(StringComparer.Ordinal);
        Add(payA, main, t.Initiator.Currency);
        Add(payB, main, t.Target.Currency);
        Add(payA, feeKey, feeA);
        Add(payB, feeKey, feeB);

        // 1. check both sides on the real inventories and take the items as they are now.
        string problem = Check(t.Initiator, planeA, payA, t.Target.Items.Count, out List<StoredItem> outA);
        if (problem.Length > 0) return t.Initiator.Name + ": " + problem;
        problem = Check(t.Target, planeB, payB, t.Initiator.Items.Count, out List<StoredItem> outB);
        if (problem.Length > 0) return t.Target.Name + ": " + problem;

        var moveA = new Move { Out = outA.Select(i => i.Id).ToList(), In = outB, Pay = payA };
        var moveB = new Move { Out = outB.Select(i => i.Id).ToList(), In = outA, Pay = payB };
        Add(moveA.Receive, main, t.Target.Currency);
        Add(moveB.Receive, main, t.Initiator.Currency);

        // 2. change both accounts.
        string failure = Apply(t.Initiator.Connection, planeA, moveA, out Applied? doneA);
        if (failure.Length > 0) return t.Initiator.Name + ": " + failure;
        failure = Apply(t.Target.Connection, planeB, moveB, out Applied? doneB);
        if (failure.Length > 0)
        {
            // Put the first account back as it was: it gets back what it gave and gives back what it got.
            var undo = new Move { Out = moveA.In.Select(i => i.Id).ToList(), In = outA, Pay = moveA.Receive, Receive = moveA.Pay };
            string undone = Apply(t.Initiator.Connection, planeA, undo, out _);
            log("TRADE", "[" + t.Role + "] the second account failed (" + failure + "); the first was " + (undone.Length == 0 ? "restored" : "NOT restored: " + undone));
            return t.Target.Name + ": " + failure;
        }

        // 3. the clients.
        Notify(t, t.Initiator.Connection, doneA!);
        Notify(t, t.Target.Connection, doneB!);
        log("TRADE", "[" + t.Role + "] " + t.Initiator.Name + " gave " + Describe(outA, t.Initiator.Currency, main) + ", " + t.Target.Name + " gave " +
            Describe(outB, t.Target.Currency, main) + "; fee " + fee + " " + feeKey + " (" + feeA + " + " + feeB + ")");
        return "";
    }

    // Can this player really give what the offer says? Returns the reason (empty = yes) and the current items.
    private static string Check(Part part, string planeId, Dictionary<string, int> pay, int incoming, out List<StoredItem> items)
    {
        string problem = "";
        List<StoredItem> found = new List<StoredItem>();
        using (Accounts.UseFor(part.Connection))
        {
            InventoryStore.Read(d =>
            {
                InvAccess? plane = InventoryStore.Resolve(d, planeId);
                if (plane == null) { problem = "the plane has no inventory"; return 0; }
                foreach (StoredItem offered in part.Items)
                {
                    InvPos pos = InventoryStore.Find(plane, offered.Id);
                    StoredItem? item = pos.Valid && pos.Location == InvPos.LocCargo ? InventoryStore.Get(plane, pos) : null;
                    if (item == null) { problem = "an offered item is no longer in the cargo"; return 0; }
                    if (!SecureTradeRules.IsTradable(item, out string why)) { problem = item.DefId + " cannot be traded (" + why + ")"; return 0; }
                    found.Add(item.Clone());
                }
                foreach (KeyValuePair<string, int> p in pay)
                {
                    if (InventoryStore.Currency(d, p.Key) < p.Value) { problem = "not enough " + p.Key + " (" + p.Value + " needed)"; return 0; }
                }
                int free = plane.Cargo.Count(x => x == null);
                if (free + found.Count < incoming) { problem = "no room in the cargo for " + incoming + " item(s)"; return 0; }
                return 0;
            });
        }
        items = found;
        return problem;
    }

    // One account: the items leave, the others enter, the currencies move. Nothing changes when anything does not fit.
    private static string Apply(NetConnection who, string planeId, Move move, out Applied? done)
    {
        string failure = "";
        Applied? result;
        using (Accounts.UseFor(who))
        {
            result = InventoryStore.Change(d =>
            {
                InvAccess? plane = InventoryStore.Resolve(d, planeId);
                if (plane == null) { failure = "the plane has no inventory"; return null; }

                var positions = new List<InvPos>();
                foreach (string id in move.Out)
                {
                    InvPos pos = InventoryStore.Find(plane, id);
                    if (!pos.Valid || pos.Location != InvPos.LocCargo) { failure = "an item changed place"; return null; }
                    positions.Add(pos);
                }
                foreach (KeyValuePair<string, int> p in move.Pay)
                {
                    if (InventoryStore.Currency(d, p.Key) < p.Value) { failure = "not enough " + p.Key; return null; }
                }
                if (plane.Cargo.Count(x => x == null) + positions.Count < move.In.Count) { failure = "no room in the cargo"; return null; }

                var applied = new Applied { CharacterId = planeId };
                foreach (InvPos pos in positions)
                {
                    InventoryStore.Set(plane, pos, null);
                    applied.Removed.Add(pos);
                }
                foreach (KeyValuePair<string, int> p in move.Pay) InventoryStore.AddCurrency(d, p.Key, -p.Value);
                foreach (KeyValuePair<string, int> p in move.Receive) InventoryStore.AddCurrency(d, p.Key, p.Value);
                foreach (StoredItem item in move.In)
                {
                    StoredItem copy = item.Clone();
                    InvPos pos = InventoryStore.FirstFreeCargo(plane);
                    InventoryStore.Set(plane, pos, copy);
                    applied.Added.Add((pos, copy, InventoryStore.NextAudit(d)));
                }
                applied.Currencies = new Dictionary<string, int>(d.Currencies);
                return applied;
            });
        }
        done = result;
        return failure;
    }

    // What changed, for the client of that player.
    private static void Notify(Trade t, NetConnection who, Applied a)
    {
        using (Accounts.UseFor(who))
        {
            foreach (InvPos pos in a.Removed)
                InventoryHandler.Send(t.Server, who, m => InventoryWire.WriteClientRemoveInventoryItem(m, a.CharacterId, pos));
            foreach ((InvPos Pos, StoredItem Item, int Audit) add in a.Added)
                InventoryHandler.Send(t.Server, who, m => InventoryWire.WriteClientInvAdd(m, a.CharacterId, add.Item, add.Pos, 0f, 0f, add.Audit));
            InventoryHandler.SendCurrencies(t.Server, who, a.Currencies);
        }
    }

    private static void Add(Dictionary<string, int> dict, string key, int amount)
    {
        if (amount <= 0) return;
        dict[key] = (dict.TryGetValue(key, out int old) ? old : 0) + amount;
    }

    private static string Describe(List<StoredItem> items, int currency, string currencyKey)
    {
        var parts = items.Select(i => i.ToString()).ToList();
        if (currency > 0) parts.Add(currency + " " + currencyKey);
        return parts.Count == 0 ? "nothing" : string.Join(" + ", parts);
    }
}
