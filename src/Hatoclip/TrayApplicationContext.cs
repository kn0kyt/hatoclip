namespace Hatoclip;

/// <summary>Task-tray UI: NotifyIcon, context menu, receive notifications and the QR window.</summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private const int PreviewLength = 40;
    private const int MaxTooltipLength = 63;
    private static readonly TimeSpan ReceivedIconDuration = TimeSpan.FromSeconds(3);

    private readonly ConfigStore _config;
    private readonly HatoclipServer _server;
    private readonly UiDispatcher _ui;
    private readonly ConnectionMonitor _monitor;
    private readonly KeyRegenerator _keyRegenerator;
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _idleIcon;
    private readonly Icon _receivedIcon;
    private readonly System.Windows.Forms.Timer _receivedIconTimer;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _allowGetItem;
    private readonly Font _boldMenuFont;
    private QrWindow? _qrWindow;

    public TrayApplicationContext(ConfigStore config, HatoclipServer server, UiDispatcher ui, AuthRateLimiter rateLimiter)
    {
        _config = config;
        _server = server;
        _ui = ui;
        _monitor = new ConnectionMonitor(config, () => server.Port, ui);
        _keyRegenerator = new KeyRegenerator(config, rateLimiter, _monitor);

        var menu = new ContextMenuStrip();
        _boldMenuFont = new Font(menu.Font, FontStyle.Bold);
        var showQrItem = new ToolStripMenuItem("接続用QRコードを表示", null, (_, _) => ShowQrWindow()) { Font = _boldMenuFont };
        _autoStartItem = new ToolStripMenuItem("Windows起動時に自動起動", null, (_, _) => ToggleAutoStart());
        _allowGetItem = new ToolStripMenuItem("スマホからの取得を許可")
        {
            CheckOnClick = true,
            Checked = _config.Current.AllowGet,
        };
        _allowGetItem.CheckedChanged += (_, _) => ToggleAllowGet(_allowGetItem.Checked);
        var regenerateItem = new ToolStripMenuItem("接続キーを再生成...", null, (_, _) => RegenerateKey(_qrWindow is { Visible: true } w ? w : null));

        menu.Items.Add(showQrItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(_allowGetItem);
        menu.Items.Add(regenerateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitThread());
        menu.Opening += (_, _) => _autoStartItem.Checked = SafeIsAutoStartEnabled();

        _idleIcon = TrayIcons.LoadIdle();
        _receivedIcon = TrayIcons.LoadReceived();
        _receivedIconTimer = new System.Windows.Forms.Timer { Interval = (int)ReceivedIconDuration.TotalMilliseconds };
        _receivedIconTimer.Tick += (_, _) =>
        {
            _receivedIconTimer.Stop();
            _notifyIcon!.Icon = _idleIcon;
        };

        _notifyIcon = new NotifyIcon
        {
            Icon = _idleIcon,
            Text = BuildTooltip(_monitor.Current),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowQrWindow();

        _monitor.Changed += OnConnectionChanged;
        _server.TextReceived += OnTextReceived;

        if (AutoStart.IsStale())
        {
            Log.Warn("Autostart entry points to a different exe path; it will be rewritten when autostart is turned on again.");
        }

        if (!_config.Current.FirstRunShown)
        {
            // Defer until the message loop runs.
            _ui.Post(() =>
            {
                ShowQrWindow();
                try
                {
                    _config.Update(c => c.FirstRunShown = true);
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to save firstRunShown.", ex);
                }

                Log.Info("First run: QR window shown automatically.");
            });
        }
    }

    /// <summary>Shows the QR window and brings it to the front. UI thread only.</summary>
    public void ShowQrWindow()
    {
        if (_qrWindow is null || _qrWindow.IsDisposed)
        {
            _qrWindow = new QrWindow(_monitor, owner => RegenerateKey(owner));
        }

        _qrWindow.ShowAndActivate();
    }

    protected override void ExitThreadCore()
    {
        _server.TextReceived -= OnTextReceived;
        _monitor.Changed -= OnConnectionChanged;
        _notifyIcon.Visible = false;
        _qrWindow?.CloseForExit();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _qrWindow?.Dispose();
            _monitor.Dispose();
            _receivedIconTimer.Dispose();
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _idleIcon.Dispose();
            _receivedIcon.Dispose();
            _boldMenuFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnConnectionChanged(object? sender, ConnectionSnapshot snapshot) =>
        _notifyIcon.Text = BuildTooltip(snapshot);

    private void OnTextReceived(object? sender, string text)
    {
        // Raised on a Kestrel thread; build the preview here, update the UI on the UI thread.
        var preview = MakePreview(text);
        _ui.Post(() =>
        {
            if (!_notifyIcon.Visible)
            {
                return;
            }

            _notifyIcon.Icon = _receivedIcon;
            _receivedIconTimer.Stop();
            _receivedIconTimer.Start();
            _notifyIcon.ShowBalloonTip(3000, "Hatoclip", $"スマホから鳩が届きました\n{preview}", ToolTipIcon.None);
        });
    }

    private void RegenerateKey(IWin32Window? owner) => _keyRegenerator.ConfirmAndRegenerate(owner);

    private void ToggleAllowGet(bool allow)
    {
        _config.Update(c => c.AllowGet = allow);
        Log.Info($"allowGet set to {allow}.");
    }

    private void ToggleAutoStart()
    {
        try
        {
            if (AutoStart.IsEnabled())
            {
                AutoStart.Disable();
            }
            else
            {
                AutoStart.Enable();
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Error("Failed to change autostart setting.", ex);
            MessageBox.Show($"自動起動の設定を変更できませんでした。\n\n{ex.Message}", "Hatoclip",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        _autoStartItem.Checked = SafeIsAutoStartEnabled();
    }

    private static bool SafeIsAutoStartEnabled()
    {
        try
        {
            return AutoStart.IsEnabled();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Warn($"Could not read autostart setting ({ex.GetType().Name}).");
            return false;
        }
    }

    private static string BuildTooltip(ConnectionSnapshot snapshot)
    {
        var text = $"Hatoclip – {snapshot.BaseUrl}";
        return text.Length <= MaxTooltipLength ? text : text[..(MaxTooltipLength - 1)] + "…";
    }

    private static string MakePreview(string text)
    {
        var collapsed = string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (collapsed.Length == 0)
        {
            return "（空のテキスト）";
        }

        var info = new System.Globalization.StringInfo(collapsed);
        return info.LengthInTextElements > PreviewLength
            ? info.SubstringByTextElements(0, PreviewLength) + "…"
            : collapsed;
    }
}
