using IdleDeck.Core.Market;

namespace IdleDeck.Core.Tests;

public class SniperEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly long NowMs = Now.ToUnixTimeMilliseconds();

    private static MarketOffer Offer(long id = 1, long price = 1000, Currency currency = Currency.Gold, int itemId = 23,
        string state = "aberto", long? buyableAtMs = null, string kind = "item", bool shiny = false, double? nota = null, int? species = null, int? potencia = null) =>
        new(id, kind, kind == "item" ? itemId : null, species, "x", 1, price, currency, state, buyableAtMs ?? NowMs - 1000, shiny, nota, "vendedor", potencia);

    private static SniperRule ItemRule(long gold = 5000, long orb = 0, int maxQty = 0, int bought = 0, bool enabled = true) =>
        new() { Id = "r1", Kind = RuleKind.Item, ItemId = 23, MaxGold = gold, MaxOrb = orb, MaxQty = maxQty, Bought = bought, Enabled = enabled };

    private static IReadOnlyList<BuyDecision> Decide(SniperRule rule, IReadOnlyList<MarketOffer> offers, Balance? balance = null,
        SniperLedger? ledger = null, SniperAccountConfig? cfg = null, long offset = 0) =>
        SniperEngine.Decide(cfg ?? new SniperAccountConfig { Enabled = true, Rules = [rule] }, rule, offers,
            balance ?? new Balance(1_000_000, 1_000), ledger ?? new SniperLedger(), Now, offset);

    [Fact]
    public void Buys_an_open_offer_within_the_ceiling()
    {
        var d = Assert.Single(Decide(ItemRule(), [Offer(price: 5000)]));
        Assert.Equal(1, d.Offer.Id);
        Assert.Null(d.WaitUntil);
    }

    [Fact]
    public void Refuses_a_price_above_the_ceiling()
    {
        Assert.Empty(Decide(ItemRule(gold: 5000), [Offer(price: 5001)]));
    }

    [Fact]
    public void A_zero_ceiling_means_no_buying_in_that_currency()
    {
        Assert.Empty(Decide(ItemRule(gold: 0, orb: 50), [Offer(price: 1, currency: Currency.Gold)]));
        Assert.Single(Decide(ItemRule(gold: 0, orb: 50), [Offer(price: 50, currency: Currency.Orb)]));
    }

    [Fact]
    public void Refuses_offers_that_are_not_open_or_not_for_this_item()
    {
        Assert.Empty(Decide(ItemRule(), [Offer(state: "vendido")]));
        Assert.Empty(Decide(ItemRule(), [Offer(itemId: 99)]));
    }

    [Fact]
    public void A_disabled_rule_buys_nothing()
    {
        Assert.Empty(Decide(ItemRule(enabled: false), [Offer()]));
    }

    [Fact]
    public void Unknown_balance_buys_nothing()
    {
        var rule = ItemRule();
        Assert.Empty(SniperEngine.Decide(new SniperAccountConfig { Rules = [rule] }, rule, [Offer()], null, new SniperLedger(), Now, 0));
    }

    [Fact]
    public void Refuses_when_the_balance_cannot_pay()
    {
        Assert.Empty(Decide(ItemRule(), [Offer(price: 2000)], new Balance(1999, 0)));
        Assert.Single(Decide(ItemRule(), [Offer(price: 2000)], new Balance(2000, 0)));
    }

    [Fact]
    public void The_reserve_is_never_spent_not_even_by_one_coin()
    {
        var cfg = new SniperAccountConfig { ReserveGold = 500 };
        var rule = ItemRule();
        cfg.Rules = [rule];
        Assert.Empty(Decide(rule, [Offer(price: 1500)], new Balance(1999, 0), cfg: cfg));
        Assert.Single(Decide(rule, [Offer(price: 1500)], new Balance(2000, 0), cfg: cfg));
    }

    [Fact]
    public void The_gem_reserve_is_separate_from_the_coin_reserve()
    {
        var cfg = new SniperAccountConfig { ReserveOrb = 10 };
        var rule = ItemRule(gold: 0, orb: 100);
        cfg.Rules = [rule];
        Assert.Empty(Decide(rule, [Offer(price: 95, currency: Currency.Orb)], new Balance(0, 100), cfg: cfg));
        Assert.Single(Decide(rule, [Offer(price: 90, currency: Currency.Orb)], new Balance(0, 100), cfg: cfg));
    }

    [Fact]
    public void The_balance_is_spent_across_offers_of_the_same_batch()
    {
        var d = Decide(ItemRule(), [Offer(1, 3000), Offer(2, 3000)], new Balance(5000, 0));
        Assert.Single(d);
    }

    [Fact]
    public void Cheapest_first()
    {
        var d = Decide(ItemRule(), [Offer(1, 3000), Offer(2, 1000), Offer(3, 2000)]);
        Assert.Equal([2L, 3L, 1L], d.Select(x => x.Offer.Id));
    }

    [Fact]
    public void Max_quantity_limits_the_number_of_decisions()
    {
        var d = Decide(ItemRule(maxQty: 3, bought: 1), [Offer(1), Offer(2), Offer(3), Offer(4)]);
        Assert.Equal(2, d.Count);
        Assert.Empty(Decide(ItemRule(maxQty: 3, bought: 3), [Offer(1)]));
    }

    [Fact]
    public void An_offer_still_in_retention_waits_for_the_server_release_time()
    {
        var releaseServerMs = NowMs + 60_000 + 5_000; // servidor 5 s à frente do relógio local
        var d = Assert.Single(Decide(ItemRule(), [Offer(buyableAtMs: releaseServerMs)], offset: 5_000));
        Assert.Equal(Now.AddSeconds(60), d.WaitUntil);
    }

    [Fact]
    public void An_offer_already_released_is_bought_now_even_with_a_clock_offset()
    {
        var d = Assert.Single(Decide(ItemRule(), [Offer(buyableAtMs: NowMs + 1_000)], offset: 5_000));
        Assert.Null(d.WaitUntil);
    }

    [Fact]
    public void An_offer_already_bought_or_in_flight_is_not_tried_again()
    {
        var ledger = new SniperLedger();
        ledger.Started(1, Now);
        Assert.Empty(Decide(ItemRule(), [Offer(1)], ledger: ledger));
        ledger.Finished(1, bought: true, Now);
        Assert.Empty(Decide(ItemRule(), [Offer(1)], ledger: ledger));
        Assert.Single(Decide(ItemRule(), [Offer(2)], ledger: ledger));
    }

    [Fact]
    public void A_failed_offer_comes_back_only_after_the_cooldown()
    {
        var ledger = new SniperLedger();
        ledger.Started(1, Now);
        ledger.Finished(1, bought: false, Now);
        Assert.False(ledger.CanTry(1, Now.AddSeconds(19)));
        Assert.True(ledger.CanTry(1, Now.AddSeconds(20)));
    }

    [Fact]
    public void Shiny_radar_only_takes_shiny_offers_in_gems()
    {
        var rule = new SniperRule { Id = "s", Kind = RuleKind.AnyShiny, MaxOrb = 3000, Enabled = true };
        var shiny = Offer(1, 2500, Currency.Orb, kind: "pokemon", shiny: true, species: 6);
        var plain = Offer(2, 100, Currency.Orb, kind: "pokemon", shiny: false, species: 6);
        var inCoins = Offer(3, 100, Currency.Gold, kind: "pokemon", shiny: true, species: 6);
        var d = Decide(rule, [shiny, plain, inCoins], new Balance(1_000_000, 10_000));
        Assert.Equal([1L], d.Select(x => x.Offer.Id));
    }

    [Fact]
    public void Nota_radar_checks_the_cutoff_locally_too()
    {
        var rule = new SniperRule { Id = "n", Kind = RuleKind.MinNota, MinNota = 7.0, MaxGold = 300_000, Enabled = true };
        var good = Offer(1, 250_000, kind: "pokemon", nota: 7.2, species: 6);
        var low = Offer(2, 250_000, kind: "pokemon", nota: 6.9, species: 6);
        var unknown = Offer(3, 250_000, kind: "pokemon", nota: null, species: 6);
        Assert.Equal([1L], Decide(rule, [good, low, unknown]).Select(x => x.Offer.Id));
    }

    [Fact]
    public void Pokemon_rule_matches_the_species()
    {
        var rule = new SniperRule { Id = "p", Kind = RuleKind.Pokemon, SpeciesId = 6, MaxOrb = 100, Enabled = true };
        var d = Decide(rule, [Offer(1, 50, Currency.Orb, kind: "pokemon", species: 6), Offer(2, 50, Currency.Orb, kind: "pokemon", species: 7)]);
        Assert.Equal([1L], d.Select(x => x.Offer.Id));
    }

    [Fact]
    public void P5_radar_buys_only_offers_whose_power_is_5()
    {
        var rule = new SniperRule { Id = "p5", Kind = RuleKind.AnyP5, MaxGold = 50_000, Enabled = true };
        var p5 = Offer(1, 20_000, kind: "pokemon", species: 6, potencia: 5);
        var p1 = Offer(2, 10_000, kind: "pokemon", species: 6, potencia: 1);
        var unknown = Offer(3, 10_000, kind: "pokemon", species: 6, potencia: null);
        Assert.Equal([1L], Decide(rule, [p5, p1, unknown]).Select(x => x.Offer.Id));
    }
}
