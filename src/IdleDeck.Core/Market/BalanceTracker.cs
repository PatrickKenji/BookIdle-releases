namespace IdleDeck.Core.Market;

/// <summary>
/// O saldo da conta (coins e gemas) e o relógio do servidor, como o jogo contou. Vive por conta, fora da sessão do
/// sniper: o saldo chega uma vez ao conectar e quem liga o sniper depois já o encontra aqui.
/// </summary>
public sealed class BalanceTracker
{
    /// <summary>Desconhecido até chegar uma mensagem com coins E gemas; depois as atualizações parciais só mexem no que trazem.</summary>
    public Balance? Balance { get; private set; }

    /// <summary>Relógio do servidor menos o local, em ms.</summary>
    public long ServerOffsetMs { get; private set; }

    public void Apply(BalanceMessage message, long localNowMs)
    {
        var before = Balance;
        if (message.Gold is { } gold && message.Orb is { } orb) Balance = new Balance(gold, orb);
        else if (before is not null) Balance = new Balance(message.Gold ?? before.Gold, message.Orb ?? before.Orb);
        if (message.ServerNowMs is { } server) ServerOffsetMs = server - localNowMs;
    }

    /// <summary>Desconta uma compra até o próximo aviso de saldo do jogo. Sem saldo conhecido, não faz nada.</summary>
    public void Deduct(Currency currency, long amount)
    {
        if (Balance is not { } b) return;
        Balance = currency == Currency.Gold ? b with { Gold = b.Gold - amount } : b with { Orb = b.Orb - amount };
    }
}
