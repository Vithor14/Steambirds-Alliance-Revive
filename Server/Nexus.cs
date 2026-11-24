using Lidgren.Network;

// Nexus = Rebel City, the Open Beta city (client 201219.1413).
//
// The map is in maps\nexus.json (format in maps\README.md): runway pieces in each slot, decor with
// exact position (runway ends, city, hills, rocks under the island), the buildings and the spawn point.
// The server reloads the file when it changes (applies to the next player who joins).
//
// How the plan was rebuilt (Open Beta videos, the recreation seen from above, the arrow texts on the floor of the
// ClientMapInfo and the game's meshes) is in the file itself and in docs/nexus-rebel-city.md in the project.
public static class Nexus
{
    // Map file of the Nexus: "nexusMap" of dungeons.json ("nexus" = the original Rebel City).
    public static string MapId => DungeonCatalog.NexusMapId;

    // shortId of the first building (the others follow the order of the file).
    private const int FirstShortId = 70001;

    private static Action<string, string> log = (_, _) => { };

    // Called once when the server starts: loads the map (an error here shows up in the log right away).
    public static void Start(Action<string, string> log)
    {
        Nexus.log = log;
        Maps.Get(MapId, log);
    }

    public static Maps.Map Map => Maps.Get(MapId, log);

    public static void SpawnPoint(out float x, out float y, out float rotation)
    {
        Maps.Map map = Map;
        if (!map.HasSpawn)
        {
            x = 0f;
            y = 0f;
            rotation = 0f;
            return;
        }
        map.Spawn.World(out x, out y);
        rotation = map.Spawn.Rotation;
    }

    // ClientReceiveTerrainTiles with the floor and the decor. Returns the size in bytes and the summary for the log.
    public static int SendFloor(NetServer server, NetConnection connection, out string summary)
    {
        Maps.Map map = Map;
        NetOutgoingMessage m = server.CreateMessage();
        map.WriteFloor(m, null, out string details);
        int bytes = m.LengthBytes;
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
        summary = map.Name + ": " + details;
        return bytes;
    }

    // The buildings as server entities (each one with the art, the dock and the text with an arrow on the floor).
    public static void SendBuildings(NetServer server, NetConnection connection, double now, Action<string, string> log)
    {
        Maps.Map map = Map;
        int shortId = FirstShortId;
        foreach (Maps.Building p in map.Buildings)
        {
            p.Point.World(out float x, out float y);
            WorldObjects.SendBuilding(server, connection, shortId++, p.Prefab, x, y, p.Point.Rotation, p.Store, now);
        }
        log("NEXUS", $"[nexus] {map.Buildings.Count} buildings of {map.Name} sent: " +
            string.Join(", ", map.Buildings.Select(p => p.Name)));
    }

    public static string Name => Map.Name;
}
