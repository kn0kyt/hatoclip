using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Hatoclip;

/// <summary>Visual weight of a <see cref="RoundedButton"/> (the phone page's .btn / .btn-secondary).</summary>
public enum RoundedButtonKind
{
    /// <summary>Accent background, white text: the main action.</summary>
    Primary,

    /// <summary>Soft accent background, accent text: small helper actions (コピー).</summary>
    Secondary,

    /// <summary>Outline only: actions that should not draw attention.</summary>
    Quiet,
}

/// <summary>
/// Owner-drawn rounded button. It is still a <see cref="Button"/>, so Enter/Space, mnemonics,
/// AcceptButton/CancelButton (IButtonControl) and the PushButton accessible role work as usual; only the
/// painting is replaced. The body is drawn inside <see cref="Theme.FocusRingInset"/>, which leaves room
/// for the keyboard focus ring around it.
/// </summary>
public sealed class RoundedButton : Button
{
    private ThemePalette _palette = Theme.Current;
    private RoundedButtonKind _kind;
    private Color _surface;
    private float _uiScale = 1f;
    private bool _hover;
    private bool _pressed;

    public RoundedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        UseVisualStyleBackColor = false;
        FlatStyle = FlatStyle.Flat;
        _surface = _palette.Card;
        Cursor = Cursors.Hand;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RoundedButtonKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemePalette Palette
    {
        get => _palette;
        set
        {
            _palette = value;
            Invalidate();
        }
    }

    /// <summary>Colour behind the button (the card or the window background), painted in the corners.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SurfaceColor
    {
        get => _surface;
        set
        {
            _surface = value;
            Invalidate();
        }
    }

    /// <summary>Device pixels per logical (96 DPI) pixel.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float UiScale
    {
        get => _uiScale;
        set
        {
            _uiScale = value;
            Invalidate();
        }
    }

    /// <summary>Space between the control's bounds and the drawn body, in device pixels.</summary>
    public int BodyInset => Theme.Scale(Theme.FocusRingInset, _uiScale);

    /// <summary>Width of the body for the given text (device pixels), excluding <see cref="BodyInset"/>.</summary>
    public int MeasureBodyWidth(string text) =>
        TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width +
        2 * Theme.Scale(16, _uiScale);

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }

        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        if (!Enabled)
        {
            _hover = false;
            _pressed = false;
        }

        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // Everything is painted in OnPaint (no system button background).
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.Clear(_surface);

        var inset = BodyInset;
        var body = Rectangle.Inflate(ClientRectangle, -inset, -inset);
        if (body.Width <= 0 || body.Height <= 0)
        {
            return;
        }

        var radius = Theme.Scale(Theme.ButtonRadius, _uiScale);
        var hairline = Drawing.HairlineWidth(_uiScale);
        var (fill, text, border) = Colors();
        Drawing.FillRoundedRect(g, body, radius, fill, border, hairline);

        if (Focused && ShowFocusCues)
        {
            var ringWidth = Math.Max(2f, (float)Math.Round(2 * _uiScale));
            // The ring's outer edge is the control's edge; with the 3px inset a 1px gap separates it from the body.
            var ring = RectangleF.Inflate(ClientRectangle, -ringWidth / 2f, -ringWidth / 2f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Drawing.RoundedRect(ring, radius + inset - ringWidth / 2f);
            using var pen = new Pen(_palette.Focus, ringWidth);
            g.DrawPath(pen, path);
        }

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        if (!UseMnemonic)
        {
            flags |= TextFormatFlags.NoPrefix;
        }
        else if (!ShowKeyboardCues)
        {
            flags |= TextFormatFlags.HidePrefix;
        }

        TextRenderer.DrawText(g, Text, Font, body, text, flags);
    }

    private (Color Fill, Color Text, Color? Border) Colors()
    {
        var p = _palette;
        // Hover/pressed: darken in light mode, lighten in dark mode (like the page's brightness(0.92)).
        var shadeTarget = p.IsDark ? Color.White : Color.Black;
        var amount = _pressed ? 0.16f : _hover ? 0.08f : 0f;

        (Color fill, Color text, Color? border) = _kind switch
        {
            RoundedButtonKind.Primary => (Blend(p.Accent, shadeTarget, amount), p.AccentText, (Color?)null),
            RoundedButtonKind.Secondary => (Blend(p.AccentSoft, p.Accent, amount * 1.5f), p.Accent, null),
            _ => (_pressed ? p.AccentSoft : _hover ? Blend(_surface, p.AccentSoft, 0.6f) : _surface, p.Text, p.Border),
        };

        if (!Enabled)
        {
            // The page uses opacity 0.45 for disabled buttons.
            fill = Blend(_surface, fill, 0.45f);
            text = Blend(_surface, text, 0.45f);
            border = border is { } b ? Blend(_surface, b, 0.45f) : null;
        }

        return (fill, text, border);
    }

    /// <summary>Linear blend from <paramref name="a"/> (t = 0) to <paramref name="b"/> (t = 1).</summary>
    internal static Color Blend(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(a.R + (b.R - a.R) * t),
            (int)Math.Round(a.G + (b.G - a.G) * t),
            (int)Math.Round(a.B + (b.B - a.B) * t));
    }
}
