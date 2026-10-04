using System.Drawing.Drawing2D;

namespace Hatoclip;

/// <summary>Small GDI+ helpers shared by the custom-painted controls.</summary>
internal static class Drawing
{
    /// <summary>A rounded rectangle path; <paramref name="radius"/> is clamped to half the shorter side.</summary>
    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Fills a rounded rectangle and draws a crisp 1-device-pixel-or-more border inside
    /// <paramref name="bounds"/> (the border's outer edge is the bounds' edge).
    /// </summary>
    public static void FillRoundedRect(Graphics g, Rectangle bounds, float radius, Color fill, Color? border, float borderWidth)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using (var path = RoundedRect(bounds, radius))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
        }

        if (border is { } c && borderWidth > 0)
        {
            var half = borderWidth / 2f;
            var inner = new RectangleF(bounds.X + half, bounds.Y + half, bounds.Width - borderWidth, bounds.Height - borderWidth);
            using var path = RoundedRect(inner, Math.Max(0, radius - half));
            using var pen = new Pen(c, borderWidth);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>Border width in device pixels for a 1-logical-pixel line (1px at 100–174%, 2px at 175%+).</summary>
    public static float HairlineWidth(float scale) => Math.Max(1f, (float)Math.Floor(scale + 0.25f));

    /// <summary>
    /// The Hatoclip pigeon, the same silhouette as the phone page's SVG symbol (48×48 grid),
    /// drawn into <paramref name="bounds"/>.
    /// </summary>
    public static void DrawPigeon(Graphics g, RectangleF bounds, ThemePalette palette)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TranslateTransform(bounds.X, bounds.Y);
        g.ScaleTransform(bounds.Width / 48f, bounds.Height / 48f);

        // Each shape is filled on its own (same opaque colour), so overlaps never cancel out.
        using (var body = new SolidBrush(palette.PigeonBody))
        using (var neck = new GraphicsPath())
        {
            // tail
            g.FillPolygon(body, new PointF[] { new(13, 30), new(2, 35.5f), new(4, 37), new(10, 37.5f), new(18, 37) });
            // body
            g.FillEllipse(body, 24 - 15, 29.5f - 10, 30, 20);
            // neck: M30,10 Q23,17 14,23.5 L30,34 L38.5,27 Q41.5,23 41,19 Z
            AddQuadratic(neck, new PointF(30, 10), new PointF(23, 17), new PointF(14, 23.5f));
            neck.AddLine(14, 23.5f, 30, 34);
            neck.AddLine(30, 34, 38.5f, 27);
            AddQuadratic(neck, new PointF(38.5f, 27), new PointF(41.5f, 23), new PointF(41, 19));
            neck.CloseFigure();
            g.FillPath(body, neck);
            // head
            g.FillEllipse(body, 35 - 7.5f, 15.5f - 7.5f, 15, 15);
        }

        using (var wing = new SolidBrush(palette.PigeonWing))
        {
            g.FillEllipse(wing, 24 - 9.8f, 28 - 5, 19.6f, 10);
        }

        using (var beak = new SolidBrush(palette.PigeonBeak))
        {
            g.FillPolygon(beak, new PointF[] { new(41.5f, 14.5f), new(47, 16.5f), new(41.5f, 18.5f) });
        }

        g.FillEllipse(Brushes.White, 36.5f - 2.4f, 14.5f - 2.4f, 4.8f, 4.8f);
        using (var pupil = new SolidBrush(Color.FromArgb(0x1d, 0x24, 0x33)))
        {
            g.FillEllipse(pupil, 37 - 1, 14.5f - 1, 2, 2);
        }

        g.Restore(state);
    }

    /// <summary>Adds an SVG-style quadratic Bézier (start, control, end) as the equivalent cubic.</summary>
    private static void AddQuadratic(GraphicsPath path, PointF p0, PointF c, PointF p1)
    {
        var c1 = new PointF(p0.X + 2f / 3f * (c.X - p0.X), p0.Y + 2f / 3f * (c.Y - p0.Y));
        var c2 = new PointF(p1.X + 2f / 3f * (c.X - p1.X), p1.Y + 2f / 3f * (c.Y - p1.Y));
        path.AddBezier(p0, c1, c2, p1);
    }
}
