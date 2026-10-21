namespace IdleDeck.Core.Net;

/// <summary>Decide se uma requisição deve ser bloqueada pelo domínio (inclui subdomínios).</summary>
public class DomainBlocker(IEnumerable<string> domains)
{
    private static readonly string[] AlwaysAllowed = ["pokeidle.io"];

    private readonly HashSet<string> _blocked = domains
        .Select(d => d.Trim().TrimStart('.').ToLowerInvariant())
        .Where(d => d.Length > 0 && !AlwaysAllowed.Contains(d))
        .ToHashSet();

    public IReadOnlyCollection<string> Domains => _blocked;

    public bool IsBlocked(Uri uri)
    {
        if (_blocked.Count == 0) return false;

        var host = uri.Host.ToLowerInvariant();
        while (true)
        {
            if (AlwaysAllowed.Contains(host)) return false;
            if (_blocked.Contains(host)) return true;
            var dot = host.IndexOf('.');
            if (dot < 0) return false;
            host = host[(dot + 1)..];
        }
    }
}
