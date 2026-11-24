using System.Globalization;
using Lidgren.Network;

// Player debug settings of the game (GameServer.DebugSetting). The client keeps them in its own copy of the game server and
// reads them while it runs the player's plane: health, speed, turn rate, steam, damage, damage received (0 = nothing hurts) and
// "epic" (1 = the plane survives with 1 hit point). The server sends them with ClientSetFloatPlayerDebugSetting `27395` /
// ClientSetStringPlayerDebugSetting `58246` and takes them off with ClientClearPlayerDebugSetting `6848`.
// ClientSetDebugSetting `3732` is the same for the three on/off switches of the whole game (spawning, enemyMovement, debugInfo).
//
// They come from the chat (/debug, /god) or from the client's own ServerSet...PlayerDebugSetting messages `49275`, `15630`
// and ServerClearPlayerDebugSetting `30792`. Who can use them follows network.json "chatCommands". Nothing is saved: they end with the connection.
public static class DebugSettings
{
    public const ushort MsgClientSetDebugSetting = 3732;
    public const ushort MsgClientClearPlayerDebugSetting = 6848;
    public const ushort MsgClientSetFloatPlayerDebugSetting = 27395;
    public const ushort MsgClientSetStringPlayerDebugSetting = 58246;
    public const ushort MsgClientSimulateLatency = 38607;

    // GameServer.DebugSetting values and what each one does on the client.
    public static readonly (int Index, string Name, bool Switch, string Help)[] All =
    {
        (0, "spawning", true, "on/off: the game's own enemy spawning (the server owns the enemies here)"),
        (1, "damage", false, "multiplier of the damage you deal (1 = normal)"),
        (2, "damageReceivedMultiplier", false, "multiplier of the damage you take (0 = nothing hurts)"),
        (3, "speed", false, "plane speed"),
        (4, "steam", false, "steam (energy) amount"),
        (5, "turnRate", false, "plane turn rate"),
        (7, "cloud", false, "cloud setting"),
        (8, "gun", false, "gun setting"),
        (9, "enemyMovement", true, "on/off: enemy movement in the client's own simulation"),
        (10, "health", false, "plane health"),
        (11, "debugInfo", true, "on/off: debug information"),
        (12, "epic", false, "1 = the plane survives with 1 hit point")
    };

    private static readonly object gate = new object();
    private static readonly Dictionary<long, Dictionary<int, float>> floats = new Dictionary<long, Dictionary<int, float>>();
    private static readonly Dictionary<long, Dictionary<int, string>> strings = new Dictionary<long, Dictionary<int, string>>();

    public static (int Index, string Name, bool Switch, string Help)? Find(string text)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            foreach (var s in All) if (s.Index == index) return s;
        foreach (var s in All)
            if (string.Equals(s.Name, text, StringComparison.OrdinalIgnoreCase)) return s;
        // Short forms: "damage_received", "received", "turn".
        foreach (var s in All)
            if (s.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase) || s.Name.Replace("Multiplier", "").Contains(text, StringComparison.OrdinalIgnoreCase)) return s;
        return null;
    }

    public static string NameOf(int index)
    {
        foreach (var s in All) if (s.Index == index) return s.Name;
        return "setting " + index;
    }

    public static void SetFloat(NetServer server, NetConnection connection, int index, float value)
    {
        lock (gate)
        {
            if (!floats.TryGetValue(connection.RemoteUniqueIdentifier, out Dictionary<int, float>? set)) floats[connection.RemoteUniqueIdentifier] = set = new Dictionary<int, float>();
            set[index] = value;
        }
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgClientSetFloatPlayerDebugSetting);
        m.Write(index);
        m.Write(value);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    public static void SetString(NetServer server, NetConnection connection, int index, string value)
    {
        lock (gate)
        {
            if (!strings.TryGetValue(connection.RemoteUniqueIdentifier, out Dictionary<int, string>? set)) strings[connection.RemoteUniqueIdentifier] = set = new Dictionary<int, string>();
            set[index] = value;
        }
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgClientSetStringPlayerDebugSetting);
        m.Write(index);
        m.Write(value);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    public static void SetSwitch(NetServer server, NetConnection connection, int index, bool on)
    {
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgClientSetDebugSetting);
        m.Write(index);
        m.Write(on);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    public static void Clear(NetServer server, NetConnection connection, int index)
    {
        lock (gate)
        {
            if (floats.TryGetValue(connection.RemoteUniqueIdentifier, out Dictionary<int, float>? f)) f.Remove(index);
            if (strings.TryGetValue(connection.RemoteUniqueIdentifier, out Dictionary<int, string>? s)) s.Remove(index);
        }
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgClientClearPlayerDebugSetting);
        m.Write((byte)index);
        m.Write(connection.RemoteUniqueIdentifier);   // PeerId = the client's Lidgren unique id
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    public static int ClearAll(NetServer server, NetConnection connection)
    {
        List<int> indexes;
        lock (gate)
        {
            indexes = new List<int>();
            if (floats.TryGetValue(connection.RemoteUniqueIdentifier, out Dictionary<int, float>? f)) indexes.AddRange(f.Keys);
            if (strings.TryGetValue(connection.RemoteUniqueIdentifier, out Dictionary<int, string>? s)) indexes.AddRange(s.Keys);
        }
        foreach (int index in indexes.Distinct()) Clear(server, connection, index);
        return indexes.Distinct().Count();
    }

    public static void SimulateLatency(NetServer server, NetConnection connection, bool on)
    {
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgClientSimulateLatency);
        m.Write(on);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    public static string Describe(NetConnection connection)
    {
        lock (gate)
        {
            var parts = new List<string>();
            if (floats.TryGetValue(connection.RemoteUniqueIdentifier, out Dictionary<int, float>? f))
                parts.AddRange(f.Select(p => NameOf(p.Key) + " = " + p.Value.ToString("0.###", CultureInfo.InvariantCulture)));
            if (strings.TryGetValue(connection.RemoteUniqueIdentifier, out Dictionary<int, string>? s))
                parts.AddRange(s.Select(p => NameOf(p.Key) + " = \"" + p.Value + "\""));
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }
    }

    public static void Disconnected(NetConnection? connection)
    {
        if (connection == null) return;
        lock (gate)
        {
            floats.Remove(connection.RemoteUniqueIdentifier);
            strings.Remove(connection.RemoteUniqueIdentifier);
        }
    }
}
