using IdleDeck.Core.Market;

namespace IdleDeck.Core.Tests;

public class BalanceTrackerTests
{
    [Fact]
    public void Unknown_until_a_message_brings_both_coins_and_gems()
    {
        var tracker = new BalanceTracker();
        Assert.Null(tracker.Balance);

        tracker.Apply(new BalanceMessage(500, null, null), 0);
        Assert.Null(tracker.Balance);

        tracker.Apply(new BalanceMessage(1000, 7, null), 0);
        Assert.Equal(new Balance(1000, 7), tracker.Balance);
    }

    [Fact]
    public void Partial_updates_keep_what_was_known()
    {
        var tracker = new BalanceTracker();
        tracker.Apply(new BalanceMessage(1000, 7, null), 0);
        tracker.Apply(new BalanceMessage(900, null, null), 0);
        Assert.Equal(new Balance(900, 7), tracker.Balance);
        tracker.Apply(new BalanceMessage(null, 5, null), 0);
        Assert.Equal(new Balance(900, 5), tracker.Balance);
    }

    [Fact]
    public void Server_clock_offset_is_server_minus_local()
    {
        var tracker = new BalanceTracker();
        tracker.Apply(new BalanceMessage(1, 1, 10_000), 4_000);
        Assert.Equal(6_000, tracker.ServerOffsetMs);
        tracker.Apply(new BalanceMessage(2, 2, null), 99_999);
        Assert.Equal(6_000, tracker.ServerOffsetMs);
    }

    [Fact]
    public void Deduct_lowers_one_currency_and_does_nothing_when_unknown()
    {
        var tracker = new BalanceTracker();
        tracker.Deduct(Currency.Gold, 10);
        Assert.Null(tracker.Balance);

        tracker.Apply(new BalanceMessage(100, 50, null), 0);
        tracker.Deduct(Currency.Gold, 30);
        tracker.Deduct(Currency.Orb, 5);
        Assert.Equal(new Balance(70, 45), tracker.Balance);
    }
}
