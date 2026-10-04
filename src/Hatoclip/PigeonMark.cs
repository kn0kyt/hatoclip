namespace Hatoclip;

/// <summary>The pigeon mark of the window header, drawn as vectors so it is crisp at any DPI.</summary>
public sealed class PigeonMark : ThemedControl
{
    public PigeonMark()
    {
        AccessibleRole = AccessibleRole.Graphic;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Palette.Background);
        var side = Math.Min(ClientSize.Width, ClientSize.Height);
        var bounds = new RectangleF((ClientSize.Width - side) / 2f, (ClientSize.Height - side) / 2f, side, side);
        Drawing.DrawPigeon(e.Graphics, bounds, Palette);
    }
}
