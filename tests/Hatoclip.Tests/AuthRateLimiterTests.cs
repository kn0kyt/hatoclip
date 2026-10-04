using System.Net;

namespace Hatoclip.Tests;

public sealed class AuthRateLimiterTests
{
    private static readonly IPAddress PhoneA = IPAddress.Parse("192.168.1.20");
    private static readonly IPAddress PhoneB = IPAddress.Parse("192.168.1.21");

    private readonly FakeTimeProvider _time = new();
    private readonly AuthRateLimiter _limiter;

    public AuthRateLimiterTests()
    {
        _limiter = new AuthRateLimiter(_time);
    }

    private void Fail(IPAddress? ip, int times)
    {
        for (var i = 0; i < times; i++)
        {
            _limiter.RecordFailure(ip);
        }
    }

    [Fact]
    public void Blocks_exactly_at_the_threshold()
    {
        for (var i = 1; i < AuthRateLimiter.MaxFailures; i++)
        {
            Assert.False(_limiter.RecordFailure(PhoneA));
        }

        Assert.False(_limiter.IsBlocked(PhoneA));
        Assert.True(_limiter.RecordFailure(PhoneA)); // the 10th failure blocks
        Assert.True(_limiter.IsBlocked(PhoneA));
        Assert.False(_limiter.RecordFailure(PhoneA)); // already blocked: not "just blocked" again
    }

    [Fact]
    public void Counts_failures_per_ip()
    {
        Fail(PhoneA, AuthRateLimiter.MaxFailures);

        Assert.True(_limiter.IsBlocked(PhoneA));
        Assert.False(_limiter.IsBlocked(PhoneB));
    }

    [Fact]
    public void Ipv4_mapped_ipv6_counts_as_the_same_address()
    {
        Fail(PhoneA.MapToIPv6(), AuthRateLimiter.MaxFailures);

        Assert.True(_limiter.IsBlocked(PhoneA));
    }

    [Fact]
    public void Block_expires_after_block_duration()
    {
        Fail(PhoneA, AuthRateLimiter.MaxFailures);

        _time.Advance(AuthRateLimiter.BlockDuration - TimeSpan.FromSeconds(1));
        Assert.True(_limiter.IsBlocked(PhoneA));

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(_limiter.IsBlocked(PhoneA));

        // After expiry the count starts from zero again.
        Assert.False(_limiter.RecordFailure(PhoneA));
        Assert.False(_limiter.IsBlocked(PhoneA));
    }

    [Fact]
    public void Failures_older_than_the_window_are_forgotten()
    {
        Fail(PhoneA, AuthRateLimiter.MaxFailures - 1);
        _time.Advance(AuthRateLimiter.Window + TimeSpan.FromSeconds(1));

        Assert.False(_limiter.RecordFailure(PhoneA));
        Fail(PhoneA, AuthRateLimiter.MaxFailures - 2);
        Assert.False(_limiter.IsBlocked(PhoneA)); // 9 failures inside the window
        Assert.True(_limiter.RecordFailure(PhoneA));
    }

    [Fact]
    public void Success_clears_the_failure_count()
    {
        Fail(PhoneA, AuthRateLimiter.MaxFailures - 1);
        _limiter.RecordSuccess(PhoneA);

        Assert.False(_limiter.RecordFailure(PhoneA));
        Assert.False(_limiter.IsBlocked(PhoneA));
        Assert.Equal(1, _limiter.EntryCount);
    }

    [Fact]
    public void Success_does_not_lift_an_active_block()
    {
        Fail(PhoneA, AuthRateLimiter.MaxFailures);
        _limiter.RecordSuccess(PhoneA);

        Assert.True(_limiter.IsBlocked(PhoneA));
    }

    [Fact]
    public void Success_for_an_unknown_ip_is_a_no_op()
    {
        _limiter.RecordSuccess(PhoneA);

        Assert.Equal(0, _limiter.EntryCount);
    }

    [Fact]
    public void Reset_forgets_blocks_and_failures()
    {
        Fail(PhoneA, AuthRateLimiter.MaxFailures);
        Fail(PhoneB, 3);

        _limiter.Reset();

        Assert.False(_limiter.IsBlocked(PhoneA));
        Assert.Equal(0, _limiter.EntryCount);
    }

    [Fact]
    public void Null_ip_is_tracked_as_one_unknown_address()
    {
        Fail(null, AuthRateLimiter.MaxFailures);

        Assert.True(_limiter.IsBlocked(null));
        Assert.False(_limiter.IsBlocked(PhoneA));
    }

    [Fact]
    public void Pruning_removes_entries_whose_window_and_block_expired()
    {
        _limiter.RecordFailure(PhoneA);
        _time.Advance(AuthRateLimiter.Window + TimeSpan.FromMinutes(1));

        _limiter.RecordFailure(PhoneB); // triggers the periodic prune

        Assert.Equal(1, _limiter.EntryCount);
    }

    [Fact]
    public void Pruning_keeps_blocked_entries()
    {
        Fail(PhoneA, AuthRateLimiter.MaxFailures);
        _time.Advance(TimeSpan.FromMinutes(2));

        _limiter.RecordFailure(PhoneB);

        Assert.Equal(2, _limiter.EntryCount);
        Assert.True(_limiter.IsBlocked(PhoneA));
    }

    [Fact]
    public void Entry_table_is_capped()
    {
        var first = new IPAddress(new byte[] { 10, 0, 0, 0 });
        for (var i = 0; i <= AuthRateLimiter.MaxEntries; i++)
        {
            _limiter.RecordFailure(new IPAddress(new byte[] { 10, (byte)(i >> 16), (byte)(i >> 8), (byte)i }));
            _time.Advance(TimeSpan.FromMilliseconds(1));
        }

        Assert.Equal(AuthRateLimiter.MaxEntries, _limiter.EntryCount);

        // The oldest entry was evicted: one more failure for it starts a new entry, evicting the next oldest.
        _limiter.RecordFailure(first);
        Assert.Equal(AuthRateLimiter.MaxEntries, _limiter.EntryCount);
    }
}
