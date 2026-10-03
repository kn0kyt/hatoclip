namespace Hatoclip;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // A second instance asks the running one to show its QR window, then exits silently.
        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
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
        Log.DeleteOldFiles();

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
        var rateLimiter = new AuthRateLimiter();
        var server = new HatoclipServer(config, clipboard, rateLimiter);

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
            using var context = new TrayApplicationContext(config, server, ui, rateLimiter);
            instance.Listen(() => ui.Post(context.ShowQrWindow));
            if (config.CorruptBackupPath is { } backup)
            {
                // Posted after the first-run QR window (a fresh config has not shown it yet).
                ui.Post(() => MessageBox.Show(
                    "設定ファイルが壊れていたため、新しい設定を作成しました。接続キーが変わったので、スマホで QR コードを読み直してください。" +
                    $"（元のファイルは {Path.GetFileName(backup)} として残しています）",
                    "Hatoclip", MessageBoxButtons.OK, MessageBoxIcon.Warning));
            }

            Application.Run(context);
        }
        finally
        {
            if (!Task.Run(server.StopAsync).Wait(TimeSpan.FromSeconds(5)))
            {
                Log.Warn("Server did not stop within 5 seconds.");
            }

            Log.Info("Hatoclip exiting.");
            Log.Close();
        }

        return 0;
    }
}
