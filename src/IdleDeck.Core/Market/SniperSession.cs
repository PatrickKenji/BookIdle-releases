using System.Collections.Concurrent;
using System.Globalization;

namespace IdleDeck.Core.Market;

public sealed record SniperTimeouts(TimeSpan Request, TimeSpan Purchase)
{
    public static readonly SniperTimeouts Default = new(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(15));
}

/// <summary>
/// O sniper de uma conta: de tempos em tempos consulta o mercado só do que as regras pedem, decide com o
/// <see cref="SniperEngine"/>, compra e espera o jogo confirmar. Um pedido em voo por vez.
/// </summary>
public sealed class SniperSession
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(3);
    private const int MaxFailures = 3;
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly IMarketChannel _channel;
    private readonly Func<SniperAccountConfig> _config;
    private readonly ISniperSink _sink;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SniperTimeouts _timeouts;
    private readonly TimeSpan _interval;
    private readonly SniperLedger _ledger;
    private readonly OfferClaims? _claims;
    private readonly string _owner;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ConcurrentDictionary<long, BuyDecision> _pending = [];
    private readonly ConcurrentDictionary<long, (SniperRule Rule, MarketOffer Offer, int Qty)> _unconfirmed = [];
    private readonly object _gate = new();
    private readonly List<(Func<MarketMessage, bool> Match, TaskCompletionSource<MarketMessage> Source)> _waiters = [];
    private int _failures;
    private readonly BalanceTracker _tracker;
    private bool _announcedNoBalance;

    public SniperSession(IMarketChannel channel, Func<SniperAccountConfig> config, ISniperSink sink, Func<DateTimeOffset> clock,
        SniperTimeouts? timeouts = null, TimeSpan? interval = null, Func<TimeSpan, CancellationToken, Task>? delay = null, SniperShared? shared = null)
    {
        _ledger = shared?.Ledger ?? new SniperLedger();
        _tracker = shared?.Tracker ?? new BalanceTracker();
        _claims = shared?.Claims;
        _owner = shared?.Owner ?? "";
        _delay = delay ?? Task.Delay;
        _channel = channel;
        _config = config;
        _sink = sink;
        _clock = clock;
        _timeouts = timeouts ?? SniperTimeouts.Default;
        _interval = interval is { } given ? (given > MinInterval ? given : MinInterval) : TimeSpan.FromSeconds(5);
        _channel.Received += OnReceived;
    }

    /// <summary>Saldo conhecido do jogo (null até chegar). Descontado aqui a cada compra, até o próximo aviso do jogo.</summary>
    public Balance? Balance => _tracker.Balance;

    /// <summary>Relógio do servidor menos o local, em ms.</summary>
    public long ServerOffsetMs => _tracker.ServerOffsetMs;

    public void Detach() => _channel.Received -= OnReceived;

    public async Task RunAsync(CancellationToken ct)
    {
        var delay = _interval;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_config().Enabled)
                {
                    var before = _failures;
                    await CycleAsync(ct);
                    delay = _failures > before ? TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, delay.Ticks * 2)) : _interval;
                    await Task.Delay(delay, ct);
                }
                else
                {
                    ClearPending();
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            var config = _config();
            config.Enabled = false;
            _pending.Clear();
            _sink.Feed(FeedKind.Error, Loc.T("Erro inesperado: {0}. Sniper desligado nesta conta.", e.Message));
            _sink.Disabled(Loc.T("Erro inesperado."));
            _sink.ConfigChanged();
        }
        finally { Detach(); }
    }

    /// <summary>Uma volta: dispara o que estava esperando a retenção e consulta o mercado do que as regras pedem.</summary>
    public async Task CycleAsync(CancellationToken ct)
    {
        var config = _config();
        if (!config.Enabled || !_channel.IsOpen) return;
        if (Balance is null)
        {
            if (!_announcedNoBalance) _sink.Feed(FeedKind.Info, Loc.T("Esperando o saldo da conta chegar do jogo…"));
            _announcedNoBalance = true;
            return;
        }

        await FirePendingAsync(config, ct);

        ItemsMessage? items = null;
        foreach (var rule in config.Rules.ToList())
        {
            if (!config.Enabled) return;
            if (!rule.Enabled || SniperRules.Remaining(rule) <= 0) continue;
            ct.ThrowIfCancellationRequested();

            var ok = rule.Kind == RuleKind.Item
                ? await WatchItemAsync(config, rule, items, v => items = v, ct)
                : await WatchPokemonAsync(config, rule, ct);
            if (!ok) return; // falha de protocolo: encerra a volta (e conta)
        }
        _failures = 0;
    }

    // ---------- Itens ----------

    private async Task<bool> WatchItemAsync(SniperAccountConfig config, SniperRule rule, ItemsMessage? cached, Action<ItemsMessage> remember, CancellationToken ct)
    {
        var items = cached;
        if (items is null)
        {
            items = await RequestAsync<ItemsMessage>(MarketProtocol.Items(), m => true, ct);
            if (items is null) return Fail(Loc.T("O jogo não respondeu à consulta de itens do mercado."));
            remember(items);
        }

        var summary = items.Items.FirstOrDefault(i => i.ItemId == rule.ItemId);
        if (summary is null) return true;

        foreach (var currency in new[] { Currency.Gold, Currency.Orb })
        {
            var (ceiling, min) = currency == Currency.Gold ? (rule.MaxGold, summary.MinGold) : (rule.MaxOrb, summary.MinOrb);
            if (ceiling <= 0 || min is null || min > ceiling) continue;

            var id = rule.ItemId!.Value;
            var offers = await RequestAsync<ItemOffersMessage>(MarketProtocol.ItemOffers(id, currency),
                m => m.ItemId == id && m.Currency == currency, ct);
            if (offers is null) return Fail(Loc.T("O jogo não respondeu à consulta das ofertas do item."));
            if (!await ActAsync(config, rule, offers.Offers, ct)) return false;
        }
        return true;
    }

    // ---------- Pokémon ----------

    private async Task<bool> WatchPokemonAsync(SniperAccountConfig config, SniperRule rule, CancellationToken ct)
    {
        foreach (var currency in new[] { Currency.Gold, Currency.Orb })
        {
            var ceiling = currency == Currency.Gold ? rule.MaxGold : rule.MaxOrb;
            if (ceiling <= 0 || (rule.Kind == RuleKind.AnyShiny && currency == Currency.Gold)) continue;

            var filter = new PokemonFilter(currency, rule.Kind == RuleKind.AnyShiny, rule.Kind == RuleKind.AnyP5,
                rule.Kind == RuleKind.MinNota ? rule.MinNota : null, "baratos");

            // Os radares pedem o mercado todo de uma vez (o jogo já filtra e ordena); só a regra de uma espécie pede a dela.
            int? species = rule.Kind == RuleKind.Pokemon ? rule.SpeciesId : null;
            var listing = await RequestAsync<ListingMessage>(MarketProtocol.Listing(filter, species), m => true, ct);
            if (listing is null) return Fail(Loc.T("O jogo não respondeu à consulta das ofertas de Pokémon."));

            // respostas de outras consultas (a tela do usuário, por exemplo) não valem para a espécie pedida
            var offers = species is { } id ? listing.Offers.Where(o => o.SpeciesId == id).ToList() : listing.Offers;
            if (!await ActAsync(config, rule, offers, ct)) return false;
        }
        return true;
    }

    // ---------- Decidir e comprar ----------

    private async Task<bool> ActAsync(SniperAccountConfig config, SniperRule rule, IReadOnlyList<MarketOffer> offers, CancellationToken ct)
    {
        var decisions = SniperEngine.Decide(config, rule, offers, Balance, _ledger, _clock(), ServerOffsetMs);
        foreach (var decision in decisions)
        {
            if (_claims is not null && !_claims.TryClaim(decision.Offer.Id, _owner, _clock())) continue; // outra conta já foi atrás desta oferta
            if (decision.WaitUntil is { } at)
            {
                if (_pending.TryAdd(decision.Offer.Id, decision))
                {
                    _sink.Feed(FeedKind.Offer, Loc.T("{0} em retenção, compra às {1:HH:mm:ss}.", Describe(decision.Offer, decision.Qty), at.ToLocalTime()));
                    _ = FireWhenReadyAsync(decision, at, ct);
                }
                continue;
            }
            if (!await BuyAsync(config, decision, ct)) return false;
        }
        return true;
    }

    private async Task FirePendingAsync(SniperAccountConfig config, CancellationToken ct)
    {
        foreach (var (id, decision) in _pending.ToList())
        {
            // revalida com o saldo, a reserva e o limite de agora; a regra pode ter mudado
            var again = SniperEngine.Decide(config, decision.Rule, [decision.Offer], Balance, _ledger, _clock(), ServerOffsetMs);
            if (again.Count == 0 || !config.Rules.Contains(decision.Rule)) { _pending.TryRemove(id, out _); continue; }
            if (again[0].WaitUntil is not null) continue;
            if (!_pending.TryRemove(id, out _)) continue;
            if (!await BuyAsync(config, again[0], ct)) return;
        }
    }

    /// <summary>Espera o fim da retenção (relógio do servidor) e compra na hora, sem esperar a próxima volta.</summary>
    private async Task FireWhenReadyAsync(BuyDecision decision, DateTimeOffset at, CancellationToken ct)
    {
        try
        {
            var wait = at - _clock();
            await _delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, ct);
            if (!_pending.TryRemove(decision.Offer.Id, out _)) return; // outra via já tratou
            var config = _config();
            if (!config.Enabled) return;
            var again = SniperEngine.Decide(config, decision.Rule, [decision.Offer], Balance, _ledger, _clock(), ServerOffsetMs);
            if (again.Count == 0 || !config.Rules.Contains(decision.Rule) || again[0].WaitUntil is not null) return;
            await BuyAsync(config, again[0], ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            _sink.Feed(FeedKind.Error, Loc.T("Erro na compra agendada: {0}", e.Message));
        }
    }

    /// <summary>Compra as unidades decididas e espera o jogo dizer se levou. False se o jogo não confirmou nada (conta como falha).</summary>
    private async Task<bool> BuyAsync(SniperAccountConfig config, BuyDecision decision, CancellationToken ct)
    {
        var offer = decision.Offer;
        var rule = decision.Rule;
        var qty = Math.Max(1, decision.Qty);
        _sink.Feed(FeedKind.Offer, Loc.T("{0} — comprando (regra {1}).", Describe(offer, qty), RuleName(rule)));
        _ledger.Started(offer.Id, _clock());

        // Cancelar no meio de uma compra perderia a resposta: o pedido já saiu, então espera o fim (o tempo é limitado).
        var result = await RequestAsync<PurchaseMessage>(MarketProtocol.Buy(offer, qty),
            m => ConfirmsPurchase(m, offer, qty), CancellationToken.None, _timeouts.Purchase);

        if (result is null)
        {
            // pode ter dado certo e a resposta só estar atrasada: não repete por um bom tempo e ainda conta se ela chegar
            _ledger.Unknown(offer.Id, _clock());
            _unconfirmed[offer.Id] = (rule, offer, qty);
            _sink.Feed(FeedKind.Lost, Loc.T("{0}: o jogo não confirmou a compra.", Describe(offer, qty)));
            return Fail(Loc.T("O jogo não confirmou a compra."));
        }

        _failures = 0;
        if (!result.Ok)
        {
            _ledger.Finished(offer.Id, bought: false, _clock());
            _sink.Feed(FeedKind.Lost, Loc.T("{0}: não levou (outro jogador, ou a oferta mudou).", Describe(offer, qty)));
            return true;
        }

        Settle(rule, offer, Delivered(result, offer, qty));
        return true;
    }

    /// <summary>
    /// A resposta é desta compra? Com sorteio ela traz o id da oferta; na compra direta o aviso não traz id, e então só vale
    /// se o valor e a moeda baterem com o pedido que acabou de sair (um pedido em voo por vez, por conta).
    /// O jogo pode entregar menos unidades do que o pedido (outro jogador levou parte): vale um total de 1 até
    /// <paramref name="qty"/> vezes o preço.
    /// </summary>
    private static bool ConfirmsPurchase(PurchaseMessage m, MarketOffer offer, int qty) =>
        (m.Id == offer.Id && m.Phase is PurchasePhase.End or PurchasePhase.Bought)
        || (m.Id == 0 && m.Phase == PurchasePhase.Bought && m.Currency == offer.Currency && m.Total is { } total
            && (offer.Price <= 0 ? total == 0 : total % offer.Price == 0 && total / offer.Price >= 1 && total / offer.Price <= qty));

    /// <summary>Quantas unidades vieram: o que o jogo disser (qtd, ou total ÷ preço); sem isso, o que foi pedido.</summary>
    private static int Delivered(PurchaseMessage m, MarketOffer offer, int asked)
    {
        long? told = m.Qty ?? (m.Total is { } total && offer.Price > 0 && total % offer.Price == 0 ? total / offer.Price : (long?)null);
        return told is { } n ? (int)Math.Clamp(n, 1, asked) : asked;
    }

    /// <summary>A compra deu certo: registra, desconta do saldo local, conta na regra e avisa.</summary>
    private void Settle(SniperRule rule, MarketOffer offer, int qty)
    {
        // se sobrou parte do anúncio (limite da regra, saldo, ou o jogo entregou menos), a sobra pode ser comprada depois
        _ledger.Finished(offer.Id, bought: qty >= offer.Qty, _clock());
        rule.Bought += qty;
        var total = offer.Price * qty;
        _tracker.Deduct(offer.Currency, total);
        _sink.Feed(FeedKind.Bought, Loc.T("Comprou {0}.", Describe(offer, qty)));
        _sink.Bought(rule, offer, qty, total);
        _sink.ConfigChanged();
    }

    // ---------- Falhas ----------

    private bool Fail(string reason)
    {
        if (++_failures < MaxFailures) return false;
        var config = _config();
        config.Enabled = false;
        _failures = 0;
        ClearPending();
        _sink.Feed(FeedKind.Error, Loc.T("{0} Sniper desligado nesta conta depois de {1} falhas seguidas.", reason, MaxFailures));
        _sink.Disabled(reason);
        _sink.ConfigChanged();
        return false;
    }

    private void ClearPending() => _pending.Clear();

    // ---------- Pedido e resposta ----------

    private async Task<T?> RequestAsync<T>(string json, Func<T, bool> match, CancellationToken ct, TimeSpan? timeout = null) where T : MarketMessage
    {
        var source = new TaskCompletionSource<MarketMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiter = (Match: (Func<MarketMessage, bool>)(m => m is T t && match(t)), Source: source);
        lock (_gate) _waiters.Add(waiter);
        try
        {
            if (!await _channel.SendAsync(json, ct)) return null;
            var finished = await Task.WhenAny(source.Task, Task.Delay(timeout ?? _timeouts.Request, ct));
            ct.ThrowIfCancellationRequested();
            return finished == source.Task ? (T)await source.Task : null;
        }
        finally
        {
            lock (_gate) _waiters.Remove(waiter);
        }
    }

    private void OnReceived(MarketMessage message)
    {
        if (message is BalanceMessage b)
        {
            _tracker.Apply(b, _clock().ToUnixTimeMilliseconds());
            return;
        }

        if (message is PurchaseMessage { Phase: PurchasePhase.End or PurchasePhase.Bought } late && _unconfirmed.TryRemove(late.Id, out var pending))
        {
            if (late.Ok) Settle(pending.Rule, pending.Offer, Delivered(late, pending.Offer, pending.Qty));
            else
            {
                _ledger.Finished(pending.Offer.Id, bought: false, _clock());
                _sink.Feed(FeedKind.Lost, Loc.T("{0}: não levou (resposta tardia do jogo).", Describe(pending.Offer, pending.Qty)));
            }
            return;
        }

        List<TaskCompletionSource<MarketMessage>> hits;
        lock (_gate) hits = _waiters.Where(w => w.Match(message)).Select(w => w.Source).ToList();
        foreach (var hit in hits) hit.TrySetResult(message);
    }

    private static string Describe(MarketOffer o, int qty = 1)
    {
        var name = string.IsNullOrEmpty(o.Name) ? Loc.T("oferta {0}", o.Id) : o.Name;
        var currency = o.Currency == Currency.Gold ? Loc.T("coins") : Loc.T("gemas");
        return qty > 1
            ? Loc.T("{0}× {1} por {2} {3} cada", qty, name, o.Price.ToString("N0", Loc.Culture), currency)
            : Loc.T("{0} por {1} {2}", name, o.Price.ToString("N0", Loc.Culture), currency);
    }

    private static string RuleName(SniperRule r) => string.IsNullOrWhiteSpace(r.Label) ? r.Kind.ToString() : r.Label;
}
