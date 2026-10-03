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

    /// <summary>Set once the QR window has been shown automatically on first run.</summary>
    [JsonPropertyName("firstRunShown")]
    public bool FirstRunShown { get; set; }

    /// <summary>iCloud link of the iOS "send" Shortcut shown on the web page ("" = not set up).</summary>
    [JsonPropertyName("shortcutSendUrl")]
    public string ShortcutSendUrl { get; set; } = "";

    /// <summary>iCloud link of the iOS "receive" Shortcut shown on the web page ("" = not set up).</summary>
    [JsonPropertyName("shortcutReceiveUrl")]
    public string ShortcutReceiveUrl { get; set; } = "";
}
