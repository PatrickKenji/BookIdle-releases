using IdleDeck.Core.Modules;
using IdleDeck.Core.Settings;

namespace IdleDeck.Core.Vote;

/// <summary>
/// O site de votação de cada perfil de jogo (<see cref="GameProfile.VoteUrl"/>): o padrão do PokeIdle, a migração
/// de quem ainda estava no TopIdle e o nome curto que aparece no botão ("Votar no IdleHub").
/// </summary>
public static class VoteSites
{
    /// <summary>Desde 2026-11 o PokeIdle conta os votos no IdleHub (antes era o TopIdle).</summary>
    public const string PokeIdleUrl = "https://idlehub.io/jogo/pokeidle-io";

    /// <summary>O site de voto que um perfil novo com este endereço já ganha preenchido ("" = nenhum).</summary>
    public static string DefaultFor(string? gameUrl) => IsPokeIdle(gameUrl) ? PokeIdleUrl : "";

    /// <summary>
    /// O site de voto que o perfil deve ter depois de carregar do disco. Perfil sem o campo (versões até a 1.0.15)
    /// ganha o padrão do jogo; perfil do PokeIdle que ainda aponta para o TopIdle passa para o IdleHub;
    /// endereço que não é http(s) vira "nenhum". Vazio é escolha do usuário e fica vazio.
    /// </summary>
    public static string Normalize(string? voteUrl, string? gameUrl)
    {
        if (voteUrl is null) return DefaultFor(gameUrl);
        voteUrl = voteUrl.Trim();
        if (voteUrl.Length == 0) return "";
        if (!AppSettings.IsHttpUrl(voteUrl)) return "";
        if (IsPokeIdle(gameUrl) && HostIs(voteUrl, "topidle.com")) return PokeIdleUrl;
        return voteUrl;
    }

    /// <summary>Nome curto do site para os botões: "IdleHub", "TopIdle" ou o domínio (sem "www.").</summary>
    public static string DisplayName(string? voteUrl)
    {
        if (!Uri.TryCreate(voteUrl, UriKind.Absolute, out var uri)) return "";
        if (ModuleRouter.HostMatches(uri.Host, "idlehub.io")) return "IdleHub";
        if (ModuleRouter.HostMatches(uri.Host, "topidle.com")) return "TopIdle";
        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    private static bool IsPokeIdle(string? gameUrl) => HostIs(gameUrl, "pokeidle.io");

    private static bool HostIs(string? url, string host) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && ModuleRouter.HostMatches(uri.Host, host);
}
