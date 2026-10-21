using IdleDeck.Core.Twitch;

namespace IdleDeck.Core.Tests;

public class TwitchWebCookiesTests
{
    private static KeyValuePair<string, string> C(string name, string value) => new(name, value);

    [Fact]
    public void Reads_login_and_token_from_the_site_cookies()
    {
        var found = TwitchWebCookies.TryRead([C("auth-token", "abc123"), C("login", "Patrick_TV"), C("name", "Patrick_TV")], out var login, out var token);

        Assert.True(found);
        Assert.Equal("patrick_tv", login); // a Twitch usa minúsculas no chat
        Assert.Equal("abc123", token);
    }

    [Fact]
    public void Falls_back_to_the_twilight_user_cookie()
    {
        var json = Uri.EscapeDataString("""{"authToken":"tok999","displayName":"Patrick","id":"1","login":"patrick_tv"}""");

        var found = TwitchWebCookies.TryRead([C("twilight-user", json)], out var login, out var token);

        Assert.True(found);
        Assert.Equal("patrick_tv", login);
        Assert.Equal("tok999", token);
    }

    [Fact]
    public void Explicit_cookies_win_over_the_twilight_user_cookie()
    {
        var json = Uri.EscapeDataString("""{"authToken":"velho","login":"outro"}""");

        TwitchWebCookies.TryRead([C("twilight-user", json), C("auth-token", "novo"), C("login", "atual")], out var login, out var token);

        Assert.Equal("atual", login);
        Assert.Equal("novo", token);
    }

    [Fact]
    public void Not_logged_in_returns_false()
    {
        Assert.False(TwitchWebCookies.TryRead([C("unique_id", "x"), C("server_session_id", "y")], out var login, out var token));
        Assert.Equal("", login);
        Assert.Equal("", token);
    }

    [Fact]
    public void Token_without_login_or_login_without_token_returns_false()
    {
        Assert.False(TwitchWebCookies.TryRead([C("auth-token", "abc")], out _, out _));
        Assert.False(TwitchWebCookies.TryRead([C("login", "patrick_tv")], out _, out _));
        Assert.False(TwitchWebCookies.TryRead([C("auth-token", ""), C("login", "patrick_tv")], out _, out _));
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("%7B%22authToken%22%3Anull%7D")]
    [InlineData("")]
    public void A_broken_twilight_user_cookie_is_ignored(string value)
    {
        Assert.False(TwitchWebCookies.TryRead([C("twilight-user", value)], out _, out _));
    }

    [Fact]
    public void Login_with_unexpected_characters_is_rejected()
    {
        Assert.False(TwitchWebCookies.TryRead([C("auth-token", "abc"), C("login", "nome com espaço\r\nJOIN #x")], out _, out _));
    }
}
