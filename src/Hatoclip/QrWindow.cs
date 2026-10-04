using System.Runtime.InteropServices;

namespace Hatoclip;

/// <summary>
/// "接続用QRコード" window: QR of the connection URL, the URL as text (IP and .local
/// variants) with copy buttons, a "phone connected" line, network diagnostics, and key regeneration.
/// Closing only hides it.
/// </summary>
/// <remarks>
/// Look: the phone page's palette and shapes (<see cref="Theme"/>): a header with the pigeon mark on
/// the window background, one rounded card (QR on the left, connection status and diagnostics on the
/// right, the two URL rows below), and the buttons underneath. Layout is manual, in logical (96 DPI)
/// pixels scaled by the window's DPI (per-monitor v2), recomputed on DPI and content changes.
/// </remarks>
public sealed class QrWindow : Form
{
    // Logical (96 DPI) pixels.
    private const int QrSize = 300;
    private const int MinQrSize = 200;
    private const int ColumnGap = 24;
    private const int RightColumnWidth = 340;
    private const int MarkSize = 40;
    private const int FieldHeight = 34;
    private const int ButtonHeight = 34;
    private const int CopyButtonWidth = 88;
    private const int MinButtonWidth = 96;

    private const string PhoneConnectedText = "✓ スマホが接続しました";

    private readonly ConnectionMonitor _monitor;
    private readonly Action<IWin32Window> _regenerateKey;
    private readonly PigeonMark _mark;
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly CardPanel _card;
    private readonly QrView _qr;
    private readonly DiagnosticRow _phoneConnected;
    private readonly DiagnosticsPanel _diagnostics;
    private readonly Label _ipCaption;
    private readonly Label _localCaption;
    private readonly UrlField _ipUrl;
    private readonly UrlField _localUrl;
    private readonly RoundedButton _ipCopyButton;
    private readonly RoundedButton _localCopyButton;
    private readonly RoundedButton _regenerateButton;
    private readonly RoundedButton _closeButton;
    private readonly Icon _icon;
    private ThemePalette _palette;
    private UiFonts? _fonts;
    private float _scale = 1f;
    private int _qrLogicalSize = QrSize;
    private bool _allowClose;
    private bool _centered;
    private bool _fitting;
    private string? _displayedKey;
    private ConnectionSnapshot? _snapshot;
    private string? _renderedQrUrl;
    private int _renderedQrArea;

    public QrWindow(ConnectionMonitor monitor, Action<IWin32Window> regenerateKey)
    {
        _monitor = monitor;
        _regenerateKey = regenerateKey;
        _icon = TrayIcons.LoadWindowIcon();
        _palette = Theme.Current;

        SuspendLayout();
        AutoScaleMode = AutoScaleMode.None; // DPI scaling is done by ApplyScale
        Text = "Hatoclip – 接続用QRコード";
        Icon = _icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        _mark = new PigeonMark();
        _title = new Label { Text = "Hatoclip", AutoSize = true, UseMnemonic = false };
        _subtitle = new Label { Text = "iPhone のカメラで読み取り、表示されたリンクを開いてください", AutoSize = true, UseMnemonic = false };

        _card = new CardPanel { TabIndex = 0 };
        _qr = new QrView { AccessibleName = "接続用QRコード" };

        // Empty until a phone connects. Its height is always reserved, so filling it in or clearing it
        // does not move the rest of the window.
        _phoneConnected = new DiagnosticRow(DiagnosticKind.Connected, "");
        _diagnostics = new DiagnosticsPanel();

        _ipCaption = new Label { Text = "URL（IP アドレス）", AutoSize = true, UseMnemonic = false };
        _ipUrl = new UrlField { FieldAccessibleName = "URL（IP アドレス）", TabIndex = 0 };
        _ipCopyButton = CreateCopyButton(_ipUrl, tabIndex: 1);
        _localCaption = new Label { Text = "URL（PC 名 .local 版）", AutoSize = true, UseMnemonic = false };
        _localUrl = new UrlField { FieldAccessibleName = "URL（PC 名 .local 版）", TabIndex = 2 };
        _localCopyButton = CreateCopyButton(_localUrl, tabIndex: 3);

        _card.Controls.AddRange([_qr, _phoneConnected, _diagnostics, _ipCaption, _ipUrl, _ipCopyButton, _localCaption, _localUrl, _localCopyButton]);

        _regenerateButton = new RoundedButton { Text = "キーを再生成", Kind = RoundedButtonKind.Quiet, TabIndex = 1 };
        _regenerateButton.Click += (_, _) => _regenerateKey(this);
        _closeButton = new RoundedButton { Text = "閉じる", Kind = RoundedButtonKind.Primary, TabIndex = 2 };
        _closeButton.Click += (_, _) => Hide();

        Controls.AddRange([_mark, _title, _subtitle, _card, _regenerateButton, _closeButton]);
        CancelButton = _closeButton;

        ApplyPalette();
        ApplyScale(DeviceDpi);
        ResumeLayout(false);

        _monitor.Changed += OnSnapshotChanged;
        Theme.Changed += OnThemeChanged;
    }

    /// <summary>Shows the window (or restores it) and brings it to the front.</summary>
    public void ShowAndActivate()
    {
        if (!Visible)
        {
            Show();
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        // Briefly topmost so the window appears above whatever the user is doing, then a normal window again.
        TopMost = true;
        Activate();
        SetForegroundWindow(Handle);
        BeginInvoke(() => TopMost = false);
    }

    /// <summary>
    /// Shows "✓ スマホが接続しました" (a phone with the current key opened the page). It stays until
    /// the window is shown again or the key changes. UI thread only.
    /// </summary>
    public void ShowPhoneConnected() => _phoneConnected.SetText(PhoneConnectedText);

    /// <summary>Allows the window to really close (application exit).</summary>
    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeTheme.SetDarkTitleBar(Handle, _palette.IsDark);
        NativeTheme.SetCornerPreference(Handle, NativeTheme.CornerPreference.Round);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyScale(DeviceDpi); // the handle exists now: DeviceDpi is the DPI of the monitor the window is on
        Display(_monitor.Current);
        if (!_centered)
        {
            _centered = true;
            CenterToScreen(); // the size is final only now
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyScale(e.DeviceDpiNew);
        // Child controls receive their own DPI messages around this one; lay out again once they are done.
        BeginInvoke(() =>
        {
            if (!IsDisposed)
            {
                ApplyScale(DeviceDpi);
            }
        });
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ActiveControl = _closeButton; // don't start with the key-bearing URL selected
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            _phoneConnected.SetText(""); // only report phones that connect while the window is showing
            _monitor.RequestRefresh(); // pick up changes that happened while hidden
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monitor.Changed -= OnSnapshotChanged;
            Theme.Changed -= OnThemeChanged;
            _icon.Dispose();
        }

        base.Dispose(disposing);
        if (disposing)
        {
            _fonts?.Dispose(); // after the controls that use them
        }
    }

    private void OnSnapshotChanged(object? sender, ConnectionSnapshot snapshot)
    {
        if (IsHandleCreated)
        {
            Display(snapshot);
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        _palette = Theme.Current;
        ApplyPalette();
        if (IsHandleCreated)
        {
            NativeTheme.SetDarkTitleBar(Handle, _palette.IsDark);
        }

        Invalidate(true);
    }

    private void Display(ConnectionSnapshot s)
    {
        if (_displayedKey is not null && _displayedKey != s.Key)
        {
            _phoneConnected.SetText(""); // that phone used the old key; the new QR has not been used yet
        }

        _displayedKey = s.Key;
        _snapshot = s; // the QR image is re-rendered by LayoutContent when the URL or the size changed

        _ipUrl.Value = s.IpUrl ?? "（LAN の IP アドレスが見つかりません）";
        _ipUrl.Enabled = s.HasLanIp;
        _ipCopyButton.Enabled = s.HasLanIp;
        _localUrl.Value = s.LocalUrl;
        _diagnostics.Display(s);
        _diagnostics.SetFonts(_fonts!.Regular, _fonts.Bold, _fonts.Small);
        _diagnostics.Palette = _palette;
        _diagnostics.UiScale = _scale;
        LayoutContent();
    }

    private void RenderQr()
    {
        if (_snapshot is null)
        {
            return;
        }

        _renderedQrUrl = _snapshot.QrUrl;
        _renderedQrArea = _qr.ImageArea;
        try
        {
            _qr.Image = QrCodeRenderer.Render(_snapshot.QrUrl, _qr.ImageArea);
        }
        catch (Exception ex)
        {
            Log.Error("QR code rendering failed.", ex);
            _qr.Image = null;
        }
    }

    private RoundedButton CreateCopyButton(UrlField field, int tabIndex)
    {
        var copy = new RoundedButton { Text = "コピー", Kind = RoundedButtonKind.Secondary, TabIndex = tabIndex };
        copy.Click += (_, _) => CopyToClipboard(field.Value, copy);
        return copy;
    }

    private void ApplyPalette()
    {
        var p = _palette;
        BackColor = p.Background;
        _title.ForeColor = p.Text;
        _title.BackColor = p.Background;
        _subtitle.ForeColor = p.Muted;
        _subtitle.BackColor = p.Background;
        _ipCaption.ForeColor = p.Muted;
        _localCaption.ForeColor = p.Muted;
        _ipCaption.BackColor = _localCaption.BackColor = p.Card;

        foreach (var c in new ThemedControl[] { _mark, _card, _qr, _phoneConnected, _diagnostics, _ipUrl, _localUrl })
        {
            c.Palette = p;
        }

        foreach (var b in new[] { _ipCopyButton, _localCopyButton })
        {
            b.Palette = p;
            b.SurfaceColor = p.Card;
        }

        foreach (var b in new[] { _regenerateButton, _closeButton })
        {
            b.Palette = p;
            b.SurfaceColor = p.Background;
        }
    }

    /// <summary>Recreates the fonts and re-lays out the window for <paramref name="dpi"/>.</summary>
    internal void ApplyScale(int dpi)
    {
        _scale = dpi / 96f;
        UiFonts? old = null;
        if (_fonts is null || _fonts.Dpi != dpi)
        {
            // Only replace fonts for a new DPI: Control.Font ignores a value-equal font, so a fresh but
            // equal set would leave the controls on the old (then disposed) instances.
            old = _fonts;
            _fonts = new UiFonts(dpi);
        }

        Font = _fonts.Regular;
        _title.Font = _fonts.Title;
        _subtitle.Font = _fonts.Regular;
        _ipCaption.Font = _localCaption.Font = _fonts.SmallBold;
        _ipUrl.TextFont = _localUrl.TextFont = _fonts.Regular;
        foreach (var b in new[] { _ipCopyButton, _localCopyButton, _regenerateButton, _closeButton })
        {
            b.Font = _fonts.Button;
            b.UiScale = _scale;
        }

        _phoneConnected.SetFonts(_fonts.Regular, _fonts.Bold, _fonts.Small);
        _diagnostics.SetFonts(_fonts.Regular, _fonts.Bold, _fonts.Small);
        foreach (var c in new ThemedControl[] { _mark, _card, _qr, _phoneConnected, _diagnostics, _ipUrl, _localUrl })
        {
            c.UiScale = _scale;
        }

        LayoutContent();
        old?.Dispose();
    }

    private int S(int logical) => Theme.Scale(logical, _scale);

    /// <summary>Places every control for the current scale and content, and sizes the window.</summary>
    private void LayoutContent()
    {
        if (_fonts is null)
        {
            return;
        }

        if (!_fitting)
        {
            _qrLogicalSize = QrSize; // FitToScreen shrinks it again only if the full size does not fit
        }

        SuspendLayout();
        _card.SuspendLayout();

        var outer = S(Theme.OuterPadding);
        var cardPad = S(Theme.CardPadding);
        var qrSize = S(_qrLogicalSize);
        var columnGap = S(ColumnGap);
        var rightWidth = S(RightColumnWidth);
        var innerWidth = qrSize + columnGap + rightWidth;
        var cardWidth = innerWidth + 2 * cardPad;
        var clientWidth = cardWidth + 2 * outer;

        // Header: mark, then title over subtitle, vertically centred on the mark.
        var y = outer;
        var mark = S(MarkSize);
        var textBlock = _title.PreferredHeight + _subtitle.PreferredHeight;
        var headerHeight = Math.Max(mark, textBlock);
        _mark.SetBounds(outer - S(2), y + (headerHeight - mark) / 2, mark, mark);
        var textX = outer + mark + S(10);
        var textY = y + (headerHeight - textBlock) / 2;
        _title.Location = new Point(textX - S(1), textY);
        _subtitle.Location = new Point(textX, textY + _title.PreferredHeight);
        y += headerHeight + S(Theme.BlockGap);

        // Card, top block: QR on the left; diagnostics (top) and "phone connected" (bottom) on the right.
        var cy = cardPad;
        _qr.SetBounds(cardPad, cy, qrSize, qrSize);
        var rightX = cardPad + qrSize + columnGap;
        var diagnosticsHeight = _diagnostics.LayoutRows(rightWidth);
        _diagnostics.SetBounds(rightX, cy, rightWidth, diagnosticsHeight);
        var connectedHeight = _phoneConnected.MeasureHeight(rightWidth);
        var connectedY = Math.Max(cy + diagnosticsHeight + S(12), cy + qrSize - connectedHeight);
        _phoneConnected.SetBounds(rightX, connectedY, rightWidth, connectedHeight);
        cy = Math.Max(cy + qrSize, connectedY + connectedHeight) + S(Theme.BlockGap) + S(2);

        // Card, URL rows (full width).
        var copyBody = Math.Max(S(CopyButtonWidth), _ipCopyButton.MeasureBodyWidth("コピー済み"));
        var fieldHeight = Math.Max(S(FieldHeight), _ipUrl.TextFont.Height + S(14));
        cy = LayoutUrlRow(_ipCaption, _ipUrl, _ipCopyButton, cardPad, cy, innerWidth, copyBody, fieldHeight);
        cy += S(10);
        cy = LayoutUrlRow(_localCaption, _localUrl, _localCopyButton, cardPad, cy, innerWidth, copyBody, fieldHeight);
        cy += cardPad;
        _card.SetBounds(outer, y, cardWidth, cy);
        y += cy + S(Theme.BlockGap);

        // Buttons: キーを再生成 on the left (quiet), 閉じる on the right (primary).
        var buttonHeight = Math.Max(S(ButtonHeight), _closeButton.Font.Height + S(14));
        PlaceButton(_regenerateButton, outer, y, buttonHeight, alignRight: false);
        PlaceButton(_closeButton, outer + cardWidth, y, buttonHeight, alignRight: true);
        y += buttonHeight + outer;

        _card.ResumeLayout(false);
        ResumeLayout(false);

        ClientSize = new Size(clientWidth, y);

        if (FitToScreen())
        {
            return; // laid out again with a smaller QR code
        }

        if (_snapshot is not null && (_snapshot.QrUrl != _renderedQrUrl || _qr.ImageArea != _renderedQrArea))
        {
            RenderQr();
        }
    }

    private int LayoutUrlRow(Label caption, UrlField field, RoundedButton copy, int x, int y, int width, int copyBody, int fieldHeight)
    {
        caption.Location = new Point(x + S(2), y);
        y += caption.PreferredHeight + S(4);
        var fieldWidth = width - copyBody - S(Theme.SmallGap);
        field.SetBounds(x, y, fieldWidth, fieldHeight);
        var inset = copy.BodyInset;
        copy.SetBounds(x + width - copyBody - inset, y - inset, copyBody + 2 * inset, fieldHeight + 2 * inset);
        return y + fieldHeight;
    }

    private void PlaceButton(RoundedButton button, int x, int y, int bodyHeight, bool alignRight)
    {
        var inset = button.BodyInset;
        var bodyWidth = Math.Max(S(MinButtonWidth), button.MeasureBodyWidth(button.Text));
        var left = alignRight ? x - bodyWidth : x;
        button.SetBounds(left - inset, y - inset, bodyWidth + 2 * inset, bodyHeight + 2 * inset);
    }

    /// <summary>
    /// When the window is taller than the screen's working area (small screen at a high scale), shrinks
    /// the QR code (not below <see cref="MinQrSize"/>) and lays out again. Returns true if it did.
    /// </summary>
    private bool FitToScreen()
    {
        if (!IsHandleCreated)
        {
            return false;
        }

        var available = Screen.FromHandle(Handle).WorkingArea.Height;
        var overflow = Height - available;
        if (_fitting || overflow <= 0 || _qrLogicalSize <= MinQrSize)
        {
            return false;
        }

        _fitting = true;
        try
        {
            var before = (Height, Qr: _qrLogicalSize);
            var shrink = (int)Math.Ceiling(overflow / _scale);
            _qrLogicalSize = Math.Max(MinQrSize, _qrLogicalSize - shrink);
            LayoutContent();
            if (Height >= before.Height)
            {
                // The diagnostics column, not the QR code, sets the height: a smaller QR does not help.
                _qrLogicalSize = before.Qr;
                LayoutContent();
            }
        }
        finally
        {
            _fitting = false;
        }

        return true;
    }

    private static void CopyToClipboard(string text, Button button)
    {
        try
        {
            Clipboard.SetText(text, TextDataFormat.UnicodeText);
        }
        catch (ExternalException ex)
        {
            Log.Warn($"Could not copy URL to clipboard ({ex.GetType().Name}).");
            return;
        }

        button.Text = "コピー済み";
        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            if (!button.IsDisposed)
            {
                button.Text = "コピー";
            }
        };
        timer.Start();
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>The window's fonts for one DPI (device-pixel sizes, see <see cref="Theme.CreateFont"/>).</summary>
    private sealed class UiFonts : IDisposable
    {
        public UiFonts(int dpi)
        {
            Dpi = dpi;
            Regular = Theme.CreateFont(Theme.BaseFontSize, dpi);
            Bold = Theme.CreateFont(Theme.BaseFontSize, dpi, semibold: true);
            Small = Theme.CreateFont(Theme.SmallFontSize, dpi);
            SmallBold = Theme.CreateFont(Theme.SmallFontSize, dpi, semibold: true);
            Title = Theme.CreateFont(Theme.TitleFontSize, dpi, semibold: true);
            Button = Theme.CreateFont(Theme.BaseFontSize, dpi, semibold: true);
        }

        public int Dpi { get; }

        public Font Regular { get; }

        public Font Bold { get; }

        public Font Small { get; }

        public Font SmallBold { get; }

        public Font Title { get; }

        public Font Button { get; }

        public void Dispose()
        {
            Regular.Dispose();
            Bold.Dispose();
            Small.Dispose();
            SmallBold.Dispose();
            Title.Dispose();
            Button.Dispose();
        }
    }
}
