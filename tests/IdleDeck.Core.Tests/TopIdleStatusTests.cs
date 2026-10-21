using IdleDeck.Core.Vote;

namespace IdleDeck.Core.Tests;

public class TopIdleStatusTests
{
    // Resposta real do jogo (POST /topidle/me), só os campos que importam.
    private const string Json = """{"votos":4,"contados":4,"ultimoVotoEm":1792526539000,"liberaEm":1792611139000,"esperaMs":21120839,"recargaMs":86400000,"ativo":true}""";

    private static readonly DateTime Voted = DateTimeOffset.FromUnixTimeMilliseconds(1792526539000).UtcDateTime;
    private static readonly DateTime Release = DateTimeOffset.FromUnixTimeMilliseconds(1792611139000).UtcDateTime;

    [Fact]
    public void Parse_reads_the_last_vote_and_the_release_time()
    {
        var status = TopIdleStatus.Parse(Json)!;
        Assert.Equal(Voted, status.LastVoteUtc);
        Assert.Equal(Release, status.ReleaseUtc);
    }

    [Fact]
    public void The_countdown_matches_what_the_game_says_it_is_waiting()
    {
        var status = TopIdleStatus.Parse(Json)!;
        var now = Release.AddHours(-5);
        var effective = status.EffectiveLastVoteUtc;
        Assert.Equal(TimeSpan.FromHours(5), VoteCooldown.Remaining(effective, now));
        Assert.Equal(TimeSpan.Zero, VoteCooldown.Remaining(effective, Release));
    }

    [Fact]
    public void Without_a_release_time_it_falls_back_to_the_last_vote_plus_24h()
    {
        var status = TopIdleStatus.Parse("""{"ultimoVotoEm":1792526539000}""")!;
        Assert.Null(status.ReleaseUtc);
        Assert.Equal(Voted, status.EffectiveLastVoteUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("lixo")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"ultimoVotoEm":0,"liberaEm":0}""")]
    [InlineData("""{"ultimoVotoEm":"x","liberaEm":null}""")]
    public void Parse_returns_null_when_there_is_nothing_usable(string json)
    {
        Assert.Null(TopIdleStatus.Parse(json));
    }

    [Fact]
    public void A_voter_that_never_voted_has_no_effective_date()
    {
        // nunca votou: o jogo manda 0 ou omite; não é um status útil
        Assert.Null(TopIdleStatus.Parse("""{"votos":0,"ultimoVotoEm":0,"liberaEm":0}"""));
    }

    [Fact]
    public void Reconcile_takes_the_game_value_when_nothing_is_stored()
    {
        var status = TopIdleStatus.Parse(Json)!;
        Assert.Equal(status.EffectiveLastVoteUtc, status.Reconcile(null));
    }

    [Fact]
    public void Reconcile_prefers_the_game_when_it_has_seen_the_stored_vote()
    {
        var status = TopIdleStatus.Parse(Json)!;
        var localAFewMinutesLater = Voted.AddMinutes(3); // o app anotou o voto um pouco depois do relógio do servidor
        Assert.Equal(status.EffectiveLastVoteUtc, status.Reconcile(localAFewMinutesLater));
    }

    [Fact]
    public void Reconcile_keeps_a_newer_local_vote_the_game_has_not_reflected_yet()
    {
        var status = TopIdleStatus.Parse(Json)!;
        var votedAgain = Voted.AddHours(25); // votou de novo; o status que chegou é velho
        Assert.Equal(votedAgain, status.Reconcile(votedAgain));
    }

    [Fact]
    public void Reconcile_takes_a_newer_game_vote_over_an_older_stored_one()
    {
        var status = TopIdleStatus.Parse(Json)!;
        var old = Voted.AddDays(-2);
        Assert.Equal(status.EffectiveLastVoteUtc, status.Reconcile(old));
    }
}
