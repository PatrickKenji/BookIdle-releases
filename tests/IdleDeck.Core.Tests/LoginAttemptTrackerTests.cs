using IdleDeck.Core.Vault;

namespace IdleDeck.Core.Tests;

public class LoginAttemptTrackerTests
{
    private static readonly DateTime T0 = new(2026, 10, 20, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Three_failures_lock_out()
    {
        var t = new LoginAttemptTracker();
        var now = T0;
        for (var i = 0; i < 3; i++)
        {
            Assert.True(t.CanAttempt(0));
            t.RecordAttempt(0, now);
            t.RecordLoginFormSeen(0, now.AddSeconds(10));
            now = now.AddMinutes(1);
        }

        Assert.True(t.IsLockedOut(0));
        Assert.False(t.CanAttempt(0));
        Assert.True(t.CanAttempt(1));
    }

    [Fact]
    public void Form_seen_after_60s_is_not_failure()
    {
        var t = new LoginAttemptTracker();
        for (var i = 0; i < 5; i++)
        {
            var now = T0.AddHours(i);
            t.RecordAttempt(0, now);
            t.RecordLoginFormSeen(0, now.AddSeconds(61));
        }

        Assert.False(t.IsLockedOut(0));
    }

    [Fact]
    public void LoggedIn_resets_counter()
    {
        var t = new LoginAttemptTracker();
        for (var i = 0; i < 2; i++)
        {
            t.RecordAttempt(0, T0);
            t.RecordLoginFormSeen(0, T0.AddSeconds(5));
        }
        t.RecordLoggedIn(0);

        t.RecordAttempt(0, T0);
        t.RecordLoginFormSeen(0, T0.AddSeconds(5));

        Assert.False(t.IsLockedOut(0));
    }

    [Fact]
    public void Attempts_closer_than_15s_are_refused()
    {
        var t = new LoginAttemptTracker();
        t.RecordAttempt(0, T0);

        Assert.False(t.CanAttempt(0, T0.AddSeconds(10)));
        Assert.True(t.CanAttempt(0, T0.AddSeconds(15)));
    }
}
