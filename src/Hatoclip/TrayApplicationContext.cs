namespace Hatoclip;

/// <summary>Task-tray UI: NotifyIcon, context menu, receive/read indications and the QR window.</summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    internal const string ReceivedMessage = "鳩がテキストを届けました";
    private const int PreviewLength = 40;
    private const int MaxTooltipLength = 63;
    private static readonly TimeSpan TransientIconDuration = TimeSpan.FromSeconds(3);

    private readonly ConfigStore _config;
    private readonly HatoclipServer _server;
    private readonly UiDispatcher _ui;
    private readonly ConnectionMonitor _monitor;
    private readonly KeyRegenerator _keyRegenerator;
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _idleIcon;
    private readonly Icon _receivedIcon;
    private readonly Icon _sentIcon;
    private readonly System.Windows.Forms.Timer _transientIconTimer;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _allowGetItem;
    private readonly ToolStripMenuItem _showPreviewItem;
    private readonly ToolStripMenuItem _excludeFromHistoryItem;
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
        _showPreviewItem = new ToolStripMenuItem("通知に内容を表示")
        {
            CheckOnClick = true,
            Checked = _config.Current.ShowPreview,
        };
        _showPreviewItem.CheckedChanged += (_, _) => ToggleShowPreview(_showPreviewItem.Checked);
        _excludeFromHistoryItem = new ToolStripMenuItem("履歴と同期に残さない")
        {
            CheckOnClick = true,
            Checked = _config.Current.ExcludeFromHistory,
        };
        _excludeFromHistoryItem.CheckedChanged += (_, _) => ToggleExcludeFromHistory(_excludeFromHistoryItem.Checked);
        var regenerateItem = new ToolStripMenuItem("接続キーを再生成...", null, (_, _) => RegenerateKey(_qrWindow is { Visible: true } w ? w : null));

        menu.Items.Add(showQrItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(_allowGetItem);
        menu.Items.Add(_showPreviewItem);
        menu.Items.Add(_excludeFromHistoryItem);
        menu.Items.Add(regenerateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitThread());
        menu.Opening += (_, _) => _autoStartItem.Checked = SafeIsAutoStartEnabled();

        _idleIcon = TrayIcons.LoadIdle();
        _receivedIcon = TrayIcons.LoadReceived();
        _sentIcon = TrayIcons.LoadSent();
        // One timer for both transient icons: the latest event wins and restarts the countdown, so
        // there is exactly one revert to idle.
        _transientIconTimer = new System.Windows.Forms.Timer { Interval = (int)TransientIconDuration.TotalMilliseconds };
        _transientIconTimer.Tick += (_, _) =>
        {
            _transientIconTimer.Stop();
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
        _server.ClipboardRead += OnClipboardRead;
        _server.Pinged += OnPinged;

        UpdateStaleAutoStart();

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
        _server.ClipboardRead -= OnClipboardRead;
        _server.Pinged -= OnPinged;
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
            _transientIconTimer.Dispose();
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _idleIcon.Dispose();
            _receivedIcon.Dispose();
            _sentIcon.Dispose();
            _boldMenuFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnConnectionChanged(object? sender, ConnectionSnapshot snapshot) =>
        _notifyIcon.Text = BuildTooltip(snapshot);

    private void OnTextReceived(object? sender, string text)
    {
        // Raised on a Kestrel thread; build the balloon text here, update the UI on the UI thread.
        var message = BuildReceivedMessage(text, _config.Current.ShowPreview);
        _ui.Post(() =>
        {
            if (!_notifyIcon.Visible)
            {
                return;
            }

            ShowTransientIcon(_receivedIcon);
            _notifyIcon.ShowBalloonTip(3000, "Hatoclip", message, ToolTipIcon.None);
        });
    }

    /// <summary>
    /// A phone fetched the PC clipboard: switch the icon briefly (no balloon), so that a read with a
    /// leaked key does not go unnoticed. Raised on a Kestrel thread.
    /// </summary>
    private void OnClipboardRead(object? sender, EventArgs e)
    {
        _ui.Post(() =>
        {
            if (_notifyIcon.Visible)
            {
                ShowTransientIcon(_sentIcon);
            }
        });
    }

    /// <summary>
    /// A phone with the right key opened the page: tell the QR window (if it is showing) so the user
    /// sees that scanning worked. Raised on a Kestrel thread.
    /// </summary>
    private void OnPinged(object? sender, EventArgs e)
    {
        _ui.Post(() =>
        {
            if (_qrWindow is { IsDisposed: false, Visible: true } window)
            {
                window.ShowPhoneConnected();
            }
        });
    }

    /// <summary>Shows <paramref name="icon"/> for <see cref="TransientIconDuration"/>, then idle. UI thread only.</summary>
    private void ShowTransientIcon(Icon icon)
    {
        _notifyIcon.Icon = icon;
        _transientIconTimer.Stop();
        _transientIconTimer.Start();
    }

    private void RegenerateKey(IWin32Window? owner) => _keyRegenerator.ConfirmAndRegenerate(owner);

    private void ToggleAllowGet(bool allow)
    {
        _config.Update(c => c.AllowGet = allow);
        Log.Info($"allowGet set to {allow}.");
    }

    private void ToggleShowPreview(bool show)
    {
        _config.Update(c => c.ShowPreview = show);
        Log.Info($"showPreview set to {show}.");
    }

    private void ToggleExcludeFromHistory(bool exclude)
    {
        _config.Update(c => c.ExcludeFromHistory = exclude);
        Log.Info($"excludeFromHistory set to {exclude}.");
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

    /// <summary>
    /// Autostart is on but points at an exe that no longer exists (the exe was moved): re-point it here.
    /// An entry whose exe still exists belongs to another copy and is left alone.
    /// </summary>
    private static void UpdateStaleAutoStart()
    {
        try
        {
            if (AutoStart.IsOrphaned())
            {
                AutoStart.Enable();
                Log.Info("Autostart entry pointed to an exe that no longer exists; updated it to the current exe.");
            }
            else if (AutoStart.IsStale())
            {
                Log.Info("Autostart entry points to another copy of the exe that still exists; left unchanged.");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Warn($"Could not update the stale autostart entry ({ex.GetType().Name}).");
        }
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

    /// <summary>Balloon text for received text: the fixed message, plus a preview line when enabled.</summary>
    internal static string BuildReceivedMessage(string text, bool showPreview) =>
        showPreview ? $"{ReceivedMessage}\n{MakePreview(text)}" : ReceivedMessage;

    internal static string MakePreview(string text)
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
