using QRCoder;

namespace Hatoclip;

/// <summary>Renders text as a QR code image (QRCoder, ECC level M, with quiet zone).</summary>
public static class QrCodeRenderer
{
    /// <summary>
    /// PNG bytes of the QR code, scaled by the largest whole number of pixels per module that fits in
    /// <paramref name="maxPixels"/> (whole-pixel modules keep the code crisp for phone cameras).
    /// </summary>
    public static byte[] RenderPng(string text, int maxPixels)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var modules = data.ModuleMatrix.Count; // includes the 4-module quiet zone on each side
        var pixelsPerModule = Math.Max(1, maxPixels / modules);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }

    public static Bitmap Render(string text, int maxPixels)
    {
        using var ms = new MemoryStream(RenderPng(text, maxPixels));
        using var decoded = new Bitmap(ms);
        return new Bitmap(decoded); // detach from the stream
    }
}
