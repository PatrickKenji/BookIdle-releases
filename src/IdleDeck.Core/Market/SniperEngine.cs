namespace IdleDeck.Core.Market;

public sealed record Balance(long Gold, long Orb);

/// <summary>
/// Comprar <see cref="Qty"/> unidades de <see cref="Offer"/> (de uma regra). <see cref="WaitUntil"/> null = a retenção já acabou:
/// comprar agora.
/// </summary>
public sealed record BuyDecision(MarketOffer Offer, SniperRule Rule, DateTimeOffset? WaitUntil, int Qty = 1);

/// <summary>Decide, sem tocar em rede nem em tela, quais das ofertas vistas uma regra manda comprar.</summary>
public static class SniperEngine
{
    /// <param name="serverOffsetMs">Relógio do servidor menos o local, em ms (para converter <c>compravelEm</c>).</param>
    public static IReadOnlyList<BuyDecision> Decide(SniperAccountConfig config, SniperRule rule, IReadOnlyList<MarketOffer> offers,
        Balance? balance, SniperLedger ledger, DateTimeOffset now, long serverOffsetMs)
    {
        if (balance is null || !rule.Enabled) return [];

        var gold = balance.Gold;
        var orb = balance.Orb;
        var remaining = SniperRules.Remaining(rule); // em unidades, não em ofertas
        var decisions = new List<BuyDecision>();

        foreach (var offer in offers.OrderBy(o => o.Price))
        {
            if (remaining <= 0) break;
            if (!Matches(rule, offer) || !ledger.CanTry(offer.Id, now)) continue;

            var isGold = offer.Currency == Currency.Gold;
            var ceiling = isGold ? rule.MaxGold : rule.MaxOrb;
            if (ceiling <= 0 || offer.Price > ceiling) continue;

            // leva o anúncio inteiro, se o limite da regra e o saldo acima da reserva deixarem; senão o quanto der
            var budget = isGold ? gold - config.ReserveGold : orb - config.ReserveOrb;
            var affordable = offer.Price <= 0 ? long.MaxValue : budget / offer.Price;
            var qty = Math.Min(Math.Max(1, offer.Qty), Math.Min(remaining, affordable));
            if (qty < 1) continue;

            if (isGold) gold -= offer.Price * qty;
            else orb -= offer.Price * qty;
            remaining -= qty;

            var releaseAt = DateTimeOffset.FromUnixTimeMilliseconds(offer.BuyableAtMs - serverOffsetMs);
            decisions.Add(new BuyDecision(offer, rule, releaseAt > now ? releaseAt : null, (int)qty));
        }
        return decisions;
    }

    private static bool Matches(SniperRule rule, MarketOffer offer)
    {
        if (offer.State != "aberto" || offer.Price < 0) return false;
        return rule.Kind switch
        {
            RuleKind.Item => offer.Kind == "item" && offer.ItemId == rule.ItemId,
            RuleKind.Pokemon => offer.Kind == "pokemon" && offer.SpeciesId == rule.SpeciesId,
            RuleKind.AnyShiny => offer.Kind == "pokemon" && offer.Shiny && offer.Currency == Currency.Orb,
            RuleKind.AnyP5 => offer.Kind == "pokemon" && offer.Potencia == 5,
            RuleKind.MinNota => offer.Kind == "pokemon" && offer.Nota is { } n && n >= rule.MinNota,
            _ => false,
        };
    }
}
