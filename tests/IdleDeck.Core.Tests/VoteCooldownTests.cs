using IdleDeck.Core.Vote;

namespace IdleDeck.Core.Tests;

public class VoteCooldownTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Remaining_is_zero_when_never_voted()
    {
        Assert.Equal(TimeSpan.Zero, VoteCooldown.Remaining(null, Now));
    }

    [Fact]
    public void Remaining_counts_down_from_24h()
    {
        Assert.Equal(TimeSpan.FromHours(5), VoteCooldown.Remaining(Now.AddHours(-19), Now));
    }

    [Fact]
    public void Remaining_is_zero_after_24h()
    {
        Assert.Equal(TimeSpan.Zero, VoteCooldown.Remaining(Now.AddHours(-24), Now));
        Assert.Equal(TimeSpan.Zero, VoteCooldown.Remaining(Now.AddDays(-3), Now));
    }

    [Fact]
    public void Remaining_clamps_future_vote_to_24h()
    {
        Assert.Equal(TimeSpan.FromHours(24), VoteCooldown.Remaining(Now.AddHours(3), Now));
    }

    [Fact]
    public void Label_shows_hours_rounded_up()
    {
        var voted = Now.AddHours(-19).AddMinutes(-50); // faltam 4 h 10 min
        Assert.Equal("Votar no TopIdle (em 5 h)", VoteCooldown.Label(voted, Now));
    }

    [Fact]
    public void Label_shows_minutes_under_one_hour()
    {
        var voted = Now.AddHours(-23).AddMinutes(-20); // faltam 40 min
        Assert.Equal("Votar no TopIdle (em 40 min)", VoteCooldown.Label(voted, Now));
    }

    [Fact]
    public void Label_is_plain_when_available()
    {
        Assert.Equal("Votar no TopIdle", VoteCooldown.Label(null, Now));
        Assert.Equal("Votar no TopIdle", VoteCooldown.Label(Now.AddHours(-30), Now));
    }

    [Fact]
    public void Vote_url_is_the_pokeidle_page_on_topidle()
    {
        Assert.Equal("https://topidle.com/jogo/pokeidle-io-5f9969", VoteCooldown.VoteUrl);
    }

    [Fact]
    public void ShortLabel_is_plain_when_available_and_counts_down_otherwise()
    {
        Assert.Equal("Votar no TopIdle", VoteCooldown.ShortLabel(null, Now));
        Assert.Equal("Votar no TopIdle", VoteCooldown.ShortLabel(Now.AddHours(-30), Now));
        Assert.Equal("Votar em 5 h", VoteCooldown.ShortLabel(Now.AddHours(-19), Now));
        Assert.Equal("Votar em 40 min", VoteCooldown.ShortLabel(Now.AddHours(-24).AddMinutes(40), Now));
    }
}
