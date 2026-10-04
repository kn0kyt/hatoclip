namespace Hatoclip;

/// <summary>Confirms and performs connection-key regeneration. UI thread only.</summary>
public sealed class KeyRegenerator
{
    private readonly ConfigStore _config;
    private readonly AuthRateLimiter _rateLimiter;
    private readonly ConnectionMonitor _monitor;

    public KeyRegenerator(ConfigStore config, AuthRateLimiter rateLimiter, ConnectionMonitor monitor)
    {
        _config = config;
        _rateLimiter = rateLimiter;
        _monitor = monitor;
    }

    /// <summary>Shows the confirmation dialog; on Yes regenerates the key. Returns true if regenerated.</summary>
    public bool ConfirmAndRegenerate(IWin32Window? owner)
    {
        var answer = MessageBox.Show(
            owner,
            "接続キーを再生成すると、今までに接続したスマホはすべて接続できなくなります。再生成しますか？",
            "Hatoclip",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
        {
            return false;
        }

        try
        {
            _config.RegenerateKey();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save regenerated key.", ex);
            MessageBox.Show(owner, $"接続キーを保存できませんでした。\n\n{ex.Message}", "Hatoclip",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        _rateLimiter.Reset();
        Log.Info("Connection key regenerated.");
        _monitor.RequestRefresh();
        return true;
    }
}
