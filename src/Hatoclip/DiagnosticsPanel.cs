namespace Hatoclip;

/// <summary>Network diagnostics messages (Japanese) shown in the QR window (see docs/design.md).</summary>
public sealed class DiagnosticsPanel : FlowLayoutPanel
{
    private static readonly Color OkColor = Color.FromArgb(0, 120, 60);
    private static readonly Color WarnColor = Color.FromArgb(180, 60, 0);

    private readonly int _logicalTextWidth;
    private Font? _boldFont;
    private Font? _smallFont;

    /// <param name="logicalTextWidth">Wrap width at 96 DPI; scaled to the current DPI when displayed.</param>
    public DiagnosticsPanel(int logicalTextWidth)
    {
        _logicalTextWidth = logicalTextWidth;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(0, 8, 0, 0);
    }

    public void Display(ConnectionSnapshot s)
    {
        SuspendLayout();
        foreach (var c in Controls.Cast<Control>().ToList())
        {
            c.Dispose();
        }

        Controls.Clear();

        if (!s.HasLanIp)
        {
            AddLine("⚠ LAN の IP アドレスが見つかりません。Wi-Fi または有線 LAN に接続してください。", WarnColor, BoldFont);
        }
        else
        {
            switch (s.Category)
            {
                case NetworkCategory.Private:
                    AddLine("ネットワーク: プライベート ✓", OkColor);
                    break;
                case NetworkCategory.Domain:
                    AddLine("ネットワーク: ドメイン ✓", OkColor);
                    break;
                case NetworkCategory.Public:
                    AddLine("⚠ このネットワークは「パブリック」になっています。スマホから接続できない可能性があります。", WarnColor, BoldFont);
                    AddLine("プライベートに変更する手順：設定 → ネットワークとインターネット → Wi-Fi（またはイーサネット）→ 接続中のネットワーク → 「プライベート ネットワーク」を選択", WarnColor);
                    break;
                default:
                    AddLine("ネットワーク: 種類を確認できませんでした", SystemColors.GrayText);
                    break;
            }

            if (s.Firewall is FirewallStatus.NotAllowed or FirewallStatus.Blocked)
            {
                AddLine("⚠ ファイアウォールでこのアプリが許可されていない可能性があります。", WarnColor, BoldFont);
            }
        }

        AddLine("スマホから接続できない場合は、初回起動時に表示された Windows ファイアウォールの確認で「許可」を選んだか確認してください。ゲスト用 Wi-Fi では接続できません。",
            SystemColors.GrayText, SmallFont);

        ResumeLayout(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _boldFont?.Dispose();
            _smallFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    private Font BoldFont => _boldFont ??= new Font(Font, FontStyle.Bold);

    private Font SmallFont => _smallFont ??= new Font(Font.FontFamily, Font.SizeInPoints - 0.5f, FontStyle.Regular);

    private void AddLine(string text, Color color, Font? font = null)
    {
        var label = new Label
        {
            Text = text,
            ForeColor = color,
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(_logicalTextWidth), 0),
            Margin = new Padding(0, 2, 0, 2),
        };
        if (font is not null)
        {
            label.Font = font;
        }

        Controls.Add(label);
    }
}
