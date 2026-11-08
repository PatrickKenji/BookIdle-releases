namespace IdleDeck.Core.Kick;

/// <summary>
/// A sessão da Kick que o próprio site guarda no navegador da conta: o cookie <c>session_token</c> (URL-encoded), que o
/// site manda como <c>Authorization: Bearer</c> nas chamadas da API.
/// </summary>
public static class KickWebCookies
{
    public static bool TryReadToken(IEnumerable<KeyValuePair<string, string>> cookies, out string token)
    {
        token = "";
        foreach (var (name, value) in cookies)
        {
            if (name != "session_token") continue;
            string decoded;
            try { decoded = Uri.UnescapeDataString(value).Trim(); }
            catch (UriFormatException) { return false; }
            if (decoded.Length == 0 || decoded.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) return false;
            token = decoded;
            return true;
        }
        return false;
    }
}
