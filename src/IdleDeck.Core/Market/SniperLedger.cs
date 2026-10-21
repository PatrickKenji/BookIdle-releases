namespace IdleDeck.Core.Market;

/// <summary>
/// Quais ofertas já foram tentadas: uma oferta comprada ou em andamento nunca é tentada de novo; uma que falhou
/// só volta depois de <see cref="RetryAfter"/> (se ainda estiver aberta, o motor a oferece de novo).
/// </summary>
public sealed class SniperLedger
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(20);

    /// <summary>Compra sem resposta do jogo: pode ter dado certo, então só volta depois de bem mais tempo.</summary>
    public static readonly TimeSpan UnknownRetryAfter = TimeSpan.FromMinutes(5);

    private enum State { InFlight, Bought, Failed, Unknown }

    private readonly Dictionary<long, (State State, DateTimeOffset At)> _offers = [];

    public bool CanTry(long offerId, DateTimeOffset now)
    {
        if (!_offers.TryGetValue(offerId, out var entry)) return true;
        return entry.State switch
        {
            State.Failed => now - entry.At >= RetryAfter,
            State.Unknown => now - entry.At >= UnknownRetryAfter,
            _ => false,
        };
    }

    public void Started(long offerId, DateTimeOffset now) => _offers[offerId] = (State.InFlight, now);

    public void Finished(long offerId, bool bought, DateTimeOffset now) =>
        _offers[offerId] = (bought ? State.Bought : State.Failed, now);

    /// <summary>O pedido saiu, mas o jogo não disse se levou.</summary>
    public void Unknown(long offerId, DateTimeOffset now) => _offers[offerId] = (State.Unknown, now);

    public void Clear() => _offers.Clear();
}
