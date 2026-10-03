namespace Hatoclip;

internal static class Program
{
    // Per-session single-instance lock. Phase 2 will signal the running instance to show its QR window.
    private const string MutexName = @"Local\Hatoclip-SingleInstance-7c1e2f4a";

    [STAThread]
    private static int Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            ApplicationConfiguration.Initialize();
            MessageBox.Show("Hatoclip はすでに起動しています。", "Hatoclip", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("Unhandled UI thread exception.", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("Unhandled exception.", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception.", e.Exception);
            e.SetObserved();
        };

        Log.Info($"Hatoclip {Application.ProductVersion} starting.");

        var config = new ConfigStore(AppPaths.ConfigFile);
        try
        {
            config.Load();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load or save config.", ex);
            MessageBox.Show($"設定ファイルを読み書きできませんでした。\n{AppPaths.ConfigFile}\n\n{ex.Message}",
                "Hatoclip", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        using var ui = new UiDispatcher();
        var clipboard = new ClipboardService(ui);
        var server = new HatoclipServer(config, clipboard, new AuthRateLimiter());

        try
        {
            // Run off the UI thread so no continuation tries to resume on the (not yet pumping) message loop.
            Task.Run(server.StartAsync).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to start server.", ex);
            MessageBox.Show($"サーバーを起動できませんでした。\n\n{ex.Message}",
                "Hatoclip", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        try
        {
            using var context = new TrayApplicationContext(config, server, ui);
            Application.Run(context);
        }
        finally
        {
            if (!Task.Run(server.StopAsync).Wait(TimeSpan.FromSeconds(5)))
            {
                Log.Warn("Server did not stop within 5 seconds.");
            }

            Log.Info("Hatoclip exiting.");
        }

        return 0;
    }
}
