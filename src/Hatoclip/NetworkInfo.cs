using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Hatoclip;

/// <summary>
/// LAN address discovery. Phase 1: basic pick. Phase 2 will add virtual-adapter filtering,
/// change notifications and network diagnostics here.
/// </summary>
public static class NetworkInfo
{
    /// <summary>First non-loopback IPv4 of an Up interface that has an IPv4 default gateway (falls back to any Up IPv4).</summary>
    public static IPAddress? GetLanIPv4()
    {
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return null;
        }

        IPAddress? fallback = null;
        foreach (var nic in interfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var props = nic.GetIPProperties();
            var address = props.UnicastAddresses
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && !IsLinkLocal(a));
            if (address is null)
            {
                continue;
            }

            var hasGateway = props.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            if (hasGateway)
            {
                return address;
            }

            fallback ??= address;
        }

        return fallback;
    }

    public static string BuildConnectUrl(IPAddress? ip, int port, string key)
    {
        var host = ip?.ToString() ?? Environment.MachineName + ".local";
        return $"http://{host}:{port}/#k={key}";
    }

    private static bool IsLinkLocal(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b[0] == 169 && b[1] == 254;
    }
}
