using Lidgren.Network;

// Inventory audit. Every inventory change the server accepts gets an audit id (InventoryStore.NextAudit), which goes back to the
// client in the RPC answer (or in ClientInvAdd). After applying the change the client reports what it did in
// ServerAuditInvAction `61999` (action id, its clock, ALInvAction). The original server compared the two and counted the
// failures; ClientOnAuditChange `23009` told the client the new count (this build's client ignores it).
//
// Here a report fails when its id was never given out (it is not below the account's next id) or when the same id comes twice
// in one connection. Failures go to the server log and to /audit; the count is sent to the client.
public static class InventoryAudit
{
    public const ushort MsgClientOnAuditChange = 23009;
    private static readonly string[] ActionNames = { "Add", "Remove", "Swap", "Consume", "Accumulate" };

    private sealed class State
    {
        public int Reported;
        public int Failed;
        public readonly HashSet<int> Seen = new HashSet<int>();
        public readonly Queue<string> Recent = new Queue<string>();
    }

    private static readonly object gate = new object();
    private static readonly Dictionary<long, State> states = new Dictionary<long, State>();

    // msg is positioned just after the action id.
    public static void Reported(NetServer server, NetConnection connection, string role, int auditId, NetIncomingMessage msg, Action<string, string> logTo)
    {
        string text = Describe(msg);
        int next = InventoryStore.Read(d => d.NextAuditId);
        bool unknown = auditId < 1 || auditId >= next;
        State s;
        bool duplicate;
        int failed;
        lock (gate)
        {
            if (!states.TryGetValue(connection.RemoteUniqueIdentifier, out State? found)) states[connection.RemoteUniqueIdentifier] = found = new State();
            s = found;
            duplicate = !s.Seen.Add(auditId);
            if (s.Seen.Count > 4096) s.Seen.Clear();
            s.Reported++;
            bool bad = unknown || duplicate;
            if (bad) s.Failed++;
            s.Recent.Enqueue((bad ? "FAIL " : "ok ") + auditId + " " + text);
            while (s.Recent.Count > 12) s.Recent.Dequeue();
            failed = s.Failed;
            if (!bad) return;
        }
        string who = PlayerSessions.NameOf(connection);
        logTo("AUDIT", "[" + role + "] " + who + ": audit id " + auditId + " " + (duplicate ? "was already reported" : "was never given out (next is " + next + ")") +
            " (" + text + "); " + failed + " failure(s) in this connection");
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgClientOnAuditChange);
        m.Write(failed);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    // "Swap armor_3 of <plane>" from the ALInvAction of the message; "?" when it cannot be read.
    private static string Describe(NetIncomingMessage msg)
    {
        long start = msg.Position;
        try
        {
            msg.ReadSingle();   // clientFixedTime
            msg.ReadSingle();   // clientChosenServerTime
            int kind = msg.ReadByte();
            string character = msg.ReadString() ?? "";
            StoredItem? item = InventoryWire.ReadItem(msg);
            string name = kind >= 0 && kind < ActionNames.Length ? ActionNames[kind] : "action " + kind;
            return name + " " + (item?.DefId ?? "(empty)") + (character.Length > 0 ? " of " + character : "");
        }
        catch
        {
            return "?";
        }
        finally
        {
            msg.Position = start;
        }
    }

    public static string Summary(NetConnection connection)
    {
        lock (gate)
        {
            if (!states.TryGetValue(connection.RemoteUniqueIdentifier, out State? s)) return "Audit: the client has not reported any inventory action yet.";
            return "Audit: " + s.Reported + " action(s) reported, " + s.Failed + " failure(s). Last: " + (s.Recent.Count == 0 ? "none" : string.Join(" | ", s.Recent.TakeLast(5)));
        }
    }

    public static void Disconnected(NetConnection? connection)
    {
        if (connection == null) return;
        lock (gate) states.Remove(connection.RemoteUniqueIdentifier);
    }
}
