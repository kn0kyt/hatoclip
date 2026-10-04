namespace Hatoclip;

/// <summary>
/// Network diagnostics messages (Japanese) shown in the QR window (see docs/design.md): a stack of
/// <see cref="DiagnosticRow"/>s. The owner sets fonts, palette and scale and calls
/// <see cref="LayoutRows"/> to place the rows for a width.
/// </summary>
public sealed class DiagnosticsPanel : ThemedControl
{
    private const int RowGap = 10;

    private Font? _regular;
    private Font? _bold;
    private Font? _small;
    private List<(DiagnosticKind Kind, string Title, string? Body)> _shown = [];

    public DiagnosticsPanel()
    {
        BackColor = Palette.Card;
    }

    public void Display(ConnectionSnapshot s)
    {
        var rows = new List<(DiagnosticKind Kind, string Title, string? Body)>();
        if (!s.HasLanIp)
        {
            rows.Add((DiagnosticKind.Warning, "⚠ LAN の IP アドレスが見つかりません。Wi-Fi または有線 LAN に接続してください。", null));
        }
        else
        {
            switch (s.Category)
            {
                case NetworkCategory.Private:
                    rows.Add((DiagnosticKind.Ok, "ネットワーク: プライベート ✓", null));
                    break;
                case NetworkCategory.Domain:
                    rows.Add((DiagnosticKind.Ok, "ネットワーク: ドメイン ✓", null));
                    break;
                case NetworkCategory.Public:
                    rows.Add((DiagnosticKind.Warning, "⚠ このネットワークは「パブリック」になっています。スマホから接続できない可能性があります。",
                        "プライベートに変更する手順：設定 → ネットワークとインターネット → Wi-Fi（またはイーサネット）→ 接続中のネットワーク → 「プライベート ネットワーク」を選択"));
                    break;
                default:
                    rows.Add((DiagnosticKind.Info, "ネットワーク: 種類を確認できませんでした", null));
                    break;
            }

            if (s.Firewall is FirewallStatus.NotAllowed or FirewallStatus.Blocked)
            {
                rows.Add((DiagnosticKind.Warning, "⚠ ファイアウォールでこのアプリが許可されていない可能性があります。", null));
            }
        }

        rows.Add((DiagnosticKind.Hint,
            "スマホから接続できない場合は、初回起動時に表示された Windows ファイアウォールの確認で「許可」を選んだか確認してください。ゲスト用 Wi-Fi では接続できません。",
            null));

        if (rows.SequenceEqual(_shown))
        {
            return; // nothing changed: keep the controls (no flicker)
        }

        _shown = rows;
        SuspendLayout();
        foreach (var c in Controls.Cast<Control>().ToList())
        {
            c.Dispose();
        }

        Controls.Clear();
        foreach (var (kind, title, body) in rows)
        {
            var row = new DiagnosticRow(kind, title, body) { Palette = Palette, UiScale = UiScale };
            if (_regular is not null)
            {
                row.SetFonts(_regular, _bold!, _small!);
            }

            Controls.Add(row);
        }

        ResumeLayout(false);
    }

    public void SetFonts(Font regular, Font bold, Font small)
    {
        _regular = regular;
        _bold = bold;
        _small = small;
        foreach (var row in Controls.OfType<DiagnosticRow>())
        {
            row.SetFonts(regular, bold, small);
        }
    }

    /// <summary>Places the rows top to bottom at <paramref name="width"/>; returns the total height (device pixels).</summary>
    public int LayoutRows(int width)
    {
        var y = 0;
        var first = true;
        foreach (var row in Controls.OfType<DiagnosticRow>())
        {
            if (!first)
            {
                y += S(RowGap);
            }

            first = false;
            var h = row.MeasureHeight(width);
            row.SetBounds(0, y, width, h);
            y += h;
        }

        return y;
    }

    protected override void OnPaletteChanged()
    {
        BackColor = Palette.Card;
        foreach (var row in Controls.OfType<DiagnosticRow>())
        {
            row.Palette = Palette;
        }
    }

    protected override void OnUiScaleChanged()
    {
        foreach (var row in Controls.OfType<DiagnosticRow>())
        {
            row.UiScale = UiScale;
        }
    }

    protected override void OnPaint(PaintEventArgs e) => e.Graphics.Clear(Palette.Card);
}
