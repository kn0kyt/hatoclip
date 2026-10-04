namespace Hatoclip;

/// <summary>Text clipboard used by the HTTP API (a seam so the server can be tested without the real clipboard).</summary>
public interface IClipboardService
{
    /// <summary>
    /// Places non-empty text on the clipboard. With <paramref name="excludeFromHistory"/> the text is
    /// kept out of Windows clipboard history and cloud clipboard sync.
    /// Throws <see cref="System.Runtime.InteropServices.ExternalException"/> if the clipboard stays busy.
    /// </summary>
    Task SetTextAsync(string text, bool excludeFromHistory);

    /// <summary>
    /// Returns (text, "text"), or ("", "non-text") when the clipboard holds only non-text data.
    /// Throws <see cref="System.Runtime.InteropServices.ExternalException"/> if the clipboard stays busy.
    /// </summary>
    Task<(string Text, string Type)> GetTextAsync();
}
