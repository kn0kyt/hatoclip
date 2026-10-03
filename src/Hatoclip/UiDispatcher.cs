namespace Hatoclip;

/// <summary>
/// Marshals work onto the WinForms UI (STA) thread from any thread, as an awaitable Task.
/// Must be constructed on the UI thread.
/// </summary>
public sealed class UiDispatcher : IDisposable
{
    private readonly Control _control;

    public UiDispatcher()
    {
        _control = new Control();
        _control.CreateControl();
        _ = _control.Handle; // force handle creation on the current (UI) thread
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _control.BeginInvoke(() =>
            {
                try
                {
                    tcs.SetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            tcs.TrySetException(ex);
        }

        return tcs.Task;
    }

    /// <summary>Fire-and-forget post to the UI thread.</summary>
    public void Post(Action action)
    {
        try
        {
            _control.BeginInvoke(action);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // Handle destroyed during shutdown.
        }
    }

    public void Dispose() => _control.Dispose();
}
