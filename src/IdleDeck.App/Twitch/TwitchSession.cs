using IdleDeck.Core.Twitch;
using Microsoft.Web.WebView2.Core;

namespace IdleDeck.App.Twitch;

/// <summary>Quem está logado na Twitch dentro do navegador de uma conta (lido dos cookies do perfil dela).</summary>
public sealed record TwitchSession(string Login, string Token)
{
    /// <summary>Só os NOMES dos cookies de twitch.tv (para diagnóstico; os valores nunca saem daqui).</summary>
    public static async Task<List<string>> CookieNamesAsync(CoreWebView2 core) =>
        (await core.CookieManager.GetCookiesAsync("https://www.twitch.tv")).Select(c => c.Name).Order().ToList();

    /// <summary>
    /// Lê os cookies de twitch.tv do perfil de <paramref name="core"/>. Null se ninguém entrou na Twitch ali.
    /// Precisa rodar na thread da tela.
    /// </summary>
    public static async Task<TwitchSession?> ReadAsync(CoreWebView2 core)
    {
        var cookies = await core.CookieManager.GetCookiesAsync("https://www.twitch.tv");
        return TwitchWebCookies.TryRead(cookies.Select(c => new KeyValuePair<string, string>(c.Name, c.Value)), out var login, out var token)
            ? new TwitchSession(login, token)
            : null;
    }
}
