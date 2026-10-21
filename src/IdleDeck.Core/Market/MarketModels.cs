namespace IdleDeck.Core.Market;

/// <summary>Moeda do mercado: <c>gold</c> são coins, <c>orb</c> são gemas.</summary>
public enum Currency { Gold, Orb }

public static class CurrencyWire
{
    public static string ToWire(Currency c) => c == Currency.Gold ? "gold" : "orb";

    public static bool TryParse(string? text, out Currency currency)
    {
        switch (text)
        {
            case "gold": currency = Currency.Gold; return true;
            case "orb": currency = Currency.Orb; return true;
            default: currency = default; return false;
        }
    }
}

/// <summary>Uma oferta aberta no mercado. <paramref name="BuyableAtMs"/> é o fim da retenção (relógio do servidor).</summary>
public sealed record MarketOffer(
    long Id, string Kind, int? ItemId, int? SpeciesId, string Name, int Qty, long Price, Currency Currency,
    string State, long BuyableAtMs, bool Shiny, double? Nota, string Seller, int? Potencia = null);

public sealed record ItemSummary(int ItemId, long? MinGold, long? MinOrb);

public sealed record SpeciesSummary(int SpeciesId, string Name, long? MinGold, long? MinOrb, int Shinys);

public enum PurchasePhase { Entered, List, End, Bought }

/// <summary>O que o jogo conta sobre o mercado (ou sobre o saldo e o relógio dele).</summary>
public abstract record MarketMessage;

public sealed record ItemsMessage(IReadOnlyList<ItemSummary> Items) : MarketMessage;

public sealed record ItemOffersMessage(int ItemId, Currency Currency, IReadOnlyList<MarketOffer> Offers) : MarketMessage;

/// <summary>Resumo por espécie; <paramref name="Total"/>/<paramref name="PerPage"/>/<paramref name="Page"/> dizem quantas páginas existem.</summary>
public sealed record SpeciesMessage(IReadOnlyList<SpeciesSummary> Species, int Total = 0, int PerPage = 0, int Page = 0) : MarketMessage;

/// <summary>Catálogo de itens do mercado que o jogo manda ao conectar (inclui casas, boxes e bicicletas).</summary>
public sealed record CatalogMessage(IReadOnlyList<CatalogItem> Items) : MarketMessage;

/// <summary>Ofertas de Pokémon (aba "vitrine").</summary>
public sealed record ListingMessage(IReadOnlyList<MarketOffer> Offers) : MarketMessage;

public sealed record PurchaseMessage(long Id, PurchasePhase Phase, bool Ok, long? Total, Currency? Currency) : MarketMessage;

public sealed record BalanceMessage(long? Gold, long? Orb, long? ServerNowMs) : MarketMessage;

/// <summary>Filtros de Pokémon do jogo. <paramref name="Order"/>: "baratos" ou "recentes".</summary>
public sealed record PokemonFilter(Currency Currency, bool OnlyShiny, bool OnlyP5, double? MinNota, string Order);
