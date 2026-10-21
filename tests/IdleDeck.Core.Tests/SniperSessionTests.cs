using System.Text.Json;
using IdleDeck.Core.Market;

namespace IdleDeck.Core.Tests;

/// <summary>Canal falso: guarda o que a sessão enviou e responde como o jogo responderia.</summary>
internal sealed class FakeMarketChannel : IMarketChannel
{
    public List<string> Sent { get; } = [];
    public bool IsOpen { get; set; } = true;
    public Func<string, IEnumerable<MarketMessage>> Responder { get; set; } = _ => [];
    public event Action<MarketMessage>? Received;

    public Task<bool> SendAsync(string json, CancellationToken ct)
    {
        Sent.Add(json);
        if (!IsOpen) return Task.FromResult(false);
        foreach (var message in Responder(json)) Received?.Invoke(message);
        return Task.FromResult(true);
    }

    public void Push(MarketMessage message) => Received?.Invoke(message);

    public IReadOnlyList<string> Types => Sent.Select(s => JsonDocument.Parse(s).RootElement.GetProperty("t").GetString()!).ToList();
}

internal sealed class FakeSink : ISniperSink
{
    public List<(FeedKind Kind, string Text)> Feed { get; } = [];
    public List<(SniperRule Rule, MarketOffer Offer, long Total)> BoughtList { get; } = [];
    public List<string> DisabledReasons { get; } = [];
    public int Changes { get; private set; }

    void ISniperSink.Feed(FeedKind kind, string text) => Feed.Add((kind, text));
    void ISniperSink.Bought(SniperRule rule, MarketOffer offer, long total) => BoughtList.Add((rule, offer, total));
    void ISniperSink.Disabled(string reason) => DisabledReasons.Add(reason);
    void ISniperSink.ConfigChanged() => Changes++;
}

public class SniperSessionTests
{
    private static readonly SniperTimeouts Fast = new(TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(300));

    private DateTimeOffset _now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeMarketChannel _channel = new();
    private readonly FakeSink _sink = new();

    private long NowMs => _now.ToUnixTimeMilliseconds();

    private static Task NeverDelay(TimeSpan _, CancellationToken ct) => Task.Delay(Timeout.Infinite, ct);

    private SniperSession NewSession(SniperAccountConfig cfg, bool withBalance = true, Func<TimeSpan, CancellationToken, Task>? delay = null,
        SniperShared? shared = null, FakeMarketChannel? channel = null, FakeSink? sink = null)
    {
        channel ??= _channel;
        var session = new SniperSession(channel, () => cfg, sink ?? _sink, () => _now, Fast, delay: delay ?? NeverDelay, shared: shared);
        if (withBalance) channel.Push(new BalanceMessage(1_000_000, 1_000, NowMs));
        return session;
    }

    private static SniperRule ItemRule(long gold = 5000, long orb = 0, int maxQty = 0) =>
        new() { Id = "r1", Kind = RuleKind.Item, ItemId = 23, Label = "Cocoon Stone", MaxGold = gold, MaxOrb = orb, MaxQty = maxQty, Enabled = true };

    private static SniperAccountConfig Config(params SniperRule[] rules) => new() { Enabled = true, Rules = [.. rules] };

    private MarketOffer ItemOffer(long id = 330733, long price = 1500, long? buyableAtMs = null) =>
        new(id, "item", 23, null, "Cocoon Stone", 1, price, Currency.Gold, "aberto", buyableAtMs ?? NowMs - 5000, false, null, "vendedor");

    /// <summary>Um mercado em que existe uma oferta de Cocoon Stone e a compra termina como <paramref name="outcome"/>.</summary>
    private void ItemMarket(MarketOffer offer, bool? outcome)
    {
        _channel.Responder = json =>
        {
            using var doc = JsonDocument.Parse(json);
            switch (doc.RootElement.GetProperty("t").GetString())
            {
                case "market.itens":
                    return [new ItemsMessage([new ItemSummary(23, offer.Price, null)])];
                case "market.item":
                    return [new ItemOffersMessage(23, Currency.Gold, [offer])];
                case "market.comprar":
                    return outcome is { } ok ? [new PurchaseMessage(offer.Id, PurchasePhase.End, ok, null, null)] : [];
                default:
                    return [];
            }
        };
    }

    [Fact]
    public async Task Buys_an_item_below_the_ceiling_and_counts_it_after_the_game_confirms()
    {
        var rule = ItemRule();
        var offer = ItemOffer();
        ItemMarket(offer, outcome: true);
        var session = NewSession(Config(rule));

        await session.CycleAsync(CancellationToken.None);

        Assert.Equal(["market.itens", "market.item", "market.comprar"], _channel.Types);
        Assert.Equal(MarketProtocol.ItemOffers(23, Currency.Gold), _channel.Sent[1]);
        Assert.Equal(MarketProtocol.Buy(offer), _channel.Sent[2]);
        Assert.Equal(1, rule.Bought);
        var bought = Assert.Single(_sink.BoughtList);
        Assert.Equal(offer.Id, bought.Offer.Id);
        Assert.Equal(1500, bought.Total);
        Assert.Equal(new Balance(1_000_000 - 1500, 1_000), session.Balance);
        Assert.True(_sink.Changes > 0);
    }

    [Fact]
    public async Task A_lost_lottery_is_not_counted_and_is_reported()
    {
        var rule = ItemRule();
        ItemMarket(ItemOffer(), outcome: false);
        var session = NewSession(Config(rule));

        await session.CycleAsync(CancellationToken.None);

        Assert.Equal(0, rule.Bought);
        Assert.Empty(_sink.BoughtList);
        Assert.Contains(_sink.Feed, f => f.Kind == FeedKind.Lost);
        Assert.Equal(new Balance(1_000_000, 1_000), session.Balance);
    }

    [Fact]
    public async Task No_answer_to_the_purchase_is_not_counted_and_does_not_hang_or_throw()
    {
        var rule = ItemRule();
        ItemMarket(ItemOffer(), outcome: null);
        var session = NewSession(Config(rule));

        await session.CycleAsync(CancellationToken.None);

        Assert.Equal(0, rule.Bought);
        Assert.Contains(_sink.Feed, f => f.Kind == FeedKind.Lost);
    }

    [Fact]
    public async Task The_same_offer_is_never_bought_twice()
    {
        var rule = ItemRule();
        ItemMarket(ItemOffer(), outcome: true);
        var session = NewSession(Config(rule));

        await session.CycleAsync(CancellationToken.None);
        await session.CycleAsync(CancellationToken.None);
        await session.CycleAsync(CancellationToken.None);

        Assert.Single(_channel.Types, t => t == "market.comprar");
        Assert.Equal(1, rule.Bought);
    }

    [Fact]
    public async Task Without_a_known_balance_nothing_is_even_asked()
    {
        ItemMarket(ItemOffer(), outcome: true);
        var session = NewSession(Config(ItemRule()), withBalance: false);

        await session.CycleAsync(CancellationToken.None);

        Assert.Empty(_channel.Sent);
    }

    [Fact]
    public async Task A_partial_estado_without_gems_keeps_the_known_gem_balance()
    {
        var session = NewSession(Config(ItemRule()));
        _channel.Push(new BalanceMessage(900, null, null));
        Assert.Equal(new Balance(900, 1_000), session.Balance);
    }

    [Fact]
    public async Task A_disabled_account_or_rule_sends_nothing()
    {
        ItemMarket(ItemOffer(), outcome: true);
        var off = Config(ItemRule());
        off.Enabled = false;
        await NewSession(off).CycleAsync(CancellationToken.None);
        Assert.Empty(_channel.Sent);

        var rule = ItemRule();
        rule.Enabled = false;
        await NewSession(Config(rule)).CycleAsync(CancellationToken.None);
        Assert.Empty(_channel.Sent);
    }

    [Fact]
    public async Task Does_not_look_at_an_item_whose_cheapest_offer_is_above_the_ceiling()
    {
        var offer = ItemOffer(price: 9000);
        ItemMarket(offer, outcome: true);
        var session = NewSession(Config(ItemRule(gold: 5000)));

        await session.CycleAsync(CancellationToken.None);

        Assert.Equal(["market.itens"], _channel.Types);
    }

    [Fact]
    public async Task Shiny_radar_asks_the_whole_market_for_gems_only_and_buys_only_shiny_in_gems()
    {
        var rule = new SniperRule { Id = "s", Kind = RuleKind.AnyShiny, Label = "Qualquer shiny", MaxOrb = 3000, Enabled = true };
        var shiny = new MarketOffer(77, "pokemon", null, 6, "Charizard", 1, 2500, Currency.Orb, "aberto", NowMs - 5000, true, 5.1, "x");
        var plain = new MarketOffer(78, "pokemon", null, 9, "Blastoise", 1, 100, Currency.Orb, "aberto", NowMs - 5000, false, 5.1, "x");
        _channel.Responder = json =>
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("t").GetString() switch
            {
                "market.listar" => [new ListingMessage([plain, shiny])],
                "market.comprar" => [new PurchaseMessage(77, PurchasePhase.End, true, null, null)],
                _ => [],
            };
        };
        var session = NewSession(Config(rule));
        _channel.Push(new BalanceMessage(1_000_000, 10_000, null));

        await session.CycleAsync(CancellationToken.None);

        Assert.Equal(["market.listar", "market.comprar"], _channel.Types);
        using (var listing = JsonDocument.Parse(_channel.Sent[0]))
        {
            Assert.True(listing.RootElement.GetProperty("soShiny").GetBoolean());
            Assert.Equal("orb", listing.RootElement.GetProperty("moeda").GetString());
            Assert.False(listing.RootElement.TryGetProperty("especieId", out _));
        }
        Assert.Equal(1, rule.Bought);
        Assert.Contains("\"id\":77", _channel.Sent[1]);
    }

    [Fact]
    public async Task Three_unanswered_cycles_in_a_row_turn_the_account_off_and_say_so()
    {
        var cfg = Config(ItemRule());
        var session = NewSession(cfg); // o canal não responde nada

        await session.CycleAsync(CancellationToken.None);
        await session.CycleAsync(CancellationToken.None);
        Assert.True(cfg.Enabled);
        await session.CycleAsync(CancellationToken.None);

        Assert.False(cfg.Enabled);
        Assert.Single(_sink.DisabledReasons);
    }

    [Fact]
    public async Task An_answer_resets_the_failure_count()
    {
        var cfg = Config(ItemRule());
        var session = NewSession(cfg);
        await session.CycleAsync(CancellationToken.None);
        await session.CycleAsync(CancellationToken.None);

        _channel.Responder = _ => [new ItemsMessage([])];
        await session.CycleAsync(CancellationToken.None);
        _channel.Responder = _ => [];
        await session.CycleAsync(CancellationToken.None);
        await session.CycleAsync(CancellationToken.None);

        Assert.True(cfg.Enabled);
    }

    [Fact]
    public async Task An_offer_in_retention_is_bought_only_once_after_its_release_time()
    {
        var rule = ItemRule();
        var offer = ItemOffer(buyableAtMs: NowMs + 60_000);
        ItemMarket(offer, outcome: true);
        var session = NewSession(Config(rule));

        await session.CycleAsync(CancellationToken.None);
        Assert.DoesNotContain("market.comprar", _channel.Types);

        _now = _now.AddSeconds(30);
        await session.CycleAsync(CancellationToken.None);
        Assert.DoesNotContain("market.comprar", _channel.Types);

        _now = _now.AddSeconds(31);
        await session.CycleAsync(CancellationToken.None);
        await session.CycleAsync(CancellationToken.None);

        Assert.Single(_channel.Types, t => t == "market.comprar");
        Assert.Equal(1, rule.Bought);
    }

    [Fact]
    public async Task A_pending_purchase_is_dropped_if_the_rule_is_turned_off_meanwhile()
    {
        var rule = ItemRule();
        ItemMarket(ItemOffer(buyableAtMs: NowMs + 60_000), outcome: true);
        var session = NewSession(Config(rule));
        await session.CycleAsync(CancellationToken.None);

        rule.Enabled = false;
        _now = _now.AddSeconds(120);
        await session.CycleAsync(CancellationToken.None);

        Assert.DoesNotContain("market.comprar", _channel.Types);
    }

    [Fact]
    public async Task The_balance_is_never_overspent_by_the_reserve()
    {
        var cfg = Config(ItemRule());
        cfg.ReserveGold = 999_000;
        ItemMarket(ItemOffer(price: 5000), outcome: true); // saldo 1.000.000 − 5.000 < reserva 999.000
        var session = NewSession(cfg);

        await session.CycleAsync(CancellationToken.None);

        Assert.DoesNotContain("market.comprar", _channel.Types);
    }

    [Fact]
    public async Task Cancelling_ends_the_loop_quietly()
    {
        var session = NewSession(Config(ItemRule()));
        using var cts = new CancellationTokenSource();
        var run = session.RunAsync(cts.Token);
        await Task.Delay(50);
        cts.Cancel();
        var finished = await Task.WhenAny(run, Task.Delay(2000));
        Assert.Same(run, finished);
        await run; // não lança
    }

    [Fact]
    public async Task A_closed_channel_sends_nothing_and_does_not_throw()
    {
        ItemMarket(ItemOffer(), outcome: true);
        var session = NewSession(Config(ItemRule()));
        _channel.IsOpen = false;
        await session.CycleAsync(CancellationToken.None);
        Assert.Empty(_channel.Sent);
    }

    // ---------- passada de correção da revisão final ----------

    [Fact]
    public async Task P5_radar_ignores_offers_that_are_not_p5_even_when_cheap()
    {
        var rule = new SniperRule { Id = "p5", Kind = RuleKind.AnyP5, Label = "Qualquer P5", MaxGold = 50_000, Enabled = true };
        var notP5 = new MarketOffer(5, "pokemon", null, 9, "Blastoise", 1, 100, Currency.Gold, "aberto", NowMs - 5000, false, 5.0, "x", 1);
        _channel.Responder = json =>
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("t").GetString() switch
            {
                "market.listar" => [new ListingMessage([notP5])], // também vale para a resposta de outra consulta (a tela do usuário)
                "market.comprar" => [new PurchaseMessage(5, PurchasePhase.End, true, null, null)],
                _ => [],
            };
        };
        var session = NewSession(Config(rule));

        await session.CycleAsync(CancellationToken.None);

        Assert.DoesNotContain("market.comprar", _channel.Types);
    }

    [Fact]
    public async Task A_pokemon_rule_asks_for_its_species_and_ignores_listings_of_other_species()
    {
        var rule = new SniperRule { Id = "p", Kind = RuleKind.Pokemon, SpeciesId = 6, Label = "Charizard", MaxOrb = 500, Enabled = true };
        var other = new MarketOffer(5, "pokemon", null, 9, "Blastoise", 1, 100, Currency.Orb, "aberto", NowMs - 5000, false, 5.0, "x");
        _channel.Responder = json =>
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("t").GetString() switch
            {
                "market.listar" => [new ListingMessage([other])],
                "market.comprar" => [new PurchaseMessage(5, PurchasePhase.End, true, null, null)],
                _ => [],
            };
        };
        var session = NewSession(Config(rule));

        await session.CycleAsync(CancellationToken.None);

        using var listing = JsonDocument.Parse(_channel.Sent[0]);
        Assert.Equal(6, listing.RootElement.GetProperty("especieId").GetInt32());
        Assert.DoesNotContain("market.comprar", _channel.Types);
    }

    [Fact]
    public async Task A_confirmation_that_arrives_late_still_counts_and_the_offer_is_not_bought_again()
    {
        var rule = ItemRule();
        var offer = ItemOffer();
        ItemMarket(offer, outcome: null); // o jogo demora além do tempo de espera
        var session = NewSession(Config(rule));
        await session.CycleAsync(CancellationToken.None);
        Assert.Equal(0, rule.Bought);

        _channel.Push(new PurchaseMessage(offer.Id, PurchasePhase.End, true, null, null)); // chegou tarde

        Assert.Equal(1, rule.Bought);
        Assert.Single(_sink.BoughtList);
        Assert.Equal(new Balance(1_000_000 - 1500, 1_000), session.Balance);

        _now = _now.AddSeconds(25);
        await session.CycleAsync(CancellationToken.None);
        Assert.Single(_channel.Types, t => t == "market.comprar");
    }

    [Fact]
    public async Task A_late_failure_is_reported_without_counting()
    {
        var rule = ItemRule();
        var offer = ItemOffer();
        ItemMarket(offer, outcome: null);
        var session = NewSession(Config(rule));
        await session.CycleAsync(CancellationToken.None);

        _channel.Push(new PurchaseMessage(offer.Id, PurchasePhase.End, false, null, null));

        Assert.Equal(0, rule.Bought);
        Assert.Equal(2, _sink.Feed.Count(f => f.Kind == FeedKind.Lost));
    }

    [Fact]
    public async Task An_unconfirmed_offer_is_not_retried_for_five_minutes()
    {
        var rule = ItemRule();
        ItemMarket(ItemOffer(), outcome: null);
        var session = NewSession(Config(rule));
        await session.CycleAsync(CancellationToken.None);

        _now = _now.AddMinutes(4);
        await session.CycleAsync(CancellationToken.None);
        Assert.Single(_channel.Types, t => t == "market.comprar");

        _now = _now.AddMinutes(1.1);
        await session.CycleAsync(CancellationToken.None);
        Assert.Equal(2, _channel.Types.Count(t => t == "market.comprar"));
    }

    [Fact]
    public async Task Cancelling_in_the_middle_of_a_purchase_still_counts_the_answer()
    {
        var rule = ItemRule();
        var offer = ItemOffer();
        ItemMarket(offer, outcome: null);
        var session = NewSession(Config(rule));
        using var cts = new CancellationTokenSource();

        var cycle = session.CycleAsync(cts.Token);
        await Task.Delay(60); // já mandou o pedido de compra e espera o jogo
        cts.Cancel();
        _channel.Push(new PurchaseMessage(offer.Id, PurchasePhase.End, true, null, null));
        try { await Task.WhenAny(cycle, Task.Delay(1000)); await cycle; } catch (OperationCanceledException) { }

        Assert.Equal(1, rule.Bought);
        Assert.Single(_sink.BoughtList);
    }

    [Fact]
    public async Task A_new_session_sharing_the_ledger_does_not_buy_the_same_offer_again()
    {
        var shared = new SniperShared(new SniperLedger(), new OfferClaims(), "a");
        var rule = ItemRule();
        var cfg = Config(rule);
        var offer = ItemOffer();
        ItemMarket(offer, outcome: true);
        var first = NewSession(cfg, shared: shared);
        await first.CycleAsync(CancellationToken.None);
        first.Detach();

        var second = NewSession(cfg, shared: shared);
        await second.CycleAsync(CancellationToken.None);

        Assert.Single(_channel.Types, t => t == "market.comprar");
    }

    [Fact]
    public async Task Two_accounts_never_go_for_the_same_offer()
    {
        var claims = new OfferClaims();
        var offer = ItemOffer();
        var channelB = new FakeMarketChannel();
        var sinkB = new FakeSink();
        ItemMarket(offer, outcome: true);
        channelB.Responder = _channel.Responder;

        var a = NewSession(Config(ItemRule()), shared: new SniperShared(new SniperLedger(), claims, "conta1"));
        var b = NewSession(Config(ItemRule()), shared: new SniperShared(new SniperLedger(), claims, "conta2"), channel: channelB, sink: sinkB);

        await a.CycleAsync(CancellationToken.None);
        await b.CycleAsync(CancellationToken.None);

        var buys = _channel.Types.Count(t => t == "market.comprar") + channelB.Types.Count(t => t == "market.comprar");
        Assert.Equal(1, buys);
    }

    [Fact]
    public async Task A_scheduled_purchase_fires_at_the_release_time_without_waiting_for_the_next_cycle()
    {
        var release = new TaskCompletionSource();
        var rule = ItemRule();
        ItemMarket(ItemOffer(buyableAtMs: NowMs + 60_000), outcome: true);
        var session = NewSession(Config(rule), delay: (_, ct) => release.Task);

        await session.CycleAsync(CancellationToken.None);
        Assert.DoesNotContain("market.comprar", _channel.Types);

        _now = _now.AddSeconds(61);
        release.SetResult();
        for (var i = 0; i < 50 && rule.Bought == 0; i++) await Task.Delay(20);

        Assert.Equal(1, rule.Bought);
        Assert.Single(_channel.Types, t => t == "market.comprar");
    }

    [Fact]
    public async Task A_scheduled_purchase_is_dropped_if_the_account_is_turned_off_before_the_time()
    {
        var release = new TaskCompletionSource();
        var cfg = Config(ItemRule());
        ItemMarket(ItemOffer(buyableAtMs: NowMs + 60_000), outcome: true);
        var session = NewSession(cfg, delay: (_, ct) => release.Task);
        await session.CycleAsync(CancellationToken.None);

        cfg.Enabled = false;
        _now = _now.AddSeconds(61);
        release.SetResult();
        await Task.Delay(150);

        Assert.DoesNotContain("market.comprar", _channel.Types);
    }

    [Fact]
    public async Task An_unexpected_error_turns_the_account_off_and_says_so()
    {
        var cfg = Config(ItemRule());
        _channel.Responder = _ => throw new InvalidOperationException("quebrou");
        var session = NewSession(cfg);
        using var cts = new CancellationTokenSource();

        var run = session.RunAsync(cts.Token);
        var finished = await Task.WhenAny(run, Task.Delay(2000));
        cts.Cancel();

        Assert.Same(run, finished);
        await run;
        Assert.False(cfg.Enabled);
        Assert.Single(_sink.DisabledReasons);
        Assert.Contains(_sink.Feed, f => f.Kind == FeedKind.Error);
    }

    [Fact]
    public async Task A_session_started_after_the_welcome_knows_the_balance_from_the_shared_tracker()
    {
        var tracker = new BalanceTracker();
        tracker.Apply(new BalanceMessage(1_000_000, 1_000, NowMs), NowMs); // chegou antes de a conta ligar o sniper
        ItemMarket(ItemOffer(), outcome: true);
        var rule = ItemRule();
        var session = NewSession(Config(rule), withBalance: false, shared: new SniperShared(new SniperLedger(), new OfferClaims(), "a", tracker));

        Assert.Equal(new Balance(1_000_000, 1_000), session.Balance);
        await session.CycleAsync(CancellationToken.None);

        Assert.Equal(1, rule.Bought);
        Assert.Equal(new Balance(1_000_000 - 1500, 1_000), tracker.Balance); // o desconto aparece também fora da sessão
    }

    [Fact]
    public async Task A_direct_purchase_confirmed_without_an_offer_id_counts()
    {
        var rule = ItemRule();
        var offer = ItemOffer(price: 1500);
        _channel.Responder = json =>
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("t").GetString() switch
            {
                "market.itens" => [new ItemsMessage([new ItemSummary(23, 1500, null)])],
                "market.item" => [new ItemOffersMessage(23, Currency.Gold, [offer])],
                // compra direta: só o aviso de compra, sem id da oferta
                "market.comprar" => [new PurchaseMessage(0, PurchasePhase.Bought, true, 1500, Currency.Gold)],
                _ => [],
            };
        };
        var session = NewSession(Config(rule));

        await session.CycleAsync(CancellationToken.None);

        Assert.Equal(1, rule.Bought);
        Assert.Single(_sink.BoughtList);
        Assert.DoesNotContain(_sink.Feed, f => f.Kind == FeedKind.Lost);
    }

    [Fact]
    public async Task An_unidentified_confirmation_for_a_different_price_is_not_taken_for_this_purchase()
    {
        var rule = ItemRule();
        var offer = ItemOffer(price: 1500);
        _channel.Responder = json =>
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("t").GetString() switch
            {
                "market.itens" => [new ItemsMessage([new ItemSummary(23, 1500, null)])],
                "market.item" => [new ItemOffersMessage(23, Currency.Gold, [offer])],
                "market.comprar" => [new PurchaseMessage(0, PurchasePhase.Bought, true, 99_999, Currency.Gold)], // outra compra qualquer
                _ => [],
            };
        };
        var session = NewSession(Config(rule));

        await session.CycleAsync(CancellationToken.None);

        Assert.Equal(0, rule.Bought);
    }
}
