using System.Runtime.InteropServices;

namespace Hatoclip;

/// <summary>
/// "接続用QRコード" window (SPEC 3.3): QR of the connection URL, the URL as text (IP and .local
/// variants) with copy buttons, network diagnostics, and key regeneration. Closing only hides it.
/// </summary>
public sealed class QrWindow : Form
{
    private const int ContentWidth = 540; // logical (96 DPI) pixels
    private const int CopyButtonWidth = 96;
    private const int QrSize = 300;

    private readonly ConnectionMonitor _monitor;
    private readonly Action<IWin32Window> _regenerateKey;
    private readonly PictureBox _qrBox;
    private readonly TextBox _ipUrlBox;
    private readonly TextBox _localUrlBox;
    private readonly Button _ipCopyButton;
    private readonly Button _closeButton;
    private readonly DiagnosticsPanel _diagnostics;
    private readonly Icon _icon;
    private bool _allowClose;

    public QrWindow(ConnectionMonitor monitor, Action<IWin32Window> regenerateKey)
    {
        _monitor = monitor;
        _regenerateKey = regenerateKey;
        _icon = TrayIcons.LoadWindowIcon();

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Hatoclip – 接続用QRコード";
        Icon = _icon;
        Font = SystemFonts.MessageBoxFont ?? Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = SystemColors.Window;

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16),
            Dock = DockStyle.Fill,
        };

        root.Controls.Add(new Label
        {
            Text = "iPhone のカメラで読み取り、表示されたリンクを開いてください",
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(0, 0, 0, 8),
            Anchor = AnchorStyles.None,
        });

        _qrBox = new PictureBox
        {
            Size = new Size(QrSize, QrSize),
            SizeMode = PictureBoxSizeMode.CenterImage,
            BackColor = Color.White,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 8),
            AccessibleName = "接続用QRコード",
        };
        root.Controls.Add(_qrBox);

        (_ipUrlBox, _ipCopyButton) = AddUrlRow(root, "URL（IP アドレス）");
        (_localUrlBox, _) = AddUrlRow(root, "URL（PC 名 .local 版）");

        _diagnostics = new DiagnosticsPanel(ContentWidth);
        root.Controls.Add(_diagnostics);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = ContentWidth,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 12, 0, 0),
            WrapContents = false,
        };
        var closeButton = new Button { Text = "閉じる", AutoSize = true, MinimumSize = new Size(88, 0) };
        _closeButton = closeButton;
        closeButton.Click += (_, _) => Hide();
        var regenerateButton = new Button { Text = "キーを再生成", AutoSize = true, MinimumSize = new Size(88, 0) };
        regenerateButton.Click += (_, _) => _regenerateKey(this);
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(regenerateButton);
        root.Controls.Add(buttons);

        CancelButton = closeButton;
        Controls.Add(root);
        ResumeLayout(true);

        _monitor.Changed += OnSnapshotChanged;
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

    /// <summary>Allows the window to really close (application exit).</summary>
    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Display(_monitor.Current);
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
            _qrBox.Image?.Dispose();
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnSnapshotChanged(object? sender, ConnectionSnapshot snapshot)
    {
        if (IsHandleCreated)
        {
            Display(snapshot);
        }
    }

    private void Display(ConnectionSnapshot s)
    {
        SuspendLayout();
        var old = _qrBox.Image;
        try
        {
            _qrBox.Image = QrCodeRenderer.Render(s.QrUrl, Math.Min(_qrBox.ClientSize.Width, _qrBox.ClientSize.Height));
        }
        catch (Exception ex)
        {
            Log.Error("QR code rendering failed.", ex);
            _qrBox.Image = null;
        }

        old?.Dispose();

        _ipUrlBox.Text = s.IpUrl ?? "（LAN の IP アドレスが見つかりません）";
        _ipUrlBox.Enabled = s.HasLanIp;
        _ipCopyButton.Enabled = s.HasLanIp;
        _localUrlBox.Text = s.LocalUrl;
        _diagnostics.Display(s);
        ResumeLayout(true);
    }

    private (TextBox Box, Button Copy) AddUrlRow(TableLayoutPanel root, string caption)
    {
        root.Controls.Add(new Label
        {
            Text = caption,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 6, 0, 2),
        });

        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth - CopyButtonWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CopyButtonWidth));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var box = new TextBox
        {
            ReadOnly = true,
            BackColor = SystemColors.Window,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 0, 6, 0),
        };
        box.GotFocus += (_, _) => box.BeginInvoke(box.SelectAll);

        var copy = new Button
        {
            Text = "コピー",
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Height = box.PreferredHeight + 4,
            Margin = new Padding(0),
        };
        copy.Click += (_, _) => CopyToClipboard(box.Text, copy);

        row.Controls.Add(box, 0, 0);
        row.Controls.Add(copy, 1, 0);
        root.Controls.Add(row);
        return (box, copy);
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
}
