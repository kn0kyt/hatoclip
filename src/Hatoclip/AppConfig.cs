using System.Text.Json.Serialization;

namespace Hatoclip;

/// <summary>Persisted settings (%LOCALAPPDATA%\Hatoclip\config.json).</summary>
public sealed class AppConfig
{
    public const int DefaultPort = 52817;

    /// <summary>
    /// JSON names of settings added after the first release. A config file lacking any of them is
    /// written back on load so the user can see (and edit) every setting.
    /// </summary>
    internal static readonly string[] OptionalKeys =
        ["showPreview", "excludeFromHistory", "shortcutSendUrl", "shortcutReceiveUrl"];

    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("port")]
    public int Port { get; set; } = DefaultPort;

    [JsonPropertyName("allowGet")]
    public bool AllowGet { get; set; } = true;

    /// <summary>Show the first characters of received text in the tray notification.</summary>
    [JsonPropertyName("showPreview")]
    public bool ShowPreview { get; set; } = true;

    /// <summary>
    /// Place received text on the clipboard so that Windows keeps it out of clipboard history (Win+V)
    /// and cloud clipboard sync.
    /// </summary>
    [JsonPropertyName("excludeFromHistory")]
    public bool ExcludeFromHistory { get; set; }

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
