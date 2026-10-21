namespace IdleDeck.Core.Market;

/// <summary>
/// Quem está com cada oferta: duas contas do mesmo jogo nunca vão atrás da mesma oferta ao mesmo tempo (decisão do
/// usuário). A primeira conta que pede fica com ela por <see cref="Lifetime"/>.
/// </summary>
public sealed class OfferClaims
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly Dictionary<long, (string Owner, DateTimeOffset At)> _claims = [];
    private readonly object _gate = new();

    public bool TryClaim(long offerId, string owner, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_claims.TryGetValue(offerId, out var claim) && claim.Owner != owner && now - claim.At < Lifetime) return false;
            _claims[offerId] = (owner, claim.Owner == owner ? claim.At : now);
            if (_claims.Count > 500)
                foreach (var old in _claims.Where(c => now - c.Value.At >= Lifetime).Select(c => c.Key).ToList()) _claims.Remove(old);
            return true;
        }
    }
}

/// <summary>O que várias sessões compartilham: o registro de tentativas da conta, as reservas de oferta entre contas e o dono.</summary>
public sealed record SniperShared(SniperLedger Ledger, OfferClaims Claims, string Owner, BalanceTracker? Tracker = null);
