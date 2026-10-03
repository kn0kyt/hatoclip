using System.Collections.Concurrent;
using System.Reflection;

namespace Hatoclip;

/// <summary>Reads files embedded in the assembly (Web/** and assets/*).</summary>
public static class EmbeddedAssets
{
    private const string TrayIconName = "Hatoclip.assets.hatoclip.ico";

    private static readonly Assembly Assembly = typeof(EmbeddedAssets).Assembly;
    private static readonly ConcurrentDictionary<string, byte[]?> Cache = new();

    /// <summary>Returns the bytes of Web/&lt;fileName&gt;, or null if it is not embedded.</summary>
    public static byte[]? GetWebFile(string fileName) => Get("Hatoclip.Web." + fileName);

    public static byte[]? GetTrayIconBytes() => Get(TrayIconName);

    private static byte[]? Get(string logicalName) => Cache.GetOrAdd(logicalName, name =>
    {
        using var stream = Assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return null;
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    });
}
