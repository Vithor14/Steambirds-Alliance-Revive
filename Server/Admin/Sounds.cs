using Lidgren.Network;

// Sounds sent by the server: ClientPlayAudio `7335` carries the name of a sound group of the game's audio bank (MasterAudio);
// the client plays it once ("Audio.PlaySoundAndForget"). A name the game does not have plays nothing.
// The names below are the ones the client's own code uses (found in the decompiled client); /sound NAME also takes any other name
// (the plugin's audio export lists the rest when the game's bank is dumped).
// Map scripts use it with the action "sound".
public static class Sounds
{
    public const ushort MsgClientPlayAudio = 7335;

    public static readonly string[] Known =
    {
        "Ui back", "Ui activate selection", "Ui options_open", "Ui options_close",
        "Inventory Open", "Inventory Close", "Player Drop Item", "Player Swap Item", "Player Equip",
        "Pickup Lower Tier", "Pickup Current Tier", "Pickup Higher Tier", "Pickup Full",
        "Out of steam", "No Ammo", "Player Low Health Warning",
        "Teleport - Rebel City", "Teleport - Another Player",
        "Stinger Overworld Enter New Area", "Stinger Mission Objective", "New mission pop up appear",
        "Chat Message", "Chat Speech Bubble"
    };

    public static void Play(NetServer server, NetConnection connection, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || connection.Status != NetConnectionStatus.Connected) return;
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgClientPlayAudio);
        m.Write(name);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    // A name from the list by its number (1-based) or part of its text; otherwise the text as it is.
    public static string Resolve(string text)
    {
        text = text.Trim();
        if (int.TryParse(text, out int number) && number >= 1 && number <= Known.Length) return Known[number - 1];
        foreach (string known in Known)
            if (string.Equals(known, text, StringComparison.OrdinalIgnoreCase)) return known;
        return text;
    }

    public static string List() => string.Join(", ", Known.Select((s, i) => (i + 1) + "=" + s));
}
