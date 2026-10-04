using System.Runtime.InteropServices;

namespace Hatoclip;

/// <summary>
/// Per-session single-instance guard. The first instance owns a named mutex and waits on a
/// named event; a second instance sets the event (asking the first to show its QR window) and exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Hatoclip-SingleInstance-7c1e2f4a";
    private const string ShowEventName = @"Local\Hatoclip-ShowQr-7c1e2f4a";
    private const int AsfwAny = -1;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly ManualResetEvent _stop = new(false);
    private Thread? _listener;

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
    }

    /// <summary>
    /// Returns the guard if this is the first instance. Otherwise signals the running instance to come
    /// to the front and returns null (the caller should exit).
    /// </summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        // Created right after the mutex so a second instance always finds it alive.
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (createdNew)
        {
            return new SingleInstance(mutex, showEvent);
        }

        try
        {
            // We were just launched by the user, so we hold foreground rights; pass them on so the
            // running instance may bring its window to the front.
            AllowSetForegroundWindow(AsfwAny);
            showEvent.Set();
        }
        finally
        {
            showEvent.Dispose();
            mutex.Dispose();
        }

        return null;
    }

    /// <summary>Starts a background thread that invokes <paramref name="onShowRequested"/> (on that thread) per signal.</summary>
    public void Listen(Action onShowRequested)
    {
        if (_listener is not null)
        {
            return;
        }

        _listener = new Thread(() =>
        {
            var handles = new WaitHandle[] { _stop, _showEvent };
            while (WaitHandle.WaitAny(handles) == 1)
            {
                Log.Info("Show requested by second instance.");
                try
                {
                    onShowRequested();
                }
                catch (Exception ex)
                {
                    Log.Error("Show request handler failed.", ex);
                }
            }
        })
        {
            IsBackground = true,
            Name = "Hatoclip single-instance listener",
        };
        _listener.Start();
    }

    public void Dispose()
    {
        _stop.Set();
        _listener?.Join(TimeSpan.FromSeconds(1));
        _showEvent.Dispose();
        _stop.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread (should not happen); disposing still closes the handle.
        }

        _mutex.Dispose();
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}
