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

    /// <summary>True when no config file existed (phase 2 uses this to auto-show the QR window).</summary>
    public bool IsFirstRun { get; private set; }

    public void Load()
    {
        lock (_gate)
        {
            AppConfig? loaded = null;
            if (File.Exists(_path))
            {
                try
                {
                    loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path), JsonOptions);
                }
                catch (Exception ex)
                {
                    Log.Error($"Config file is unreadable; regenerating ({ex.GetType().Name}).");
                }
            }
            else
            {
                IsFirstRun = true;
            }

            var config = loaded ?? new AppConfig();
            var dirty = loaded is null;

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

    /// <summary>128-bit random key as 32 lowercase hex characters.</summary>
    public static string GenerateKey() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

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
