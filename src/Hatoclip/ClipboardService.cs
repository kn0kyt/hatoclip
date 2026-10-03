using System.Runtime.InteropServices;

namespace Hatoclip;

/// <summary>Text-only clipboard access. Every clipboard call is executed on the UI (STA) thread.</summary>
public sealed class ClipboardService
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(80);

    private readonly UiDispatcher _ui;

    public ClipboardService(UiDispatcher ui)
    {
        _ui = ui;
    }

    public Task SetTextAsync(string text) => WithRetryAsync(() =>
    {
        if (text.Length == 0)
        {
            Clipboard.Clear();
        }
        else
        {
            Clipboard.SetText(text, TextDataFormat.UnicodeText);
        }

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
