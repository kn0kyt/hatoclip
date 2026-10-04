using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Hatoclip;

/// <summary>An IPv4 address on a usable (non-virtual, Up) network interface.</summary>
public sealed record LanCandidate(
    IPAddress Address,
    string InterfaceId,
    string InterfaceName,
    string Description,
    NetworkInterfaceType Type,
    bool HasGateway,
    bool IsPhysicalType,
    bool IsBestRoute)
{
    public override string ToString() =>
        $"{Address} on \"{InterfaceName}\" ({Type}, gateway={HasGateway}, bestRoute={IsBestRoute})";
}

/// <summary>Result of LAN address discovery: the chosen candidate plus diagnostics.</summary>
public sealed record LanSelection(LanCandidate? Best, IReadOnlyList<LanCandidate> Candidates, IReadOnlyList<string> ExcludedInterfaces)
{
    public bool HasAddress => Best is not null;
}

/// <summary>LAN IPv4 discovery for the connection URL (selection rules: docs/design.md).</summary>
public static class NetworkInfo
{
    /// <summary>
    /// Interfaces whose name or description contains any of these (case-insensitive) are treated as
    /// virtual / VPN adapters and never offered as the connection address. Extend as needed.
    /// </summary>
    public static readonly string[] VirtualAdapterKeywords =
    [
        "Hyper-V", "vEthernet", "WSL", "VirtualBox", "VMware", "TAP", "Tailscale", "ZeroTier",
        "Wintun", "WireGuard", "OpenVPN", "Bluetooth", "Npcap", "Loopback", "Wi-Fi Direct",
    ];

    /// <summary>
    /// The machine's mDNS name, e.g. "my-pc.local". Uses the DNS host name: MachineName is the NetBIOS
    /// name, which is truncated to 15 characters.
    /// </summary>
    public static string LocalHostName => GetHostName().ToLowerInvariant() + ".local";

    /// <summary>Picks the best LAN IPv4: has gateway &gt; physical type (Ethernet/Wi-Fi) &gt; preferred by the routing table.</summary>
    public static LanSelection SelectLanAddress()
    {
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException ex)
        {
            Log.Warn($"Could not enumerate network interfaces ({ex.Message}).");
            return new LanSelection(null, [], []);
        }

        var bestRouteIndex = GetBestRouteInterfaceIndex();
        var candidates = new List<LanCandidate>();
        var excluded = new List<string>();

        foreach (var nic in interfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp)
            {
                continue;
            }

            if (IsVirtualAdapter(nic))
            {
                excluded.Add(nic.Name);
                continue;
            }

            IPInterfaceProperties props;
            try
            {
                props = nic.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            var address = props.UnicastAddresses
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && !IsLinkLocal(a));
            if (address is null)
            {
                continue;
            }

            var hasGateway = props.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));

            var isBestRoute = false;
            if (bestRouteIndex is { } best)
            {
                try
                {
                    isBestRoute = props.GetIPv4Properties()?.Index == best;
                }
                catch (NetworkInformationException)
                {
                }
            }

            candidates.Add(new LanCandidate(address, nic.Id, nic.Name, nic.Description, nic.NetworkInterfaceType,
                hasGateway, IsPhysicalType(nic.NetworkInterfaceType), isBestRoute));
        }

        var ordered = OrderCandidates(candidates);
        return new LanSelection(ordered.FirstOrDefault(), ordered, excluded);
    }

    /// <summary>Best first: has gateway &gt; physical type &gt; preferred by the routing table (stable otherwise).</summary>
    internal static List<LanCandidate> OrderCandidates(IEnumerable<LanCandidate> candidates) =>
        candidates
            .OrderByDescending(c => c.HasGateway)
            .ThenByDescending(c => c.IsPhysicalType)
            .ThenByDescending(c => c.IsBestRoute)
            .ToList();

    private static string GetHostName()
    {
        try
        {
            var name = Dns.GetHostName();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch (SocketException ex)
        {
            Log.Warn($"Could not get the host name ({ex.SocketErrorCode}); using the NetBIOS name.");
        }

        return Environment.MachineName;
    }

    public static string BuildConnectUrl(string host, int port, string key) => $"http://{host}:{port}/#k={key}";

    private static bool IsVirtualAdapter(NetworkInterface nic) => IsVirtualAdapter(nic.Name, nic.Description);

    /// <summary>True if the adapter name or description contains a <see cref="VirtualAdapterKeywords"/> entry.</summary>
    internal static bool IsVirtualAdapter(string name, string description) =>
        VirtualAdapterKeywords.Any(k =>
            name.Contains(k, StringComparison.OrdinalIgnoreCase) ||
            description.Contains(k, StringComparison.OrdinalIgnoreCase));

    internal static bool IsPhysicalType(NetworkInterfaceType type) => type is
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet or
        NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit;

    private static bool IsLinkLocal(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b[0] == 169 && b[1] == 254;
    }

    /// <summary>
    /// Interface index the routing table would use for an off-link IPv4 destination (i.e. the default
    /// route with the lowest metric). This is a local route-table lookup; no packet is sent.
    /// .NET does not expose interface metrics directly, so this stands in for "lowest metric".
    /// </summary>
    private static int? GetBestRouteInterfaceIndex()
    {
        try
        {
            // 1.1.1.1 in network byte order, as a uint read little-endian.
            var dest = BitConverter.ToUInt32([1, 1, 1, 1], 0);
            return GetBestInterface(dest, out var index) == 0 ? (int)index : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int GetBestInterface(uint dwDestAddr, out uint pdwBestIfIndex);
}
