using System.Globalization;
using Lidgren.Network;

// Sending inventories, currencies and unlocks to the client.
public static partial class InventoryHandler
{
    public static void Send(NetServer server, NetConnection? connection, Action<NetOutgoingMessage> write)
    {
        ProgressionMessages.Send(server, connection, write);
    }

    // ClientReplaceInventory for one inventory of the account (storage or plane).
    public static void SendInventory(NetServer server, NetConnection connection, string? characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return;
        byte[] account = Accounts.AccountId;
        NetOutgoingMessage message = server.CreateMessage();
        bool written = InventoryStore.Read(d =>
        {
            InvAccess? a = InventoryStore.Resolve(d, characterId);
            if (a == null) return false;
            InventoryWire.WriteClientReplaceInventory(message, account, a);
            return true;
        });
        if (written && connection.Status == NetConnectionStatus.Connected)
            server.SendMessage(message, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    public static void SendCurrencies(NetServer server, NetConnection connection, Dictionary<string, int> currencies)
    {
        PilotSave pilot = Accounts.Pilot();
        Send(server, connection, m => InventoryWire.WriteClientUpdatePlayerCurrencies(m, pilot, currencies));
    }

    // Adds unlock items to the account (consumables, instant loot) and tells the client.
    public static List<string> AddUnlocks(NetServer server, NetConnection connection, IEnumerable<string> unlocks)
    {
        List<string> wanted = unlocks.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        List<string> added = Accounts.Change(d =>
        {
            var news = new List<string>();
            foreach (string unlock in wanted)
            {
                if (d.Pilot.Unlocks.Contains(unlock)) continue;
                d.Pilot.Unlocks.Add(unlock);
                news.Add(unlock);
            }
            return news;
        });
        if (added.Count > 0)
            Send(server, connection, m => ProgressionMessages.WriteClientUpdatePlayerDataUnlocks(m, PilotProgression.CurrentUnlocks()));
        return added;
    }

    private static void Chat(NetServer server, NetConnection connection, string text) => WorldObjects.SendChat(server, connection, text);
}
