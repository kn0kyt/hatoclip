using System.Security.Cryptography;
using System.Text.Json;

namespace Hatoclip;

/// <summary>Loads/saves <see cref="AppConfig"/> and generates the connection key on first run.</summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly string _path;

    public ConfigStore(string path)
    {
        _path = path;
    }

    public AppConfig Current { get; private set; } = new();

    /// <summary>True when no config file existed when <see cref="Load"/> ran.</summary>
    public bool IsFirstRun { get; private set; }

    /// <summary>
    /// Full path of the backup made when <see cref="Load"/> found an unparsable config file and
    /// replaced it with a fresh one (new key); null otherwise.
    /// </summary>
    public string? CorruptBackupPath { get; private set; }

    /// <summary>
    /// The current connection key. The server reads it on every API request, so a regenerated key
    /// takes effect immediately without a restart.
    /// </summary>
    public string Key
    {
        get
        {
            lock (_gate)
            {
                return Current.Key;
            }
        }
    }

    public void Load()
    {
        lock (_gate)
        {
            AppConfig? loaded = null;
            var missingKeys = false;
            if (File.Exists(_path))
            {
                // I/O errors propagate (Program reports them); only a file that was read but cannot
                // be parsed is replaced, after keeping a copy so nothing is silently lost.
                var json = File.ReadAllText(_path);
                try
                {
                    loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                    missingKeys = loaded is not null && HasMissingShortcutKeys(json);
                }
                catch (JsonException ex)
                {
                    Log.Error($"Config file cannot be parsed ({ex.GetType().Name}).");
                }

                if (loaded is null)
                {
                    CorruptBackupPath = BackUpCorruptFile();
                    Log.Warn($"Backed up the unparsable config file to {Path.GetFileName(CorruptBackupPath)}; creating a new config.");
                }
            }
            else
            {
                IsFirstRun = true;
            }

            var config = loaded ?? new AppConfig();
            // Write back a config from an older version so the user can see where the optional
            // shortcut URLs go.
            var dirty = loaded is null || missingKeys;

            config.ShortcutSendUrl ??= ""; // "null" in the file
            config.ShortcutReceiveUrl ??= "";

            if (!IsValidKey(config.Key))
            {
                config.Key = GenerateKey();
                dirty = true;
                Log.Info("Generated a new connection key.");
            }

            if (config.Port is < 1024 or > 65535)
            {
                config.Port = AppConfig.DefaultPort;
                dirty = true;
            }

            Current = config;
            if (dirty)
            {
                SaveLocked();
            }
        }
    }

    /// <summary>Applies a change to the current config and persists it.</summary>
    public void Update(Action<AppConfig> change)
    {
        lock (_gate)
        {
            change(Current);
            SaveLocked();
        }
    }

    /// <summary>Replaces the connection key with a fresh random one and persists it.</summary>
    public void RegenerateKey()
    {
        lock (_gate)
        {
            var previous = Current.Key;
            Current.Key = GenerateKey();
            try
            {
                SaveLocked();
            }
            catch
            {
                Current.Key = previous; // keep memory and disk consistent
                throw;
            }
        }
    }

    /// <summary>128-bit random key as 32 lowercase hex characters.</summary>
    public static string GenerateKey() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    private static bool HasMissingShortcutKeys(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return root.ValueKind == JsonValueKind.Object
            && (!root.TryGetProperty("shortcutSendUrl", out _) || !root.TryGetProperty("shortcutReceiveUrl", out _));
    }

    /// <summary>
    /// Moves the config file to config.json.bak, or config.yyyyMMdd-HHmmss.bak if that already
    /// exists. Throws if the file cannot be moved, so it is never overwritten.
    /// </summary>
    private string BackUpCorruptFile()
    {
        var dir = Path.GetDirectoryName(_path)!;
        var backup = _path + ".bak";
        if (File.Exists(backup))
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            backup = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(_path)}.{stamp}.bak");
        }

        File.Move(_path, backup);
        return backup;
    }

    private static bool IsValidKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length >= 32;

    private void SaveLocked()
    {
        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOptions));
        File.Move(tmp, _path, overwrite: true);
    }
}
