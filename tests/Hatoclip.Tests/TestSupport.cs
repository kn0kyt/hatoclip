using System.Runtime.InteropServices;

[assembly: AssemblyFixture(typeof(Hatoclip.Tests.LogDisabledFixture))]

namespace Hatoclip.Tests;

/// <summary>Keeps the app's file logger quiet for the whole test run (no files under %LOCALAPPDATA%).</summary>
public sealed class LogDisabledFixture
{
    public LogDisabledFixture()
    {
        Log.Enabled = false;
    }
}

/// <summary>Manually advanced clock for <see cref="AuthRateLimiter"/>.</summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>In-memory clipboard for the HTTP tests.</summary>
public sealed class FakeClipboard : IClipboardService
{
    public string Text { get; set; } = "";

    public bool? LastExcludeFromHistory { get; private set; }

    public int SetCount { get; private set; }

    /// <summary>When true every call throws as if another app held the clipboard.</summary>
    public bool Busy { get; set; }

    public Task SetTextAsync(string text, bool excludeFromHistory)
    {
        if (Busy)
        {
            throw new ExternalException("busy");
        }

        Text = text;
        LastExcludeFromHistory = excludeFromHistory;
        SetCount++;
        return Task.CompletedTask;
    }

    public Task<(string Text, string Type)> GetTextAsync()
    {
        if (Busy)
        {
            throw new ExternalException("busy");
        }

        return Task.FromResult((Text, "text"));
    }
}

/// <summary>A fresh temporary directory, deleted on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hatoclip-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
