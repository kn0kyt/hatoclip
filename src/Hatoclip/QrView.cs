using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Hatoclip;

/// <summary>
/// The QR code on a white rounded plate (white in dark mode too, so phone cameras always see dark
/// modules on a light background). The image is drawn 1:1 with nearest-neighbour sampling, so the
/// modules stay pixel-crisp; <see cref="QrCodeRenderer"/> already uses whole pixels per module.
/// </summary>
public sealed class QrView : ThemedControl
{
    private Bitmap? _image;

    public QrView()
    {
        AccessibleRole = AccessibleRole.Graphic;
    }

    /// <summary>Owned by this control: the previous image is disposed when replaced.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Bitmap? Image
    {
        get => _image;
        set
        {
            if (!ReferenceEquals(_image, value))
            {
                _image?.Dispose();
                _image = value;
                Invalidate();
            }
        }
    }

    /// <summary>Largest square image (device pixels) that fits inside the plate's border.</summary>
    public int ImageArea => Math.Max(0, Math.Min(ClientSize.Width, ClientSize.Height) - 2 * (int)Math.Ceiling(Drawing.HairlineWidth(UiScale)));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Palette.Card);
        Drawing.FillRoundedRect(g, ClientRectangle, S(Theme.FieldRadius), Color.White,
            Palette.IsDark ? null : Palette.Border, Drawing.HairlineWidth(UiScale));

        if (_image is { } image)
        {
            // Keep the (white) quiet-zone corners of the image inside the plate's rounded border.
            var border = Drawing.HairlineWidth(UiScale);
            var inner = RectangleF.Inflate(ClientRectangle, -border, -border);
            using var clip = Drawing.RoundedRect(inner, Math.Max(0, S(Theme.FieldRadius) - border));
            g.SetClip(clip);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.None;
            var x = (ClientSize.Width - image.Width) / 2;
            var y = (ClientSize.Height - image.Height) / 2;
            g.DrawImage(image, new Rectangle(x, y, image.Width, image.Height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image?.Dispose();
            _image = null;
        }

        base.Dispose(disposing);
    }
}
