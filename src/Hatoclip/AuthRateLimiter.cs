using System.Net;

namespace Hatoclip;

/// <summary>
/// Simple in-memory lockout: after <see cref="MaxFailures"/> auth failures from one IP within
/// <see cref="Window"/>, wrong keys from that IP are rejected (429) for <see cref="BlockDuration"/>;
/// the correct key is never blocked (the server checks it first). Entries that no longer matter are pruned, and the table size is capped.
/// </summary>
public sealed class AuthRateLimiter
{
    public const int MaxFailures = 10;
    public const int MaxEntries = 10_000;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly TimeProvider _time;
    private DateTimeOffset _lastPrune;

    public AuthRateLimiter(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _lastPrune = _time.GetUtcNow();
    }

    /// <summary>Number of tracked addresses (for tests).</summary>
    internal int EntryCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public bool IsBlocked(IPAddress? ip)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(Key(ip), out var entry) &&
                   entry.BlockedUntil is { } until && until > _time.GetUtcNow();
        }
    }

    /// <summary>Records a failure; returns true if the IP has just become blocked.</summary>
    public bool RecordFailure(IPAddress? ip)
    {
        var now = _time.GetUtcNow();
        var key = Key(ip);
        lock (_gate)
        {
            if (now - _lastPrune >= PruneInterval)
            {
                PruneLocked(now);
            }

            if (!_entries.TryGetValue(key, out var entry))
            {
                if (_entries.Count >= MaxEntries)
                {
                    PruneLocked(now);
                    if (_entries.Count >= MaxEntries)
                    {
                        RemoveOldestLocked();
                    }
                }

                entry = new Entry();
                _entries[key] = entry;
            }

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
            entry.LastFailure = now;
            if (entry.BlockedUntil is null && entry.Failures.Count >= MaxFailures)
            {
                entry.BlockedUntil = now + BlockDuration;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Clears the failure count of an IP after a successful authentication. An active block stays:
    /// the server lets the correct key through regardless, while wrong keys from that address
    /// (e.g. another device behind the same NAT) keep getting 429 until the block expires.
    /// </summary>
    public void RecordSuccess(IPAddress? ip)
    {
        lock (_gate)
        {
            var key = Key(ip);
            if (!_entries.TryGetValue(key, out var entry))
            {
                return;
            }

            if (entry.BlockedUntil is { } until && until > _time.GetUtcNow())
            {
                entry.Failures.Clear();
            }
            else
            {
                _entries.Remove(key);
            }
        }
    }

    /// <summary>Forgets all failure history and blocks (used after the connection key is regenerated).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    /// <summary>Removes entries whose failure window and block have both expired.</summary>
    private void PruneLocked(DateTimeOffset now)
    {
        _lastPrune = now;
        List<string>? expired = null;
        foreach (var (key, entry) in _entries)
        {
            var blockExpired = entry.BlockedUntil is not { } until || until <= now;
            var windowExpired = now - entry.LastFailure > Window;
            if (blockExpired && windowExpired)
            {
                (expired ??= []).Add(key);
            }
        }

        if (expired is not null)
        {
            foreach (var key in expired)
            {
                _entries.Remove(key);
            }
        }
    }

    private void RemoveOldestLocked()
    {
        string? oldestKey = null;
        var oldest = DateTimeOffset.MaxValue;
        foreach (var (key, entry) in _entries)
        {
            if (entry.LastFailure < oldest)
            {
                oldest = entry.LastFailure;
                oldestKey = key;
            }
        }

        if (oldestKey is not null)
        {
            _entries.Remove(oldestKey);
        }
    }

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

        public DateTimeOffset LastFailure { get; set; }

        public DateTimeOffset? BlockedUntil { get; set; }
    }
}
