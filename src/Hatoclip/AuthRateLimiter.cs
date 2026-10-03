using System.Collections.Concurrent;
using System.Net;

namespace Hatoclip;

/// <summary>
/// Simple in-memory lockout: after <see cref="MaxFailures"/> auth failures from one IP within
/// <see cref="Window"/>, that IP is rejected (429) for <see cref="BlockDuration"/>.
/// </summary>
public sealed class AuthRateLimiter
{
    public const int MaxFailures = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly TimeProvider _time;

    public AuthRateLimiter(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    public bool IsBlocked(IPAddress? ip)
    {
        if (!_entries.TryGetValue(Key(ip), out var entry))
        {
            return false;
        }

        lock (entry)
        {
            return entry.BlockedUntil is { } until && until > _time.GetUtcNow();
        }
    }

    /// <summary>Records a failure; returns true if the IP has just become blocked.</summary>
    public bool RecordFailure(IPAddress? ip)
    {
        var now = _time.GetUtcNow();
        var entry = _entries.GetOrAdd(Key(ip), _ => new Entry());
        lock (entry)
        {
            if (entry.BlockedUntil is { } until && until <= now)
            {
                entry.BlockedUntil = null;
                entry.Failures.Clear();
            }

            while (entry.Failures.Count > 0 && now - entry.Failures.Peek() > Window)
            {
                entry.Failures.Dequeue();
            }

            entry.Failures.Enqueue(now);
            if (entry.BlockedUntil is null && entry.Failures.Count >= MaxFailures)
            {
                entry.BlockedUntil = now + BlockDuration;
                return true;
            }

            return false;
        }
    }

    /// <summary>Clears the failure history of an IP after a successful authentication.</summary>
    public void RecordSuccess(IPAddress? ip)
    {
        var key = Key(ip);
        if (_entries.TryGetValue(key, out var entry))
        {
            lock (entry)
            {
                if (entry.BlockedUntil is null)
                {
                    entry.Failures.Clear();
                }
            }
        }
    }

    /// <summary>Forgets all failure history and blocks (used after the connection key is regenerated).</summary>
    public void Reset() => _entries.Clear();

    private static string Key(IPAddress? ip)
    {
        if (ip is null)
        {
            return "unknown";
        }

        return (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
    }

    private sealed class Entry
    {
        public Queue<DateTimeOffset> Failures { get; } = new();

        public DateTimeOffset? BlockedUntil { get; set; }
    }
}
