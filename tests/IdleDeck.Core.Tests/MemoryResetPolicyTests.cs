using IdleDeck.Core.Health;

namespace IdleDeck.Core.Tests;

public class MemoryResetPolicyTests
{
    private const long Mb = 1024 * 1024;
    private static readonly DateTime T0 = new(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
    private readonly MemoryResetPolicy _policy = new(TimeSpan.FromMinutes(10));

    [Fact]
    public void Below_limit_no_reset() =>
        Assert.False(_policy.ShouldAutoReset(0, 599 * Mb, 600, enabled: true, T0));

    [Fact]
    public void Above_limit_resets() =>
        Assert.True(_policy.ShouldAutoReset(0, 601 * Mb, 600, enabled: true, T0));

    [Fact]
    public void Disabled_never_resets() =>
        Assert.False(_policy.ShouldAutoReset(0, 5000 * Mb, 600, enabled: false, T0));

    [Fact]
    public void Cooldown_blocks_second_reset()
    {
        _policy.RecordReset(0, T0);

        Assert.False(_policy.ShouldAutoReset(0, 900 * Mb, 600, true, T0.AddMinutes(5)));
        Assert.True(_policy.ShouldAutoReset(0, 900 * Mb, 600, true, T0.AddMinutes(10)));
    }

    [Fact]
    public void Cooldown_is_per_account()
    {
        _policy.RecordReset(0, T0);

        Assert.True(_policy.ShouldAutoReset(1, 900 * Mb, 600, true, T0.AddMinutes(1)));
    }
}

public class RetryBackoffTests
{
    [Fact]
    public void Backoff_sequence()
    {
        var b = new RetryBackoff();

        var seconds = Enumerable.Range(0, 6).Select(_ => b.NextDelay().TotalSeconds).ToArray();
        Assert.Equal([5.0, 10.0, 20.0, 40.0, 60.0, 60.0], seconds);

        b.Reset();
        Assert.Equal(TimeSpan.FromSeconds(5), b.NextDelay());
    }
}
