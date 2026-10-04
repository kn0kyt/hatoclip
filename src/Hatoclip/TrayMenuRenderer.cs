using System.Drawing.Drawing2D;

namespace Hatoclip;

/// <summary>
/// Flat look for the tray context menu in the <see cref="Theme"/> palette: card-coloured background,
/// rounded accent-soft highlight, a drawn check mark, separators in the border colour. Colours are read
/// from <see cref="Theme.Current"/> at paint time, so a theme change applies on the next opening.
/// </summary>
public sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    public TrayMenuRenderer()
        : base(new ThemeColorTable())
    {
        RoundedEdges = false;
    }

    /// <summary>False when Windows 11 already rounds the menu window and draws its border.</summary>
    public bool DrawBorder { get; set; } = true;

    /// <summary>Applies font, renderer, padding and colours to <paramref name="menu"/> (items and order unchanged).</summary>
    public static void Apply(ContextMenuStrip menu)
    {
        var renderer = new TrayMenuRenderer();
        menu.Renderer = renderer;
        menu.Font = Theme.CreatePointFont(Theme.MenuFontSize);
        menu.Padding = new Padding(4, 6, 4, 6);
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = true;
        foreach (ToolStripItem item in menu.Items)
        {
            if (item is ToolStripMenuItem)
            {
                item.Padding = new Padding(4, 5, 12, 5);
            }
        }

        menu.HandleCreated += (_, _) =>
        {
            // Windows 11: small rounded corners and a themed border drawn by DWM; Windows 10: our own border.
            if (NativeTheme.SetCornerPreference(menu.Handle, NativeTheme.CornerPreference.RoundSmall))
            {
                renderer.DrawBorder = false;
                NativeTheme.SetBorderColor(menu.Handle, Theme.Current.Border);
            }
        };
        menu.Opening += (_, _) =>
        {
            var p = Theme.Current;
            menu.BackColor = p.Card;
            menu.ForeColor = p.Text;
            if (menu.IsHandleCreated && !renderer.DrawBorder)
            {
                NativeTheme.SetBorderColor(menu.Handle, p.Border);
            }
        };
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Theme.Current.Card);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (!DrawBorder)
        {
            return;
        }

        using var pen = new Pen(Theme.Current.Border);
        var r = e.AffectedBounds;
        e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Same colour as the menu: no separate margin band.
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled)
        {
            return;
        }

        var g = e.Graphics;
        var scale = e.Item.Owner?.DeviceDpi / 96f ?? 1f;
        // Inset from the menu's own edges (item bounds can reach under the menu's padding), in item coordinates.
        var inset = Theme.Scale(4, scale);
        var ownerWidth = e.ToolStrip?.Width ?? e.Item.Width;
        var left = Math.Max(0, inset - e.Item.Bounds.X);
        var right = Math.Min(e.Item.Width, ownerWidth - inset - e.Item.Bounds.X);
        var bounds = Rectangle.FromLTRB(left, Theme.Scale(1, scale), right, e.Item.Height - Theme.Scale(1, scale));
        Drawing.FillRoundedRect(g, bounds, Theme.Scale(6, scale), Theme.Current.AccentSoft, null, 0);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var p = Theme.Current;
        e.TextColor = e.Item.Enabled ? p.Text : p.Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        var r = e.ImageRectangle;
        var scale = e.Item.Owner?.DeviceDpi / 96f ?? 1f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.Current.Accent, Math.Max(1.5f, 1.8f * scale))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        // A check mark centred in the check margin, about 10 logical px wide.
        var cx = r.X + r.Width / 2f;
        var cy = r.Y + r.Height / 2f;
        var u = 5f * scale;
        g.DrawLines(pen, new PointF[]
        {
            new(cx - u, cy + 0.1f * u),
            new(cx - 0.3f * u, cy + 0.75f * u),
            new(cx + u, cy - 0.75f * u),
        });
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var scale = e.Item.Owner?.DeviceDpi / 96f ?? 1f;
        var inset = Theme.Scale(8, scale);
        var y = e.Item.Height / 2;
        using var pen = new Pen(Theme.Current.Border);
        e.Graphics.DrawLine(pen, inset, y, e.Item.Width - inset, y);
    }

    /// <summary>Colours used by the base renderer where we do not override the painting.</summary>
    private sealed class ThemeColorTable : ProfessionalColorTable
    {
        public ThemeColorTable()
        {
            UseSystemColors = false;
        }

        public override Color ToolStripDropDownBackground => Theme.Current.Card;

        public override Color ImageMarginGradientBegin => Theme.Current.Card;

        public override Color ImageMarginGradientMiddle => Theme.Current.Card;

        public override Color ImageMarginGradientEnd => Theme.Current.Card;

        public override Color MenuBorder => Theme.Current.Border;

        public override Color MenuItemBorder => Theme.Current.AccentSoft;

        public override Color MenuItemSelected => Theme.Current.AccentSoft;

        public override Color SeparatorDark => Theme.Current.Border;

        public override Color SeparatorLight => Theme.Current.Border;

        public override Color CheckBackground => Theme.Current.Card;

        public override Color CheckSelectedBackground => Theme.Current.AccentSoft;

        public override Color CheckPressedBackground => Theme.Current.AccentSoft;
    }
}
