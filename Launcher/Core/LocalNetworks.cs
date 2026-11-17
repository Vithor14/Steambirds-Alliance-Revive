using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SteambirdsLauncher.Core;

// The IPv4 networks of this PC (cable/Wi-Fi and VPNs like Radmin VPN, Hamachi, ZeroTier, Tailscale): where the server
// search goes, and which addresses the host gives to the other players.
public sealed class LocalNetwork
{
    public string Name { get; init; } = "";        // name of the adapter in Windows
    public string Kind { get; init; } = "";        // "Radmin VPN", "Hamachi", "Wi-Fi", "LAN"...
    public bool IsVpn { get; init; }
    public IPAddress Address { get; init; } = IPAddress.None;
    public IPAddress Mask { get; init; } = IPAddress.None;
    public int PrefixLength { get; init; }

    // Directed broadcast of the network (192.168.0.255 for 192.168.0.10/24).
    public IPAddress Broadcast
    {
        get
        {
            uint ip = ToUInt(Address);
            uint mask = ToUInt(Mask);
            return FromUInt(ip | ~mask);
        }
    }

    public bool Contains(IPAddress other)
    {
        if (other.AddressFamily != AddressFamily.InterNetwork) return false;
        uint mask = ToUInt(Mask);
        return (ToUInt(other) & mask) == (ToUInt(Address) & mask);
    }

    public override string ToString() => Kind + " (" + Address + ")";

    public static uint ToUInt(IPAddress address)
    {
        byte[] b = address.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    public static IPAddress FromUInt(uint value) =>
        new IPAddress(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });
}

public static class LocalNetworks
{
    // Networks that are up, with an IPv4 address (no loopback).
    public static List<LocalNetwork> List()
    {
        var result = new List<LocalNetwork>();
        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch
        {
            return result;
        }

        foreach (NetworkInterface nic in adapters)
        {
            try
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                (string kind, bool vpn) = Classify(nic);
                foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(info.Address)) continue;
                    int prefix = info.PrefixLength;
                    if (prefix <= 0 || prefix > 32)
                    {
                        prefix = 24;
                        try
                        {
                            if (info.IPv4Mask != null && !info.IPv4Mask.Equals(IPAddress.Any)) prefix = CountBits(info.IPv4Mask);
                        }
                        catch
                        {
                        }
                    }
                    uint mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
                    result.Add(new LocalNetwork
                    {
                        Name = nic.Name,
                        Kind = kind,
                        IsVpn = vpn,
                        Address = info.Address,
                        Mask = LocalNetwork.FromUInt(mask),
                        PrefixLength = prefix
                    });
                }
            }
            catch
            {
                // An adapter that cannot be read is skipped.
            }
        }

        // VPNs used for games first, then cable/Wi-Fi, then the rest.
        return result.OrderBy(n => n.IsVpn ? 0 : 1).ThenBy(n => n.Kind, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (string Kind, bool Vpn) Classify(NetworkInterface nic)
    {
        string text = (nic.Description + " " + nic.Name).ToLowerInvariant();
        if (text.Contains("radmin")) return ("Radmin VPN", true);
        if (text.Contains("hamachi")) return ("Hamachi", true);
        if (text.Contains("zerotier")) return ("ZeroTier", true);
        if (text.Contains("tailscale")) return ("Tailscale", true);
        if (text.Contains("wireguard")) return ("WireGuard", true);
        if (text.Contains("openvpn") || text.Contains("tap-windows") || text.Contains(" vpn") || text.Contains("vpn ")) return ("VPN", true);
        if (text.Contains("vethernet") || text.Contains("hyper-v") || text.Contains("virtualbox") || text.Contains("vmware") || text.Contains("wsl"))
            return (L.T("virtual network"), false);
        if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return ("Wi-Fi", false);
        return (L.T("local network"), false);
    }

    private static int CountBits(IPAddress mask)
    {
        uint value = LocalNetwork.ToUInt(mask);
        int bits = 0;
        while (value != 0)
        {
            bits += (int)(value & 1);
            value >>= 1;
        }
        return bits;
    }

    // ---------------------------------------------------------------- ARP table
    // Addresses this PC talked to recently (other PCs of the LAN and of the VPNs): the search also asks them directly,
    // for networks that do not pass broadcasts.

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetIpNetTable(IntPtr table, ref int size, bool order);

    public static List<IPAddress> Neighbors()
    {
        var result = new List<IPAddress>();
        int size = 0;
        try
        {
            GetIpNetTable(IntPtr.Zero, ref size, false);
            if (size <= 0) return result;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetIpNetTable(buffer, ref size, false) != 0) return result;
                int count = Marshal.ReadInt32(buffer);
                // MIB_IPNETROW: index (4), physical address length (4), physical address (8), address (4), type (4).
                const int rowSize = 24;
                for (int i = 0; i < count; i++)
                {
                    IntPtr row = buffer + 4 + i * rowSize;
                    int type = Marshal.ReadInt32(row, 20);
                    if (type == 2) continue; // invalid
                    uint raw = unchecked((uint)Marshal.ReadInt32(row, 16));
                    var address = new IPAddress(new[] { (byte)raw, (byte)(raw >> 8), (byte)(raw >> 16), (byte)(raw >> 24) });
                    byte first = address.GetAddressBytes()[0];
                    if (first == 0 || first >= 224 || address.Equals(IPAddress.Broadcast)) continue;
                    result.Add(address);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // Without the table the search uses only broadcasts.
        }
        return result.Distinct().ToList();
    }
}
