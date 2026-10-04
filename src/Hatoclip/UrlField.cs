using System.ComponentModel;

namespace Hatoclip;

/// <summary>
/// Read-only, selectable URL in a rounded "field" (the phone page's <c>.field</c>): a borderless
/// <see cref="TextBox"/> inside a custom-painted rounded box. While the text box does not have the
/// focus, a URL that is too long is shown shortened with "…"; on focus the full text is put back and
/// selected (so Ctrl+C and the copy button always see the whole URL). When disabled, the text is
/// painted greyed out instead (a disabled TextBox would ignore the theme colours).
/// </summary>
public sealed class UrlField : ThemedControl
{
    private const int HorizontalPadding = 12;

    private readonly TextBox _box;
    private string _value = "";
    private bool _focused;

    public UrlField()
    {
        _box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            TabStop = true,
        };
        _box.GotFocus += (_, _) =>
        {
            _focused = true;
            _box.Text = _value;
            _box.BeginInvoke(_box.SelectAll);
            Invalidate();
        };
        _box.LostFocus += (_, _) =>
        {
            _focused = false;
            UpdateDisplayedText();
            Invalidate();
        };
        Controls.Add(_box);
        ApplyColors();
    }

    /// <summary>The full URL (what is copied and selected).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value
    {
        get => _value;
        set
        {
            value ??= "";
            if (_value != value)
            {
                _value = value;
                UpdateDisplayedText();
                Invalidate();
            }
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Font TextFont
    {
        get => _box.Font;
        set
        {
            _box.Font = value;
            PerformLayout();
            UpdateDisplayedText();
            Invalidate();
        }
    }

    /// <summary>Accessible name of the inner text box (the caption shown above the field).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? FieldAccessibleName
    {
        get => _box.AccessibleName;
        set => _box.AccessibleName = value;
    }

    /// <summary>
    /// The longest prefix of <paramref name="text"/> that fits in <paramref name="maxWidth"/> followed by
    /// "…" (or the text itself when it fits). <paramref name="measure"/> returns a string's width.
    /// </summary>
    internal static string Ellipsize(string text, int maxWidth, Func<string, int> measure)
    {
        const string ellipsis = "…";
        if (text.Length == 0 || measure(text) <= maxWidth)
        {
            return text;
        }

        // Binary search for the largest n with measure(text[..n] + "…") <= maxWidth.
        int lo = 0, hi = text.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (measure(text[..mid] + ellipsis) <= maxWidth)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (lo > 0 && char.IsHighSurrogate(text[lo - 1]))
        {
            lo--; // never split a surrogate pair
        }

        return text[..lo] + ellipsis;
    }

    protected override void OnPaletteChanged() => ApplyColors();

    protected override void OnUiScaleChanged() => PerformLayout();

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        _box.Visible = Enabled;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Enabled)
        {
            _box.Focus(); // clicks on the field's padding focus the text as well
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var pad = S(HorizontalPadding);
        var height = _box.PreferredHeight;
        _box.SetBounds(pad, Math.Max(0, (ClientSize.Height - height) / 2), Math.Max(0, ClientSize.Width - 2 * pad), height);
        UpdateDisplayedText();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Palette.Card);
        var hairline = Drawing.HairlineWidth(UiScale);
        var border = _focused ? Palette.Focus : Palette.Border;
        Drawing.FillRoundedRect(g, ClientRectangle, S(Theme.FieldRadius), Palette.Field, border, _focused ? hairline * 2 : hairline);

        if (!Enabled)
        {
            var pad = S(HorizontalPadding);
            var rect = new Rectangle(pad, 0, ClientSize.Width - 2 * pad, ClientSize.Height);
            TextRenderer.DrawText(g, _value, _box.Font, rect, Palette.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
        }
    }

    private void ApplyColors()
    {
        _box.BackColor = Palette.Field;
        _box.ForeColor = Palette.Text;
        BackColor = Palette.Card;
    }

    private void UpdateDisplayedText()
    {
        if (_focused)
        {
            if (_box.Text != _value)
            {
                _box.Text = _value;
            }

            return;
        }

        // Leave a few pixels for the edit control's internal margins.
        var available = _box.ClientSize.Width - S(6);
        var font = _box.Font;
        var shown = available <= 0
            ? _value
            : Ellipsize(_value, available, s => TextRenderer.MeasureText(s, font, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width);
        if (_box.Text != shown)
        {
            _box.Text = shown;
        }
    }
}
