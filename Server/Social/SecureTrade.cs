using Lidgren.Network;

// Safe trade between two players of the same room (Nexus, open world or one dungeon instance): the player menu "Trade" / "/trade NAME".
//
// Flow of the client (UGUISecureTrade, GameClient.ClientRequestSecureTrade):
//   1. A sends ServerInitiateSecureTrade(shortId of B). The server tells B (ClientRequestSecureTrade(name of A, id of A)): B sees a
//      chat line and a bubble over A, and answers with "/trade" (no name) = ServerResponseSecureTrade(id of A, true).
//   2. On "yes" the server opens the window on both clients (ClientOpenSecureTrade with an empty OfferComplete).
//   3. Each side picks up to 4 items from the cargo of its live plane and an amount of gold, then the client sends
//      ServerSecureTradeOffer(initiator, target, version, Offer). The server checks it, keeps ITS OWN copy of the items, adds 1 to the
//      version, clears both "accepted" marks and sends ClientSecureTradeUpdate to both (the fee is recalculated every time).
//   4. Each side accepts (ServerSecureTradeAccept with the version it saw; an old version is ignored and answered with the current
//      state). When both accepted, the server checks everything again, swaps the items and gold and sends ClientSecureTradeResult.
//   5. Closing the window sends ServerSecureTradeCancel: the other side gets ClientSecureTradeResult(false).
//
// The shortIds in the messages are the ones of each client: its own plane (1000+) and the relay id (7,000,000+) of the other plane,
// so every message is written once per recipient. The server never trusts the items in the offer: only the ids are used, and the items
// come from the account inventory. Items stay in the cargo of the owner until the very end.
// Code: this file (flow), SecureTradeWire.cs (messages), SecureTradeExecute.cs (the swap), SecureTradeRules.cs (numbers and fee).
public static partial class SecureTrade
{
    private const ushort MsgInitiate = 12500;       // ServerInitiateSecureTrade(int shortId)
    private const ushort MsgResponse = 12503;       // ServerResponseSecureTrade(int otherShortId, bool acceptRequest)
    private const ushort MsgOffer = 12505;          // ServerSecureTradeOffer(int initiator, int target, int version, Offer offer)
    private const ushort MsgAccept = 12506;         // ServerSecureTradeAccept(int initiator, int target, int version, bool accept)
    private const ushort MsgCancel = 12508;         // ServerSecureTradeCancel(int initiator, int target)

    // One side of an offer.
    private sealed class Part
    {
        public NetConnection Connection = null!;
        public string Name = "";
        public string AccountId = "";
        public int Currency;
        public List<StoredItem> Items = new List<StoredItem>();
        public bool Accepted;
        public int ShortId;                         // the id of its own plane, for its own client
        public int RelayId;                         // the id the other players know its plane by
    }

    private sealed class Trade
    {
        public NetServer Server = null!;
        public string Role = "";
        public Part Initiator = null!;
        public Part Target = null!;
        public bool Open;                           // false = the request waits for the answer of the target
        public int Version;
        public byte Fee = SecureTradeRules.FeeSplit;
        public byte FeeCurrency = SecureTradeRules.CurrencyMain;
        public int FeeValue;
        public bool GuildRate;
        public DateTime Created = DateTime.UtcNow;
        public DateTime Changed = DateTime.UtcNow;

        public Part PartOf(NetConnection connection) => Initiator.Connection == connection ? Initiator : Target;
        public Part Other(NetConnection connection) => Initiator.Connection == connection ? Target : Initiator;
        public bool Has(NetConnection connection) => Initiator.Connection == connection || Target.Connection == connection;
    }

    private static readonly object gate = new object();
    private static readonly List<Trade> trades = new List<Trade>();
    private static Action<string, string> log = (_, _) => { };
    private static bool described;

    public static void Start(Action<string, string> logger) => log = logger;

    public static bool Handles(ushort id) => id == MsgInitiate || id == MsgResponse || id == MsgOffer || id == MsgAccept || id == MsgCancel;

    // The message is positioned after the message id. role: "[nexus]", "[world]"...
    public static void Handle(NetServer server, NetIncomingMessage msg, ushort id, string role)
    {
        role = role.Trim('[', ']');
        NetConnection me = msg.SenderConnection;
        lock (gate)
        {
            Expire();
            switch (id)
            {
                case MsgInitiate: Initiate(server, me, role, msg.ReadInt32()); break;
                case MsgResponse: Respond(server, me, role, msg.ReadInt32(), msg.ReadBoolean()); break;
                case MsgOffer: Offered(server, me, msg); break;
                case MsgAccept: Accepted(server, me, msg); break;
                case MsgCancel: Cancel(me, "closed the window"); break;
            }
        }
    }

    // A connection ended (it switched servers or dropped): its trade ends.
    public static void PlayerLeft(NetConnection connection)
    {
        // Same lock order as a message (the game lock, then this one): the messages written here ask the game for the plane ids.
        lock (Game.Lock)
        lock (gate)
        {
            Trade? t = trades.FirstOrDefault(x => x.Has(connection));
            if (t != null) End(t, false, "left the server");
        }
    }

    // ------------------------------------------------------------------ 1. the request

    private static void Initiate(NetServer server, NetConnection me, string role, int targetShortId)
    {
        if (!SecureTradeRules.Enabled)
        {
            Say(server, me, "Trading between players is turned off on this server.");
            return;
        }
        NetConnection? other = PlayerRelay.ConnectionByRelayId(me, targetShortId);
        if (other == null || other == me || Game.GetPlaneShortId(me) == 0 || Game.GetPlaneShortId(other) == 0)
        {
            Say(server, me, "That player is not here, or has no plane in the air.");
            return;
        }
        if (trades.Any(t => t.Open && (t.Has(me) || t.Has(other))))
        {
            Say(server, me, trades.Any(t => t.Open && t.Has(me)) ? "You are already trading: close that window first." : PlayerSessions.NameOf(other) + " is already trading.");
            return;
        }

        string myAccount, otherAccount;
        using (Accounts.UseFor(me)) myAccount = Accounts.AccountIdText;
        using (Accounts.UseFor(other)) otherAccount = Accounts.AccountIdText;
        if (SocialHandler.IsBlockedBy(otherAccount, AccountOf(me)) )
        {
            Say(server, me, PlayerSessions.NameOf(other) + " does not take trade requests.");
            return;
        }

        // A new request replaces an older one of the same player, and the target keeps only the last request it got.
        trades.RemoveAll(t => !t.Open && (t.Has(me) || t.Target.Connection == other));
        var trade = new Trade
        {
            Server = server,
            Role = role,
            Initiator = new Part { Connection = me, Name = PlayerSessions.NameOf(me), AccountId = myAccount },
            Target = new Part { Connection = other, Name = PlayerSessions.NameOf(other), AccountId = otherAccount }
        };
        trades.Add(trade);
        if (!described)
        {
            described = true;
            log("TRADE", "rules: " + SecureTradeRules.Describe());
        }

        int myRelayId = PlayerRelay.RelayIdOf(me.RemoteUniqueIdentifier);
        server.SendMessage(WriteRequest(server, trade.Initiator.Name, myRelayId), other, NetDeliveryMethod.ReliableOrdered, 0);
        Say(server, me, "Trade request sent to " + trade.Target.Name + ".");
        log("TRADE", "[" + role + "] " + trade.Initiator.Name + " asked " + trade.Target.Name + " to trade");
    }

    // ------------------------------------------------------------------ 2. the answer

    private static void Respond(NetServer server, NetConnection me, string role, int initiatorShortId, bool accept)
    {
        Trade? t = trades.FirstOrDefault(x => !x.Open && x.Target.Connection == me &&
                                              PlayerRelay.RelayIdOf(x.Initiator.Connection.RemoteUniqueIdentifier) == initiatorShortId);
        if (t == null)
        {
            Say(server, me, "There is no trade request to answer (it may have expired).");
            return;
        }
        if (!accept)
        {
            trades.Remove(t);
            Say(server, t.Initiator.Connection, t.Target.Name + " declined the trade.");
            log("TRADE", "[" + role + "] " + t.Target.Name + " declined the trade of " + t.Initiator.Name);
            return;
        }
        if (Game.GetPlaneShortId(t.Initiator.Connection) == 0 || Game.GetPlaneShortId(me) == 0)
        {
            trades.Remove(t);
            Say(server, me, "The trade ended: a plane is not in the air.");
            return;
        }

        t.Open = true;
        t.Changed = DateTime.UtcNow;
        t.GuildRate = false;
        Recalculate(t);
        SendOpen(t);
        log("TRADE", "[" + role + "] " + t.Initiator.Name + " and " + t.Target.Name + " are trading");
    }

    // ------------------------------------------------------------------ 3. offers

    private static void Offered(NetServer server, NetConnection me, NetIncomingMessage msg)
    {
        int initiatorId = msg.ReadInt32();
        int targetId = msg.ReadInt32();
        int version = msg.ReadInt32();
        msg.ReadInt32();                                            // Offer.PlayerShortId: the sender
        msg.ReadString();                                           // Offer.CurrencyKey: the server decides the currency
        int currency = msg.ReadInt32();
        byte fee = msg.ReadByte();
        byte feeCurrency = msg.ReadByte();
        int count = Math.Clamp(msg.ReadVariableInt32(), 0, 32);
        var wanted = new List<string>();
        for (int i = 0; i < count; i++)
        {
            StoredItem? item = InventoryWire.ReadItem(msg);
            if (item != null) wanted.Add(item.Id);
        }

        Trade? t = trades.FirstOrDefault(x => x.Open && x.Has(me));
        if (t == null || !IdsMatch(t, me, initiatorId, targetId)) return;
        if (version != t.Version)
        {
            SendUpdate(t, me);                                      // the window of this player is out of date: it gets the real state
            return;
        }

        Part mine = t.PartOf(me);
        string problem = "";
        List<StoredItem>? items = Resolve(me, wanted, ref problem);
        if (items == null || currency < 0 || fee > 2 || feeCurrency > 1)
        {
            if (problem.Length == 0) problem = "that offer is not valid";
            Say(server, me, "Trade: " + problem + ".");
            SendUpdate(t, me);
            return;
        }

        mine.Items = items;
        mine.Currency = currency;
        t.Fee = fee;
        t.FeeCurrency = feeCurrency;
        Changed(t);
    }

    // The items of the offer, from the cargo of the live plane. Null (and the reason) when one cannot be traded.
    private static List<StoredItem>? Resolve(NetConnection owner, List<string> ids, ref string problem)
    {
        if (ids.Count > SecureTradeRules.SlotLimit) { problem = "at most " + SecureTradeRules.SlotLimit + " items fit in one offer"; return null; }
        if (ids.Distinct().Count() != ids.Count) { problem = "the same item twice"; return null; }
        string? planeId = Game.GetPlaneCharacterId(owner);
        if (planeId == null) { problem = "no plane in the air"; return null; }

        string reason = "";
        using (Accounts.UseFor(owner))
        {
            List<StoredItem>? found = InventoryStore.Read(d =>
            {
                InvAccess? plane = InventoryStore.Resolve(d, planeId);
                if (plane == null) { reason = "the plane has no inventory"; return null; }
                var list = new List<StoredItem>();
                foreach (string id in ids)
                {
                    InvPos pos = InventoryStore.Find(plane, id);
                    StoredItem? item = pos.Valid && pos.Location == InvPos.LocCargo ? InventoryStore.Get(plane, pos) : null;
                    if (item == null) { reason = "an item is no longer in the cargo"; return null; }
                    if (!SecureTradeRules.IsTradable(item, out string why)) { reason = item.DefId + " cannot be traded (" + why + ")"; return null; }
                    list.Add(item.Clone());
                }
                return list;
            });
            if (found == null) { problem = reason; return null; }
            return found;
        }
    }

    // ------------------------------------------------------------------ 4. accept

    private static void Accepted(NetServer server, NetConnection me, NetIncomingMessage msg)
    {
        int initiatorId = msg.ReadInt32();
        int targetId = msg.ReadInt32();
        int version = msg.ReadInt32();
        bool accept = msg.ReadBoolean();

        Trade? t = trades.FirstOrDefault(x => x.Open && x.Has(me));
        if (t == null || !IdsMatch(t, me, initiatorId, targetId)) return;
        if (version != t.Version)
        {
            SendUpdate(t, me);
            return;
        }

        t.PartOf(me).Accepted = accept;
        t.Changed = DateTime.UtcNow;
        if (!(t.Initiator.Accepted && t.Target.Accepted))
        {
            SendUpdate(t, t.Other(me).Connection);                  // the other window shows the mark
            return;
        }

        // Both agreed to the version they both saw.
        string failure = Execute(t);
        if (failure.Length == 0)
        {
            End(t, true, "");
            return;
        }
        t.Initiator.Accepted = false;
        t.Target.Accepted = false;
        Say(server, t.Initiator.Connection, "Trade failed: " + failure + ".");
        Say(server, t.Target.Connection, "Trade failed: " + failure + ".");
        log("TRADE", "[" + t.Role + "] " + t.Initiator.Name + " and " + t.Target.Name + ": the swap was refused (" + failure + ")");
        t.Version++;
        SendUpdate(t, t.Initiator.Connection);
        SendUpdate(t, t.Target.Connection);
    }

    // ------------------------------------------------------------------ 5. cancel and end

    private static void Cancel(NetConnection me, string why)
    {
        Trade? t = trades.FirstOrDefault(x => x.Has(me));
        if (t != null) End(t, false, why, me);
    }

    // The trade is over. accepted: it was completed. closedBy: the one who closed the window (does not need the result message).
    private static void End(Trade t, bool completed, string why, NetConnection? closedBy = null)
    {
        trades.Remove(t);
        if (t.Open)
        {
            foreach (Part p in new[] { t.Initiator, t.Target })
            {
                if (p.Connection == closedBy || p.Connection.Status != NetConnectionStatus.Connected) continue;
                t.Server.SendMessage(WriteResult(t, p.Connection, completed), p.Connection, NetDeliveryMethod.ReliableOrdered, 0);
            }
        }
        else if (!completed && why == "no answer")
        {
            Say(t.Server, t.Initiator.Connection, t.Target.Name + " did not answer the trade request.");
        }
        log("TRADE", "[" + t.Role + "] " + t.Initiator.Name + " / " + t.Target.Name + ": " + (completed ? "completed" : "ended (" + why + ")"));
    }

    // Requests that nobody answered, and trades left open for a long time.
    private static void Expire()
    {
        DateTime now = DateTime.UtcNow;
        foreach (Trade t in trades.ToList())
        {
            double idle = (now - (t.Open ? t.Changed : t.Created)).TotalSeconds;
            bool dead = t.Initiator.Connection.Status != NetConnectionStatus.Connected || t.Target.Connection.Status != NetConnectionStatus.Connected;
            if (dead) End(t, false, "a player left");
            else if (!t.Open && idle > SecureTradeRules.RequestSeconds) End(t, false, "no answer");
            else if (t.Open && idle > SecureTradeRules.IdleSeconds) End(t, false, "idle");
        }
    }

    // ------------------------------------------------------------------ helpers

    // The ids a client writes are its own plane and the relay id of the other one.
    private static bool IdsMatch(Trade t, NetConnection me, int initiatorId, int targetId)
    {
        NetConnection other = t.Other(me).Connection;
        int own = Game.GetPlaneShortId(me);
        int theirs = PlayerRelay.RelayIdOf(other.RemoteUniqueIdentifier);
        if (own == 0 || theirs == 0) return false;
        return t.Initiator.Connection == me ? initiatorId == own && targetId == theirs : initiatorId == theirs && targetId == own;
    }

    // A change of the offer: the version goes up, nobody has accepted anymore and the fee is calculated again.
    private static void Changed(Trade t)
    {
        t.Version++;
        t.Initiator.Accepted = false;
        t.Target.Accepted = false;
        t.Changed = DateTime.UtcNow;
        Recalculate(t);
        SendUpdate(t, t.Initiator.Connection);
        SendUpdate(t, t.Target.Connection);
    }

    private static void Recalculate(Trade t)
    {
        bool sameFlock = false;
        try
        {
            sameFlock = FlockStore.MembershipsOf(t.Initiator.AccountId).Any(f => f.ActiveMember(t.Target.AccountId) != null);
        }
        catch (Exception)
        {
            // flocks off or not loaded: the normal rate
        }
        var all = new List<StoredItem>(t.Initiator.Items);
        all.AddRange(t.Target.Items);
        t.GuildRate = SecureTradeRules.GuildRateApplies(sameFlock);
        t.FeeValue = SecureTradeRules.Fee(all, t.Initiator.Currency + t.Target.Currency, t.FeeCurrency, sameFlock);
    }

    private static Accounts.Account AccountOf(NetConnection connection)
    {
        using (Accounts.UseFor(connection)) return Accounts.Current;
    }

    private static void Say(NetServer server, NetConnection connection, string text) => WorldObjects.SendChat(server, connection, text);
}
