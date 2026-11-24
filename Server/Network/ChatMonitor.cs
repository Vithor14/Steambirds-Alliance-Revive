// Feeds the "Chat" page of the launcher's Server tab. Every chat line the server relays (nearby chat, whispers, flock chat)
// is also printed as one structured line that the launcher reads and groups by map:
//
//   HH:mm:ss.fff [CHATMON] channel TAB room TAB mapLabel TAB sender TAB target TAB text
//
// channel: "map" (nearby chat of a map), "whisper" or "flock". room: stable key of the map instance ("nexus", "world",
// "dungeon:3"). target: the recipient of a whisper or the flock name; empty for the nearby chat.
// The launcher keeps these lines out of its console (the readable "[CHAT]" line stays there) but they remain in the log file.
public static class ChatMonitor
{
    public const string Tag = "CHATMON";

    public static void Emit(Action<string, string> log, string channel, string room, string mapLabel, string sender, string target, string text)
    {
        log(Tag, string.Join('\t', channel, Clean(room), Clean(mapLabel), Clean(sender), Clean(target), Clean(text)));
    }

    // Where a player is right now: the key and the readable name of the map they chat in (the tutorial is private and has no room).
    public static void MapOf(Lidgren.Network.NetConnection connection, out string room, out string label)
    {
        room = PlayerRelay.RoomOf(connection) ?? "tutorial";
        label = room == "tutorial" ? "Tutorial" : LabelOfRoom(room);
    }

    // Readable name of a relay room.
    public static string LabelOfRoom(string room)
    {
        if (room == PlayerRelay.NexusRoom) return "Rebel City (Nexus)";
        if (room == WorldServer.RelayRoom) return "Open world";
        if (room.StartsWith("dungeon:", StringComparison.Ordinal)) return DungeonServer.LabelOf(room) ?? "Dungeon";
        return room;
    }

    private static string Clean(string? text) =>
        (text ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
