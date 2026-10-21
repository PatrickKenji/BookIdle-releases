using IdleDeck.Core.Twitch;

namespace IdleDeck.Core.Tests;

public class TwitchTests
{
    private static readonly DateTime T0 = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    // ---------- Linhas do chat (IRC) ----------

    [Fact]
    public void Parse_ping()
    {
        var m = IrcMessage.Parse("PING :tmi.twitch.tv");

        Assert.NotNull(m);
        Assert.Null(m.Prefix);
        Assert.Equal("PING", m.Command);
        Assert.Empty(m.Params);
        Assert.Equal("tmi.twitch.tv", m.Trailing);
    }

    [Fact]
    public void Parse_ignores_tags_and_reads_prefix_params_and_trailing()
    {
        var m = IrcMessage.Parse("@badges=;color=#FF0000 :nick!nick@nick.tmi.twitch.tv PRIVMSG #canal :oi, tudo bem?");

        Assert.NotNull(m);
        Assert.Equal("nick!nick@nick.tmi.twitch.tv", m.Prefix);
        Assert.Equal("PRIVMSG", m.Command);
        Assert.Equal(["#canal"], m.Params);
        Assert.Equal("oi, tudo bem?", m.Trailing);
    }

    [Fact]
    public void Parse_join_confirmation()
    {
        var m = IrcMessage.Parse(":nick!nick@nick.tmi.twitch.tv JOIN #canal\r\n");

        Assert.NotNull(m);
        Assert.Equal("JOIN", m.Command);
        Assert.Equal(["#canal"], m.Params);
        Assert.Null(m.Trailing);
    }

    [Fact]
    public void Parse_notice_login_failed()
    {
        var m = IrcMessage.Parse(":tmi.twitch.tv NOTICE * :Login authentication failed");

        Assert.NotNull(m);
        Assert.Equal("NOTICE", m.Command);
        Assert.Equal(["*"], m.Params);
        Assert.Equal("Login authentication failed", m.Trailing);
    }

    [Fact]
    public void Parse_numeric_welcome()
    {
        var m = IrcMessage.Parse(":tmi.twitch.tv 001 patrick :Welcome, GLHF!");

        Assert.NotNull(m);
        Assert.Equal("001", m.Command);
        Assert.Equal(["patrick"], m.Params);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@so=tags")]
    [InlineData(":so-prefixo")]
    public void Parse_garbage_returns_null(string? line)
    {
        Assert.Null(IrcMessage.Parse(line));
    }

    // ---------- Limite de JOIN ----------

    [Fact]
    public void JoinScheduler_allows_20_then_waits_for_the_window()
    {
        var scheduler = new JoinScheduler();
        for (var i = 0; i < 20; i++) scheduler.Record(T0);

        Assert.Equal(TimeSpan.FromSeconds(10), scheduler.NextDelay(T0));
        Assert.Equal(TimeSpan.FromSeconds(6), scheduler.NextDelay(T0.AddSeconds(4)));
        Assert.Equal(TimeSpan.Zero, scheduler.NextDelay(T0.AddSeconds(10)));
    }

    [Fact]
    public void JoinScheduler_with_19_joins_has_no_delay()
    {
        var scheduler = new JoinScheduler();
        for (var i = 0; i < 19; i++) scheduler.Record(T0);

        Assert.Equal(TimeSpan.Zero, scheduler.NextDelay(T0));
    }

    // ---------- Nomes de canal ----------

    [Fact]
    public void Channels_are_normalized_lowercase_without_hash_or_url_and_without_duplicates()
    {
        var result = TwitchChannels.Normalize(
            ["  #Canal ", "canal", "https://twitch.tv/Outro", "https://www.twitch.tv/Terceiro?sr=a", "inválido!!", "", "@Quarto"]);

        Assert.Equal(["canal", "outro", "terceiro", "quarto"], result);
    }

    [Fact]
    public void Channels_longer_than_25_characters_are_dropped()
    {
        Assert.Empty(TwitchChannels.Normalize([new string('a', 26)]));
        Assert.Single(TwitchChannels.Normalize([new string('a', 25)]));
    }
}
