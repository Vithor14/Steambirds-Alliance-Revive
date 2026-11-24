namespace SteambirdsLauncher.Core;

// Ports of the map test server (SteambirdsServer.exe --test-map, started by the map editor's "Play test"). They are the
// ones in Server\Dungeons\MapTest.cs and differ from the normal server's, so both can run at the same time.
public static class MapTestPorts
{
    public const int Http = 6543;   // OZY_URL = http://localhost:6543/ozy
    public const int Nexus = 1511;  // only the login passes through it: the server sends the game straight to the map
}
