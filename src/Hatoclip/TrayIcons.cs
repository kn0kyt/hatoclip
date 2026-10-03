namespace Hatoclip;

/// <summary>
/// Tray icon images. The placeholder pigeon lives in assets/hatoclip.ico (embedded);
/// replace that file to rebrand. Phase 2/3 can add a "just received" variant here.
/// </summary>
public static class TrayIcons
{
    public static Icon LoadIdle()
    {
        var bytes = EmbeddedAssets.GetTrayIconBytes();
        if (bytes is not null)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                return new Icon(ms, SystemInformation.SmallIconSize);
            }
            catch (Exception ex)
            {
                Log.Warn($"Tray icon could not be loaded ({ex.GetType().Name}); using default icon.");
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
