using System.Runtime.InteropServices;
using Microsoft.Win32;
using static Hatoclip.NetworkListManagerInterop;

namespace Hatoclip;

public enum NetworkCategory
{
    Unknown,
    Public,
    Private,
    Domain,
}

public enum FirewallStatus
{
    /// <summary>Could not be determined (registry unreadable etc.).</summary>
    Unknown,

    /// <summary>An active inbound Allow rule covers this exe (or our port) for the current profile.</summary>
    Allowed,

    /// <summary>The firewall is turned off for the current profile.</summary>
    FirewallOff,

    /// <summary>No matching Allow rule was found.</summary>
    NotAllowed,

    /// <summary>An active inbound Block rule targets this exe.</summary>
    Blocked,
}

/// <summary>
/// Read-only network diagnostics for the QR window (SPEC 3.3): network category (Public/Private) via
/// the Network List Manager COM API, and a best-effort Windows Firewall rule check via the registry.
/// Never modifies any system setting; every failure degrades to "Unknown".
/// </summary>
public static class NetworkDiagnostics
{
    private const string FirewallPolicyKey = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";

    /// <summary>
    /// Category of the network on the given adapter (NetworkInterface.Id, "{GUID}"). If the adapter is
    /// unknown or not matched, returns the least-trusted category among all connected networks.
    /// </summary>
    public static NetworkCategory GetCategory(string? adapterId)
    {
        try
        {
            return GetCategoryCore(adapterId);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException or
                                       TypeLoadException or NotSupportedException or UnauthorizedAccessException)
        {
            Log.Warn($"Network category could not be read ({ex.GetType().Name}: {ex.Message}).");
            return NetworkCategory.Unknown;
        }
    }

    private static NetworkCategory GetCategoryCore(string? adapterId)
    {
        Guid? wanted = Guid.TryParse(adapterId, out var g) ? g : null;
        var manager = Create();
        NetworkCategory? matched = null;
        NetworkCategory? leastTrusted = null;
        try
        {
            var connections = manager.GetNetworkConnections();
            var buffer = new INetworkConnection[1];
            while (connections.Next(1, buffer, out var fetched) == 0 && fetched == 1)
            {
                var connection = buffer[0];
                try
                {
                    if (connection.IsConnected == 0)
                    {
                        continue;
                    }

                    var network = connection.GetNetwork();
                    NetworkCategory category;
                    try
                    {
                        category = Map(network.GetCategory());
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(network);
                    }

                    if (wanted is { } id && connection.GetAdapterId() == id)
                    {
                        matched = category;
                    }

                    if (leastTrusted is null || Trust(category) < Trust(leastTrusted.Value))
                    {
                        leastTrusted = category;
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(connection);
                }
            }

            Marshal.ReleaseComObject(connections);
        }
        finally
        {
            Marshal.ReleaseComObject(manager);
        }

        return matched ?? leastTrusted ?? NetworkCategory.Unknown;
    }

    private static NetworkCategory Map(NlmNetworkCategory category) => category switch
    {
        NlmNetworkCategory.Public => NetworkCategory.Public,
        NlmNetworkCategory.Private => NetworkCategory.Private,
        NlmNetworkCategory.DomainAuthenticated => NetworkCategory.Domain,
        _ => NetworkCategory.Unknown,
    };

    private static int Trust(NetworkCategory c) => c switch
    {
        NetworkCategory.Public => 0,
        NetworkCategory.Unknown => 1,
        _ => 2,
    };

    /// <summary>
    /// Best-effort check whether Windows Firewall lets inbound connections reach this exe on the given
    /// profile. Reads the local firewall rule store in the registry (read-only). Rules pushed by Group
    /// Policy or third-party firewalls are not visible here, hence "possibly" in the UI wording.
    /// </summary>
    public static FirewallStatus GetFirewallStatus(string exePath, int port, NetworkCategory category)
    {
        try
        {
            using var policy = Registry.LocalMachine.OpenSubKey(FirewallPolicyKey);
            if (policy is null)
            {
                return FirewallStatus.Unknown;
            }

            if (IsFirewallDisabled(policy, category))
            {
                return FirewallStatus.FirewallOff;
            }

            using var rules = policy.OpenSubKey("FirewallRules");
            if (rules is null)
            {
                return FirewallStatus.Unknown;
            }

            var profile = category switch
            {
                NetworkCategory.Public => "Public",
                NetworkCategory.Private => "Private",
                NetworkCategory.Domain => "Domain",
                _ => null,
            };

            var allowed = false;
            var blocked = false;
            foreach (var name in rules.GetValueNames())
            {
                if (rules.GetValue(name) is not string data)
                {
                    continue;
                }

                var rule = FirewallRule.Parse(data);
                if (!rule.IsActiveInbound || !rule.AppliesToProfile(profile) || !rule.AllowsTcp)
                {
                    continue;
                }

                var matchesApp = rule.App is { } app && PathsEqual(app, exePath);
                var matchesPort = rule.App is null && rule.MatchesLocalPort(port);
                if (rule.IsAllow && (matchesApp || matchesPort))
                {
                    allowed = true;
                }
                else if (rule.IsBlock && matchesApp)
                {
                    blocked = true;
                }
            }

            // Block rules win over Allow rules in Windows Firewall.
            return blocked ? FirewallStatus.Blocked : allowed ? FirewallStatus.Allowed : FirewallStatus.NotAllowed;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Firewall rules could not be read ({ex.GetType().Name}).");
            return FirewallStatus.Unknown;
        }
    }

    private static bool IsFirewallDisabled(RegistryKey policy, NetworkCategory category)
    {
        var profileKey = category switch
        {
            NetworkCategory.Public => "PublicProfile",
            NetworkCategory.Private => "StandardProfile",
            NetworkCategory.Domain => "DomainProfile",
            _ => null,
        };
        if (profileKey is null)
        {
            return false;
        }

        using var key = policy.OpenSubKey(profileKey);
        return key?.GetValue("EnableFirewall") is int enabled && enabled == 0;
    }

    private static bool PathsEqual(string rulePath, string exePath)
    {
        try
        {
            var a = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rulePath));
            var b = Path.GetFullPath(exePath);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>Parser for the "v2.x|Action=Allow|Active=TRUE|Dir=In|..." rule strings.</summary>
    private sealed class FirewallRule
    {
        private readonly Dictionary<string, List<string>> _fields = new(StringComparer.OrdinalIgnoreCase);

        public static FirewallRule Parse(string data)
        {
            var rule = new FirewallRule();
            foreach (var part in data.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                var name = part[..eq];
                if (!rule._fields.TryGetValue(name, out var list))
                {
                    rule._fields[name] = list = [];
                }

                list.Add(part[(eq + 1)..]);
            }

            return rule;
        }

        public string? App => First("App");

        public bool IsAllow => Is("Action", "Allow");

        public bool IsBlock => Is("Action", "Block");

        public bool IsActiveInbound => Is("Active", "TRUE") && Is("Dir", "In");

        /// <summary>Protocol 6 = TCP; no Protocol field = any protocol.</summary>
        public bool AllowsTcp => First("Protocol") is not { } p || p == "6";

        /// <summary>No Profile field = all profiles. Unknown current profile matches any rule.</summary>
        public bool AppliesToProfile(string? profile) =>
            profile is null || !_fields.TryGetValue("Profile", out var profiles) ||
            profiles.Any(p => string.Equals(p, profile, StringComparison.OrdinalIgnoreCase));

        public bool MatchesLocalPort(int port)
        {
            if (!_fields.TryGetValue("LPort", out var ports))
            {
                return false;
            }

            foreach (var spec in ports.SelectMany(p => p.Split(',')))
            {
                var dash = spec.IndexOf('-');
                if (dash > 0 &&
                    int.TryParse(spec[..dash], out var lo) && int.TryParse(spec[(dash + 1)..], out var hi) &&
                    port >= lo && port <= hi)
                {
                    return true;
                }

                if (int.TryParse(spec, out var single) && single == port)
                {
                    return true;
                }
            }

            return false;
        }

        private string? First(string name) => _fields.TryGetValue(name, out var list) ? list[0] : null;

        private bool Is(string name, string value) =>
            string.Equals(First(name), value, StringComparison.OrdinalIgnoreCase);
    }
}
