using System.Diagnostics;
using System.Net.NetworkInformation;

namespace SteambirdsLauncher.Core;

// Port checks: is a server (this launcher's, or the "Run server.ps1" one) already listening on this PC?
public static class NetworkCheck
{
    public static bool IsTcpListening(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == port);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsUdpBound(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(e => e.Port == port);
        }
        catch
        {
            return false;
        }
    }

    // The HTTP port answers the game's login: if it is open, some server is running on this PC.
    // The ports come from server\network.json (Server tab > Network and multiplayer).
    public static bool AnyServerListening() => IsTcpListening(ServerNetworkFile.Read().HttpPort);

    // Text describing the first server port that is already taken, or null if all are free.
    public static string? FirstBusyServerPort()
    {
        ServerNetworkFile ports = ServerNetworkFile.Read();
        if (IsTcpListening(ports.HttpPort)) return L.F("TCP port {0} (HTTP)", ports.HttpPort);
        if (IsUdpBound(ports.NexusPort)) return L.F("UDP port {0} (Nexus)", ports.NexusPort);
        if (IsUdpBound(ports.TutorialPort)) return L.F("UDP port {0} (tutorial)", ports.TutorialPort);
        if (IsUdpBound(ports.DungeonPort)) return L.F("UDP port {0} (dungeon)", ports.DungeonPort);
        return null;
    }

    public sealed class ServerProcessInfo
    {
        public int Id { get; init; }
        public string Path { get; init; } = "";
        public bool IsLauncherServer { get; init; }
    }

    // Other Server processes: one left open by a launcher that crashed, or the one from "dotnet run".
    public static List<ServerProcessInfo> FindServerProcesses(int? ignoreId)
    {
        var list = new List<ServerProcessInfo>();
        string ours = System.IO.Path.GetFullPath(LauncherPaths.ServerExe);
        foreach (Process p in Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(LauncherPaths.ServerExeName)))
        {
            try
            {
                if (ignoreId.HasValue && p.Id == ignoreId.Value) continue;
                string path = "";
                try
                {
                    path = p.MainModule?.FileName ?? "";
                }
                catch
                {
                }
                list.Add(new ServerProcessInfo
                {
                    Id = p.Id,
                    Path = path,
                    IsLauncherServer = path.Length > 0 && string.Equals(System.IO.Path.GetFullPath(path), ours, StringComparison.OrdinalIgnoreCase)
                });
            }
            finally
            {
                p.Dispose();
            }
        }
        return list;
    }

    public static bool KillProcess(int id, out string error)
    {
        error = "";
        try
        {
            using Process p = Process.GetProcessById(id);
            p.Kill(entireProcessTree: true);
            p.WaitForExit(5000);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }
}
