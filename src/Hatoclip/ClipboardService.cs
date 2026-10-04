using System.Runtime.InteropServices;

namespace Hatoclip;

/// <summary>Text-only clipboard access. Every clipboard call is executed on the UI (STA) thread.</summary>
public sealed class ClipboardService : IClipboardService
{
    /// <summary>Any data: clipboard managers / history should ignore this content.</summary>
    public const string ExcludeFromMonitorFormat = "ExcludeClipboardContentFromMonitorProcessing";

    /// <summary>DWORD 0: do not add this content to Windows clipboard history (Win+V).</summary>
    public const string CanIncludeInHistoryFormat = "CanIncludeInClipboardHistory";

    /// <summary>DWORD 0: do not sync this content to the cloud clipboard.</summary>
    public const string CanUploadToCloudFormat = "CanUploadToCloudClipboard";

    private const int MaxAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(80);

    private readonly UiDispatcher _ui;

    public ClipboardService(UiDispatcher ui)
    {
        _ui = ui;
    }

    /// <summary>Sets non-empty text (an empty string would clear the clipboard; the server rejects it).</summary>
    public Task SetTextAsync(string text, bool excludeFromHistory) => WithRetryAsync(() =>
    {
        // copy: true keeps the data on the clipboard after the app exits (same as Clipboard.SetText).
        Clipboard.SetDataObject(CreateTextDataObject(text, excludeFromHistory), copy: true);
        return true;
    });

    /// <summary>Returns (text, "text"), or ("", "non-text") when the clipboard holds only non-text data.</summary>
    public Task<(string Text, string Type)> GetTextAsync() => WithRetryAsync(() =>
    {
        if (Clipboard.ContainsText(TextDataFormat.UnicodeText) || Clipboard.ContainsText(TextDataFormat.Text))
        {
            return (Clipboard.GetText(TextDataFormat.UnicodeText), "text");
        }

        var data = Clipboard.GetDataObject();
        var hasData = data is not null && data.GetFormats().Length > 0;
        return ("", hasData ? "non-text" : "text");
    });

    /// <summary>
    /// Unicode text, plus, when <paramref name="excludeFromHistory"/> is set, the documented clipboard
    /// formats that keep it out of clipboard history and cloud sync
    /// (https://learn.microsoft.com/windows/win32/dataxchg/clipboard-formats#cloud-clipboard-and-clipboard-history-formats).
    /// Streams are placed on the clipboard as raw HGLOBAL bytes.
    /// </summary>
    public static DataObject CreateTextDataObject(string text, bool excludeFromHistory)
    {
        var data = new DataObject();
        // Same as Clipboard.SetText(text, UnicodeText): CF_UNICODETEXT only; Windows synthesizes
        // CF_TEXT / CF_OEMTEXT for programs that ask for ANSI text.
        data.SetData(DataFormats.UnicodeText, false, text);
        if (excludeFromHistory)
        {
            data.SetData(ExcludeFromMonitorFormat, false, DwordZero());
            data.SetData(CanIncludeInHistoryFormat, false, DwordZero());
            data.SetData(CanUploadToCloudFormat, false, DwordZero());
        }

        return data;
    }

    private static MemoryStream DwordZero() => new(BitConverter.GetBytes(0));

    private async Task<T> WithRetryAsync<T>(Func<T> op)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _ui.InvokeAsync(op).ConfigureAwait(false);
            }
            catch (ExternalException) when (attempt < MaxAttempts)
            {
                // Clipboard is locked by another application; back off without blocking the UI thread.
                await Task.Delay(RetryDelay).ConfigureAwait(false);
            }
        }
    }
}
