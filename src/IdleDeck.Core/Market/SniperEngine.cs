namespace IdleDeck.Core.Market;

public sealed record Balance(long Gold, long Orb);

/// <summary>Comprar <see cref="Offer"/> (de uma regra). <see cref="WaitUntil"/> null = a retenção já acabou: comprar agora.</summary>
public sealed record BuyDecision(MarketOffer Offer, SniperRule Rule, DateTimeOffset? WaitUntil);

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
        var remaining = SniperRules.Remaining(rule);
        var decisions = new List<BuyDecision>();

        foreach (var offer in offers.OrderBy(o => o.Price))
        {
            if (decisions.Count >= remaining) break;
            if (!Matches(rule, offer) || !ledger.CanTry(offer.Id, now)) continue;

            if (offer.Currency == Currency.Gold)
            {
                if (rule.MaxGold <= 0 || offer.Price > rule.MaxGold || gold - offer.Price < config.ReserveGold) continue;
                gold -= offer.Price;
            }
            else
            {
                if (rule.MaxOrb <= 0 || offer.Price > rule.MaxOrb || orb - offer.Price < config.ReserveOrb) continue;
                orb -= offer.Price;
            }

            var releaseAt = DateTimeOffset.FromUnixTimeMilliseconds(offer.BuyableAtMs - serverOffsetMs);
            decisions.Add(new BuyDecision(offer, rule, releaseAt > now ? releaseAt : null));
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
