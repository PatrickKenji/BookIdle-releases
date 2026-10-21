namespace IdleDeck.Core.Twitch;

/// <summary>Uma linha do chat da Twitch (IRC): <c>[@tags] [:prefixo] COMANDO [params] [:texto final]</c>.</summary>
public sealed record IrcMessage(string? Prefix, string Command, IReadOnlyList<string> Params, string? Trailing)
{
    /// <summary>As tags (<c>@...</c>) são ignoradas. Linha vazia ou sem comando: null.</summary>
    public static IrcMessage? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var rest = line.Trim();

        if (rest[0] == '@')
        {
            var space = rest.IndexOf(' ');
            if (space < 0) return null;
            rest = rest[(space + 1)..].TrimStart();
        }

        string? prefix = null;
        if (rest.Length > 0 && rest[0] == ':')
        {
            var space = rest.IndexOf(' ');
            if (space < 0) return null;
            prefix = rest[1..space];
            rest = rest[(space + 1)..].TrimStart();
        }

        string? trailing = null;
        var marker = rest.IndexOf(" :", StringComparison.Ordinal);
        if (marker >= 0)
        {
            trailing = rest[(marker + 2)..];
            rest = rest[..marker];
        }

        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? null : new IrcMessage(prefix, parts[0], parts[1..], trailing);
    }
}
