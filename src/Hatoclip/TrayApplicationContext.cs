namespace Hatoclip;

/// <summary>Task-tray UI: NotifyIcon, context menu and receive notifications.</summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private const int PreviewLength = 40;

    private readonly ConfigStore _config;
    private readonly HatoclipServer _server;
    private readonly UiDispatcher _ui;
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly ToolStripMenuItem _allowGetItem;

    public TrayApplicationContext(ConfigStore config, HatoclipServer server, UiDispatcher ui)
    {
        _config = config;
        _server = server;
        _ui = ui;

        var menu = new ContextMenuStrip();
        menu.Items.Add("接続情報を表示", null, (_, _) => ShowConnectionInfo());
        _allowGetItem = new ToolStripMenuItem("スマホからの取得を許可")
        {
            CheckOnClick = true,
            Checked = _config.Current.AllowGet,
        };
        _allowGetItem.CheckedChanged += (_, _) => ToggleAllowGet(_allowGetItem.Checked);
        menu.Items.Add(_allowGetItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitThread());

        _icon = TrayIcons.LoadIdle();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "Hatoclip",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowConnectionInfo();

        _server.TextReceived += OnTextReceived;
    }

    protected override void ExitThreadCore()
    {
        _server.TextReceived -= OnTextReceived;
        _notifyIcon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnTextReceived(object? sender, string text)
    {
        // Raised on a Kestrel thread; build the preview here, show it on the UI thread.
        var preview = MakePreview(text);
        _ui.Post(() =>
        {
            if (_notifyIcon.Visible)
            {
                _notifyIcon.ShowBalloonTip(3000, "Hatoclip", $"スマホから鳩が届きました\n{preview}", ToolTipIcon.None);
            }
        });
    }

    private void ToggleAllowGet(bool allow)
    {
        _config.Update(c => c.AllowGet = allow);
        Log.Info($"allowGet set to {allow}.");
    }

    private void ShowConnectionInfo()
    {
        // Phase 1: plain MessageBox. Phase 2 replaces this with the QR window.
        var ip = NetworkInfo.GetLanIPv4();
        var url = NetworkInfo.BuildConnectUrl(ip, _server.Port, _config.Current.Key);
        var note = ip is null ? "\n\nLAN の IPv4 アドレスが見つかりませんでした。Wi-Fi / 有線 LAN の接続を確認してください。" : "";
        MessageBox.Show(
            $"スマホのブラウザで次の URL を開いてください（PC と同じ Wi-Fi に接続してください）。\n\n{url}{note}",
            "Hatoclip - 接続情報",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
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
