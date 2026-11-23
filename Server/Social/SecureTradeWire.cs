using Lidgren.Network;

// Safe trade: the messages the server sends (written once per recipient, because every client names the planes with its own ids).
public static partial class SecureTrade
{
    private const ushort MsgOpen = 12501;           // ClientOpenSecureTrade(string otherName, int otherShortId, OfferComplete offer, bool isApplicableForGuildTax)
    private const ushort MsgRequest = 12502;        // ClientRequestSecureTrade(string otherName, int otherShortId)
    private const ushort MsgUpdate = 12504;         // ClientSecureTradeUpdate(int initiator, int target, int version, OfferComplete offer, bool tax)
    private const ushort MsgResult = 12507;         // ClientSecureTradeResult(int initiator, int target, bool accepted)

    // (id of the initiator, id of the target) as the viewer's client knows them: its own plane and the relay id of the other.
    // The last known ids are kept in the parts, so the closing message still names the planes after the other player's plane is gone.
    private static (int Initiator, int Target) IdsFor(Trade t, NetConnection viewer)
    {
        Part mine = t.PartOf(viewer);
        Part theirs = t.Other(viewer);
        int own = Game.GetPlaneShortId(viewer);
        if (own != 0) mine.ShortId = own; else own = mine.ShortId;
        int other = PlayerRelay.RelayIdOf(theirs.Connection.RemoteUniqueIdentifier);
        if (other != 0) theirs.RelayId = other; else other = theirs.RelayId;
        return t.Initiator.Connection == viewer ? (own, other) : (other, own);
    }

    private static NetOutgoingMessage WriteRequest(NetServer server, string initiatorName, int initiatorRelayId)
    {
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgRequest);
        m.Write(initiatorName);
        m.Write(initiatorRelayId);
        return m;
    }

    // OfferCompleteSerializer.Write: fee mode, fee currency, fee value, then the initiator's and the target's part.
    private static void WriteOffer(NetBuffer m, Trade t, (int Initiator, int Target) ids)
    {
        m.Write(t.Fee);
        m.Write(t.FeeCurrency);
        m.WriteVariableInt32(t.FeeValue);
        WritePart(m, t.Initiator, ids.Initiator);
        WritePart(m, t.Target, ids.Target);
    }

    // OfferPartSerializer.Write.
    private static void WritePart(NetBuffer m, Part part, int shortId)
    {
        m.Write(shortId);
        m.Write(SecureTradeRules.MainCurrency);
        m.Write(part.Currency);
        m.Write(part.Accepted);
        m.WriteVariableInt32(part.Items.Count);
        foreach (StoredItem item in part.Items) InventoryWire.WriteItem(m, item);
    }

    // Both windows open (the trade was accepted).
    private static void SendOpen(Trade t)
    {
        foreach (NetConnection viewer in new[] { t.Initiator.Connection, t.Target.Connection })
        {
            NetOutgoingMessage m = t.Server.CreateMessage();
            m.Write(MsgOpen);
            m.Write(t.Other(viewer).Name);
            (int Initiator, int Target) ids = IdsFor(t, viewer);
            m.Write(t.Initiator.Connection == viewer ? ids.Target : ids.Initiator);   // the id of the other player
            WriteOffer(m, t, ids);
            m.Write(t.GuildRate);
            t.Server.SendMessage(m, viewer, NetDeliveryMethod.ReliableOrdered, 0);
        }
    }

    // The state of the trade (offers, marks, fee and version) for one of the players.
    private static void SendUpdate(Trade t, NetConnection viewer)
    {
        if (viewer.Status != NetConnectionStatus.Connected) return;
        (int Initiator, int Target) ids = IdsFor(t, viewer);
        NetOutgoingMessage m = t.Server.CreateMessage();
        m.Write(MsgUpdate);
        m.Write(ids.Initiator);
        m.Write(ids.Target);
        m.Write(t.Version);
        WriteOffer(m, t, ids);
        m.Write(t.GuildRate);
        t.Server.SendMessage(m, viewer, NetDeliveryMethod.ReliableOrdered, 0);
    }

    private static NetOutgoingMessage WriteResult(Trade t, NetConnection viewer, bool accepted)
    {
        (int Initiator, int Target) ids = IdsFor(t, viewer);
        NetOutgoingMessage m = t.Server.CreateMessage();
        m.Write(MsgResult);
        m.Write(ids.Initiator);
        m.Write(ids.Target);
        m.Write(accepted);
        return m;
    }
}
