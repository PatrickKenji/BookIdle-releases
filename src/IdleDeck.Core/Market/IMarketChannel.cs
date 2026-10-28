namespace IdleDeck.Core.Market;

/// <summary>A conexão da conta com o mercado do jogo: manda pedidos e recebe o que o jogo conta.</summary>
public interface IMarketChannel
{
    /// <summary>O jogo está conectado e dá para mandar pedidos.</summary>
    bool IsOpen { get; }

    /// <summary>Manda um pedido pronto (JSON). False se não deu para mandar.</summary>
    Task<bool> SendAsync(string json, CancellationToken ct);

    /// <summary>Chega do jogo: respostas do mercado, eventos de compra, saldo. Pode vir de qualquer thread.</summary>
    event Action<MarketMessage>? Received;
}

public enum FeedKind { Info, Offer, Bought, Lost, Error }

/// <summary>Para onde a sessão conta o que acontece (feed da tela, notificação, salvar configuração).</summary>
public interface ISniperSink
{
    void Feed(FeedKind kind, string text);

    /// <summary>Comprou <paramref name="qty"/> unidades de <paramref name="offer"/> pela regra, pagando <paramref name="total"/> na moeda da oferta.</summary>
    void Bought(SniperRule rule, MarketOffer offer, int qty, long total);

    /// <summary>A sessão desligou a conta sozinha (falhas seguidas).</summary>
    void Disabled(string reason);

    /// <summary>A configuração mudou (contador de compras, chave desligada): vale salvar.</summary>
    void ConfigChanged();
}
