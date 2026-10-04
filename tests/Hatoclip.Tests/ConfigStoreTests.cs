using System.Text.Json;

namespace Hatoclip.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    private string ConfigPath => _dir.File("config.json");

    public void Dispose() => _dir.Dispose();

    private ConfigStore LoadStore()
    {
        var store = new ConfigStore(ConfigPath);
        store.Load();
        return store;
    }

    private JsonElement ReadFile() => JsonDocument.Parse(File.ReadAllText(ConfigPath)).RootElement;

    [Fact]
    public void First_run_creates_a_config_with_a_128_bit_hex_key()
    {
        var store = LoadStore();

        Assert.True(store.IsFirstRun);
        Assert.Matches("^[0-9a-f]{32}$", store.Key);
        Assert.True(File.Exists(ConfigPath));
        Assert.Equal(store.Key, ReadFile().GetProperty("key").GetString());
    }

    [Fact]
    public void First_run_writes_the_defaults()
    {
        LoadStore();
        var json = ReadFile();

        Assert.Equal(AppConfig.DefaultPort, json.GetProperty("port").GetInt32());
        Assert.True(json.GetProperty("allowGet").GetBoolean());
        Assert.True(json.GetProperty("showPreview").GetBoolean());
        Assert.False(json.GetProperty("excludeFromHistory").GetBoolean());
        Assert.Equal("", json.GetProperty("shortcutSendUrl").GetString());
    }

    [Fact]
    public void Values_round_trip()
    {
        var store = LoadStore();
        store.Update(c =>
        {
            c.AllowGet = false;
            c.ShowPreview = false;
            c.ExcludeFromHistory = true;
            c.Port = 50000;
            c.ShortcutSendUrl = "https://www.icloud.com/shortcuts/abc";
        });

        var reloaded = LoadStore();

        Assert.False(reloaded.IsFirstRun);
        Assert.Equal(store.Key, reloaded.Key);
        Assert.False(reloaded.Current.AllowGet);
        Assert.False(reloaded.Current.ShowPreview);
        Assert.True(reloaded.Current.ExcludeFromHistory);
        Assert.Equal(50000, reloaded.Current.Port);
        Assert.Equal("https://www.icloud.com/shortcuts/abc", reloaded.Current.ShortcutSendUrl);
    }

    [Fact]
    public void Old_config_without_new_keys_gets_defaults_and_is_written_back()
    {
        const string key = "0123456789abcdef0123456789abcdef";
        File.WriteAllText(ConfigPath, $$"""{ "key": "{{key}}", "port": 52817, "allowGet": false, "firstRunShown": true }""");

        var store = LoadStore();

        Assert.Equal(key, store.Key);
        Assert.False(store.Current.AllowGet);
        Assert.True(store.Current.ShowPreview);
        Assert.False(store.Current.ExcludeFromHistory);

        var json = ReadFile();
        Assert.Equal(key, json.GetProperty("key").GetString());
        Assert.False(json.GetProperty("allowGet").GetBoolean());
        Assert.True(json.GetProperty("showPreview").GetBoolean());
        Assert.False(json.GetProperty("excludeFromHistory").GetBoolean());
        Assert.True(json.TryGetProperty("shortcutSendUrl", out _));
        Assert.True(json.TryGetProperty("shortcutReceiveUrl", out _));
    }

    [Fact]
    public void Complete_config_is_not_rewritten()
    {
        LoadStore();
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(ConfigPath, stamp);

        LoadStore();

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(ConfigPath));
    }

    [Fact]
    public void Corrupt_file_is_moved_to_bak_and_replaced()
    {
        File.WriteAllText(ConfigPath, "{ this is not json");

        var store = LoadStore();

        Assert.Equal(_dir.File("config.json.bak"), store.CorruptBackupPath);
        Assert.Equal("{ this is not json", File.ReadAllText(_dir.File("config.json.bak")));
        Assert.Matches("^[0-9a-f]{32}$", store.Key);
        Assert.Equal(store.Key, ReadFile().GetProperty("key").GetString());
    }

    [Fact]
    public void Corrupt_file_gets_a_timestamped_backup_when_bak_exists()
    {
        File.WriteAllText(_dir.File("config.json.bak"), "older backup");
        File.WriteAllText(ConfigPath, "not json either");

        var store = LoadStore();

        Assert.NotNull(store.CorruptBackupPath);
        Assert.Matches(@"^config\.\d{8}-\d{6}\.bak$", Path.GetFileName(store.CorruptBackupPath));
        Assert.Equal("not json either", File.ReadAllText(store.CorruptBackupPath!));
        Assert.Equal("older backup", File.ReadAllText(_dir.File("config.json.bak")));
    }

    [Fact]
    public void Invalid_key_and_port_are_replaced()
    {
        File.WriteAllText(ConfigPath, """{ "key": "short", "port": 80 }""");

        var store = LoadStore();

        Assert.Matches("^[0-9a-f]{32}$", store.Key);
        Assert.Equal(AppConfig.DefaultPort, store.Current.Port);
    }

    [Fact]
    public void RegenerateKey_changes_and_persists_the_key()
    {
        var store = LoadStore();
        var before = store.Key;

        store.RegenerateKey();

        Assert.NotEqual(before, store.Key);
        Assert.Matches("^[0-9a-f]{32}$", store.Key);
        Assert.Equal(store.Key, LoadStore().Key);
    }
}
