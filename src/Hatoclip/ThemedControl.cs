using System.ComponentModel;

namespace Hatoclip;

/// <summary>
/// Base for the custom-painted controls of the QR window: double-buffered, not focusable, and painted
/// from a <see cref="ThemePalette"/> and a DPI scale factor that the owning window sets (one source of
/// truth for DPI, so a layout pass and a paint pass never disagree).
/// </summary>
public abstract class ThemedControl : Control
{
    private ThemePalette _palette = Theme.Current;
    private float _uiScale = 1f;

    protected ThemedControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemePalette Palette
    {
        get => _palette;
        set
        {
            if (_palette != value)
            {
                _palette = value;
                OnPaletteChanged();
                Invalidate(true);
            }
        }
    }

    /// <summary>Device pixels per logical (96 DPI) pixel.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float UiScale
    {
        get => _uiScale;
        set
        {
            if (Math.Abs(_uiScale - value) > 0.001f)
            {
                _uiScale = value;
                OnUiScaleChanged();
                Invalidate(true);
            }
        }
    }

    /// <summary>Logical pixels to device pixels.</summary>
    protected int S(int logical) => Theme.Scale(logical, _uiScale);

    protected virtual void OnPaletteChanged()
    {
    }

    protected virtual void OnUiScaleChanged()
    {
    }
}
