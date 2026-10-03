namespace Hatoclip;

/// <summary>
/// Tray icon images, embedded from assets/ (replace the .ico files to rebrand):
/// hatoclip.ico (idle) and hatoclip-received.ico (shown for a few seconds after text arrives).
/// </summary>
public static class TrayIcons
{
    public static Icon LoadIdle() => Load("hatoclip.ico", SystemInformation.SmallIconSize);

    public static Icon LoadReceived() => Load("hatoclip-received.ico", SystemInformation.SmallIconSize);

    /// <summary>Application icon for windows (title bar / taskbar).</summary>
    public static Icon LoadWindowIcon() => Load("hatoclip.ico", new Size(32, 32));

    private static Icon Load(string fileName, Size size)
    {
        var bytes = EmbeddedAssets.GetAsset(fileName);
        if (bytes is not null)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                return new Icon(ms, size);
            }
            catch (Exception ex)
            {
                Log.Warn($"Icon {fileName} could not be loaded ({ex.GetType().Name}); using default icon.");
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
