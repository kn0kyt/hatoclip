using System.Drawing.Drawing2D;

namespace Hatoclip;

/// <summary>How a <see cref="DiagnosticRow"/> is drawn.</summary>
public enum DiagnosticKind
{
    /// <summary>Green dot, green text (e.g. "ネットワーク: プライベート ✓").</summary>
    Ok,

    /// <summary>Muted dot, muted text.</summary>
    Info,

    /// <summary>Callout: soft amber rounded box with a warning triangle, bold title and optional body.</summary>
    Warning,

    /// <summary>Small muted text without a mark (the always-visible hint).</summary>
    Hint,

    /// <summary>Soft green rounded box with bold green text ("✓ スマホが接続しました"); draws nothing while empty.</summary>
    Connected,
}

/// <summary>
/// One line (or callout) of the QR window's status area. Custom painted with word wrapping; the
/// accessible name is the full text. A leading "⚠" of a warning title is drawn as a triangle icon
/// instead of the glyph (the text itself is unchanged).
/// </summary>
public sealed class DiagnosticRow : ThemedControl
{
    private const TextFormatFlags WrapFlags =
        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;

    private const string WarningGlyph = "⚠";

    private DiagnosticKind _kind;
    private string _title = "";
    private string? _body;
    private Font? _regularFont;
    private Font? _boldFont;
    private Font? _smallFont;

    public DiagnosticRow(DiagnosticKind kind, string title, string? body = null)
    {
        _kind = kind;
        AccessibleRole = AccessibleRole.StaticText;
        SetText(title, body);
    }

    public DiagnosticKind Kind => _kind;

    public string Title => _title;

    public string? Body => _body;

    public void SetText(string title, string? body = null)
    {
        _title = title;
        _body = body;
        AccessibleName = body is null ? title : $"{title} {body}";
        Invalidate();
    }

    public void SetFonts(Font regular, Font bold, Font small)
    {
        _regularFont = regular;
        _boldFont = bold;
        _smallFont = small;
        Invalidate();
    }

    /// <summary>Height (device pixels) of this row laid out at <paramref name="width"/>.</summary>
    public int MeasureHeight(int width)
    {
        var l = ComputeLayout(width);
        return l.Height;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Palette.Card);
        if (_regularFont is null || (_kind == DiagnosticKind.Connected && _title.Length == 0))
        {
            return;
        }

        var l = ComputeLayout(ClientSize.Width);
        var p = Palette;

        switch (_kind)
        {
            case DiagnosticKind.Warning:
                Drawing.FillRoundedRect(g, ClientRectangle, S(Theme.CalloutRadius), p.WarnSoft, p.WarnBorder, Drawing.HairlineWidth(UiScale));
                DrawWarningIcon(g, l.Mark, p);
                TextRenderer.DrawText(g, DisplayTitle, l.TitleFont, l.TitleRect, p.Warn, p.WarnSoft, WrapFlags);
                if (_body is not null)
                {
                    TextRenderer.DrawText(g, _body, l.BodyFont, l.BodyRect, p.Text, p.WarnSoft, WrapFlags);
                }

                break;
            case DiagnosticKind.Connected:
                Drawing.FillRoundedRect(g, ClientRectangle, S(Theme.CalloutRadius), p.OkSoft, null, 0);
                TextRenderer.DrawText(g, _title, l.TitleFont, l.TitleRect, p.Ok, p.OkSoft, WrapFlags);
                break;
            case DiagnosticKind.Ok:
            case DiagnosticKind.Info:
                var color = _kind == DiagnosticKind.Ok ? p.Ok : p.Muted;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(color))
                {
                    g.FillEllipse(brush, l.Mark);
                }

                TextRenderer.DrawText(g, _title, l.TitleFont, l.TitleRect, color, p.Card, WrapFlags);
                break;
            default:
                TextRenderer.DrawText(g, _title, l.TitleFont, l.TitleRect, p.Muted, p.Card, WrapFlags);
                break;
        }
    }

    private string DisplayTitle =>
        _kind == DiagnosticKind.Warning && _title.StartsWith(WarningGlyph, StringComparison.Ordinal)
            ? _title[WarningGlyph.Length..].TrimStart()
            : _title;

    private RowLayout ComputeLayout(int width)
    {
        var regular = _regularFont ?? Font;
        var bold = _boldFont ?? regular;
        var small = _smallFont ?? regular;

        switch (_kind)
        {
            case DiagnosticKind.Warning:
            {
                int padX = S(12), padY = S(10), icon = S(16), gap = S(8);
                var textX = padX + icon + gap;
                var textW = Math.Max(1, width - textX - padX);
                var titleH = Measure(DisplayTitle, bold, textW);
                var titleRect = new Rectangle(textX, padY, textW, titleH);
                var bodyRect = Rectangle.Empty;
                var bottom = titleRect.Bottom;
                if (_body is not null)
                {
                    var bodyH = Measure(_body, regular, textW);
                    bodyRect = new Rectangle(textX, bottom + S(4), textW, bodyH);
                    bottom = bodyRect.Bottom;
                }

                var markY = padY + (bold.Height - icon) / 2f;
                return new RowLayout(bottom + padY, new RectangleF(padX, markY, icon, icon), bold, titleRect, regular, bodyRect);
            }

            case DiagnosticKind.Connected:
            {
                int padX = S(12), padY = S(7);
                var textW = Math.Max(1, width - 2 * padX);
                // Measured with the real text even while empty, so the reserved height never changes.
                var titleH = Measure("✓", bold, textW);
                return new RowLayout(titleH + 2 * padY, RectangleF.Empty, bold, new Rectangle(padX, padY, textW, titleH), regular, Rectangle.Empty);
            }

            case DiagnosticKind.Ok:
            case DiagnosticKind.Info:
            {
                int dot = S(8), gap = S(8);
                var textX = dot + gap;
                var textW = Math.Max(1, width - textX);
                var titleH = Measure(_title, regular, textW);
                var markY = (regular.Height - dot) / 2f;
                return new RowLayout(titleH, new RectangleF(0, markY, dot, dot), regular, new Rectangle(textX, 0, textW, titleH), regular, Rectangle.Empty);
            }

            default:
            {
                var titleH = Measure(_title, small, width);
                return new RowLayout(titleH, RectangleF.Empty, small, new Rectangle(0, 0, width, titleH), small, Rectangle.Empty);
            }
        }
    }

    private static int Measure(string text, Font font, int width) =>
        TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue), WrapFlags).Height;

    private void DrawWarningIcon(Graphics g, RectangleF r, ThemePalette p)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = new GraphicsPath();
        var top = new PointF(r.X + r.Width / 2f, r.Y + r.Height * 0.06f);
        var right = new PointF(r.Right, r.Bottom - r.Height * 0.06f);
        var left = new PointF(r.X, r.Bottom - r.Height * 0.06f);
        path.AddPolygon(new[] { top, right, left });
        using (var fill = new SolidBrush(p.Warn))
        using (var pen = new Pen(p.Warn, Math.Max(1f, r.Width * 0.12f)) { LineJoin = LineJoin.Round })
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path); // rounds the corners a little
        }

        // Exclamation mark in the callout colour.
        var cx = r.X + r.Width / 2f;
        var stroke = Math.Max(1.2f, r.Width * 0.13f);
        using (var mark = new Pen(p.WarnSoft, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(mark, cx, r.Y + r.Height * 0.38f, cx, r.Y + r.Height * 0.62f);
        }

        using var dotBrush = new SolidBrush(p.WarnSoft);
        var dot = stroke * 1.05f;
        g.FillEllipse(dotBrush, cx - dot / 2f, r.Y + r.Height * 0.76f - dot / 2f, dot, dot);
    }

    private readonly record struct RowLayout(int Height, RectangleF Mark, Font TitleFont, Rectangle TitleRect, Font BodyFont, Rectangle BodyRect);
}
