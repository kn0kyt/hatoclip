using System.Collections.Concurrent;
using System.Reflection;

namespace Hatoclip;

/// <summary>Reads files embedded in the assembly (Web/** and assets/*).</summary>
public static class EmbeddedAssets
{
    private static readonly Assembly Assembly = typeof(EmbeddedAssets).Assembly;
    private static readonly ConcurrentDictionary<string, byte[]?> Cache = new();

    /// <summary>Returns the bytes of assets/&lt;fileName&gt;, or null if it is not embedded.</summary>
    public static byte[]? GetAsset(string fileName) => Get("Hatoclip.assets." + fileName);

    /// <summary>Returns the bytes of the resource with the given logical name (e.g. "Hatoclip.Web.app.js"), or null.</summary>
    public static byte[]? Get(string logicalName) => Cache.GetOrAdd(logicalName, name =>
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
