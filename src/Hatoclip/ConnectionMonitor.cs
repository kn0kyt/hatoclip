using System.Net.NetworkInformation;

namespace Hatoclip;

/// <summary>Everything the QR window and tray tooltip show. Value equality = "nothing changed".</summary>
public sealed record ConnectionSnapshot(
    string? LanIp,
    string? InterfaceName,
    string LocalHostName,
    int Port,
    string Key,
    NetworkCategory Category,
    FirewallStatus Firewall)
{
    public bool HasLanIp => LanIp is not null;

    public string? IpUrl => LanIp is null ? null : NetworkInfo.BuildConnectUrl(LanIp, Port, Key);

    public string LocalUrl => NetworkInfo.BuildConnectUrl(LocalHostName, Port, Key);

    /// <summary>URL encoded in the QR: the IP version when available, otherwise the .local version.</summary>
    public string QrUrl => IpUrl ?? LocalUrl;

    /// <summary>Base URL without the key (safe for tooltips and logs).</summary>
    public string BaseUrl => $"http://{LanIp ?? LocalHostName}:{Port}";

    // Exclude the key from the generated ToString so a snapshot can never leak it into a log.
    public override string ToString() =>
        $"ip={LanIp ?? "(none)"}, interface={InterfaceName ?? "(none)"}, port={Port}, category={Category}, firewall={Firewall}";
}

/// <summary>
/// Tracks the connection URL and network diagnostics. Recomputes on network change events and every
/// <see cref="PollInterval"/> as a fallback, and raises <see cref="Changed"/> on the UI thread when the
/// IP, port, key or diagnostics differ from the previous snapshot.
/// </summary>
public sealed class ConnectionMonitor : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly ConfigStore _config;
    private readonly Func<int> _port;
    private readonly UiDispatcher _ui;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly object _refreshGate = new();
    private bool _refreshRunning;
    private bool _refreshPending;
    private bool _disposed;

    /// <summary>Must be constructed on the UI thread.</summary>
    public ConnectionMonitor(ConfigStore config, Func<int> port, UiDispatcher ui)
    {
        _config = config;
        _port = port;
        _ui = ui;
        Current = Compute(logDetails: true);
        Log.Info($"Connection: {Current}.");

        _pollTimer = new System.Windows.Forms.Timer { Interval = (int)PollInterval.TotalMilliseconds };
        _pollTimer.Tick += (_, _) => RequestRefresh();
        _pollTimer.Start();

        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
    }

    /// <summary>Raised on the UI thread with the new snapshot.</summary>
    public event EventHandler<ConnectionSnapshot>? Changed;

    /// <summary>Latest snapshot. Read on the UI thread.</summary>
    public ConnectionSnapshot Current { get; private set; }

    /// <summary>Recomputes in the background; coalesces concurrent requests. Safe from any thread.</summary>
    public void RequestRefresh()
    {
        lock (_refreshGate)
        {
            if (_disposed)
            {
                return;
            }

            if (_refreshRunning)
            {
                _refreshPending = true;
                return;
            }

            _refreshRunning = true;
        }

        Task.Run(RefreshLoop);
    }

    public void Dispose()
    {
        lock (_refreshGate)
        {
            _disposed = true;
        }

        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        _pollTimer.Dispose();
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => RequestRefresh();

    private void RefreshLoop()
    {
        while (true)
        {
            ConnectionSnapshot? snapshot = null;
            try
            {
                snapshot = Compute(logDetails: false);
            }
            catch (Exception ex)
            {
                Log.Error("Connection refresh failed.", ex);
            }

            if (snapshot is not null)
            {
                _ui.Post(() => Apply(snapshot));
            }

            lock (_refreshGate)
            {
                if (!_refreshPending || _disposed)
                {
                    _refreshRunning = false;
                    return;
                }

                _refreshPending = false;
            }
        }
    }

    private void Apply(ConnectionSnapshot snapshot)
    {
        if (_disposed || snapshot == Current)
        {
            return;
        }

        var keyChanged = snapshot.Key != Current.Key;
        Current = snapshot;
        Log.Info($"Connection changed: {snapshot}{(keyChanged ? ", key=(regenerated)" : "")}.");
        Changed?.Invoke(this, snapshot);
    }

    private ConnectionSnapshot Compute(bool logDetails)
    {
        var selection = NetworkInfo.SelectLanAddress();
        if (logDetails || !selection.HasAddress)
        {
            Log.Info($"LAN candidates: [{string.Join("; ", selection.Candidates)}]; " +
                     $"excluded virtual adapters: [{string.Join(", ", selection.ExcludedInterfaces)}].");
        }

        var port = _port();
        var category = NetworkDiagnostics.GetCategory(selection.Best?.InterfaceId);
        var firewall = NetworkDiagnostics.GetFirewallStatus(AutoStart.CurrentExePath, port, category);
        return new ConnectionSnapshot(
            selection.Best?.Address.ToString(),
            selection.Best?.InterfaceName,
            NetworkInfo.LocalHostName,
            port,
            _config.Key,
            category,
            firewall);
    }
}
