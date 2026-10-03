using System.Text.Json.Serialization;

namespace Hatoclip;

/// <summary>Persisted settings (%LOCALAPPDATA%\Hatoclip\config.json).</summary>
public sealed class AppConfig
{
    public const int DefaultPort = 52817;

    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("port")]
    public int Port { get; set; } = DefaultPort;

    [JsonPropertyName("allowGet")]
    public bool AllowGet { get; set; } = true;
}
