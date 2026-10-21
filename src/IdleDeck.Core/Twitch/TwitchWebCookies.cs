using System.Text.Json;
using System.Text.RegularExpressions;

namespace IdleDeck.Core.Twitch;

/// <summary>
/// Quem entrou na Twitch no navegador de uma conta: o login e a chave que o próprio site da Twitch guarda nos cookies
/// (<c>login</c> e <c>auth-token</c>; <c>twilight-user</c> como reserva). É a mesma chave que o chat do site usa, por isso a
/// conexão leve de chat do app consegue ficar presente sem registrar app nenhum na Twitch.
/// </summary>
public static partial class TwitchWebCookies
{
    [GeneratedRegex("^[a-z0-9_]{1,25}$")]
    private static partial Regex ValidLogin();

    /// <summary>
    /// Procura o par login + chave nos cookies de twitch.tv. Falta um dos dois, ou o login tem caracteres que a Twitch
    /// não aceita (nunca vai para uma linha do chat): devolve false.
    /// </summary>
    public static bool TryRead(IEnumerable<KeyValuePair<string, string>> cookies, out string login, out string token)
    {
        login = "";
        token = "";
        string? explicitToken = null, explicitLogin = null, twilight = null;
        foreach (var (name, value) in cookies)
        {
            switch (name)
            {
                case "auth-token": explicitToken = value; break;
                case "login": explicitLogin = value; break;
                case "twilight-user": twilight = value; break;
            }
        }

        var (twilightLogin, twilightToken) = ReadTwilightUser(twilight);
        var candidateLogin = (string.IsNullOrWhiteSpace(explicitLogin) ? twilightLogin : explicitLogin)?.Trim().ToLowerInvariant();
        var candidateToken = (string.IsNullOrWhiteSpace(explicitToken) ? twilightToken : explicitToken)?.Trim();

        if (string.IsNullOrEmpty(candidateToken) || string.IsNullOrEmpty(candidateLogin) || !ValidLogin().IsMatch(candidateLogin)) return false;
        if (candidateToken.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) return false;
        login = candidateLogin;
        token = candidateToken;
        return true;
    }

    private static (string? Login, string? Token) ReadTwilightUser(string? cookie)
    {
        if (string.IsNullOrWhiteSpace(cookie)) return (null, null);
        try
        {
            using var json = JsonDocument.Parse(Uri.UnescapeDataString(cookie));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);
            return (Read(root, "login"), Read(root, "authToken"));
        }
        catch (Exception e) when (e is JsonException or UriFormatException)
        {
            return (null, null);
        }

        static string? Read(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
