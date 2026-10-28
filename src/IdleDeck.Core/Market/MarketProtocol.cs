using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IdleDeck.Core.Market;

/// <summary>
/// Como o mercado do pokeidle.io fala (um WebSocket com JSON <c>{"t": ...}</c>): monta os pedidos e lê as respostas.
/// Medido no jogo em 2026-10-21; ver docs/pokeidle-mercado.md.
/// </summary>
public static class MarketProtocol
{
    public static string Items() => """{"t":"market.itens","elementos":[],"categoria":""}""";

    public static string ItemOffers(int itemId, Currency currency) =>
        new JsonObject { ["t"] = "market.item", ["itemId"] = itemId, ["moeda"] = CurrencyWire.ToWire(currency) }.ToJsonString();

    public static string Species(PokemonFilter filter, int page = 0) => Request("market.especies", filter, null, page);

    /// <summary>Ofertas de Pokémon. Sem espécie (null) traz o mercado todo, já com os filtros do jogo (shiny, P5, nota).</summary>
    public static string Listing(PokemonFilter filter, int? speciesId) => Request("market.listar", filter, speciesId, 0);

    /// <summary>Compra de <paramref name="qty"/> unidades, com o preço (por unidade) e a moeda que a oferta mostra (nunca um preço do app).</summary>
    public static string Buy(MarketOffer offer, int qty = 1) =>
        new JsonObject
        {
            ["t"] = "market.comprar",
            ["id"] = offer.Id,
            ["qtd"] = qty,
            ["preco"] = offer.Price,
            ["moeda"] = CurrencyWire.ToWire(offer.Currency),
            ["sorteioUi"] = 1,
        }.ToJsonString();

    private static string Request(string type, PokemonFilter f, int? speciesId, int page)
    {
        var o = new JsonObject
        {
            ["t"] = type,
            ["tipo"] = "pokemon",
            ["moeda"] = CurrencyWire.ToWire(f.Currency),
            ["busca"] = "",
            ["ordem"] = f.Order,
            ["criterios"] = new JsonArray(),
            ["elementos"] = new JsonArray(),
            ["categoria"] = "",
            ["soShiny"] = f.OnlyShiny,
            ["soP5"] = f.OnlyP5,
            ["soMega"] = false,
            ["soLendario"] = false,
            ["soTmElemental"] = false,
            ["soTmAoe"] = false,
            ["semOutland"] = false,
            ["nivelMin"] = "",
            ["nivelMax"] = "",
            ["potenciaMin"] = "",
            ["potenciaMax"] = "",
            ["ivMin"] = "",
            ["ivMax"] = "",
            ["qualidadeMin"] = "",
            ["qualidadeMax"] = "",
            ["notaMin"] = f.MinNota is { } n ? n.ToString("0.###", CultureInfo.InvariantCulture) : "",
            ["notaMax"] = "",
            ["pagina"] = page,
        };
        if (speciesId is { } id) o["especieId"] = id;
        return o.ToJsonString();
    }

    /// <summary>A primeira mensagem do mercado que o texto traz, ou null (nunca lança).</summary>
    public static MarketMessage? Parse(string json) => ParseAll(json).FirstOrDefault();

    /// <summary>
    /// Todas as mensagens do mercado de um quadro do jogo, na ordem. Um quadro "batalha" pode trazer o fim do sorteio
    /// e a compra juntos. O que não é do mercado vira lista vazia; nunca lança.
    /// </summary>
    public static IReadOnlyList<MarketMessage> ParseAll(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Str(root, "t") is not { } t) return [];
            return t switch
            {
                "market" => ParseMarket(root) is { } m ? [m] : [],
                "batalha" => ParseBattle(root),
                "welcome" => ParseWelcome(root),
                "estado" => ParseBalance(root) is { } b ? [b] : [],
                _ => [],
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            return [];
        }
    }

    private static MarketMessage? ParseMarket(JsonElement root)
    {
        switch (Str(root, "aba"))
        {
            case "itens":
                if (!root.TryGetProperty("resumo", out var resumo) || resumo.ValueKind != JsonValueKind.Object) return null;
                var items = new List<ItemSummary>();
                foreach (var p in resumo.EnumerateObject())
                    if (int.TryParse(p.Name, out var id) && p.Value.ValueKind == JsonValueKind.Object)
                        items.Add(new ItemSummary(id, Long(p.Value, "minGold"), Long(p.Value, "minOrb")));
                return new ItemsMessage(items);

            case "item":
                if (Int(root, "itemId") is not { } itemId || !CurrencyWire.TryParse(Str(root, "moeda"), out var currency)) return null;
                return Rows(root, "linhas") is { } itemRows ? new ItemOffersMessage(itemId, currency, itemRows) : null;

            case "especies":
                if (!root.TryGetProperty("linhas", out var rows) || rows.ValueKind != JsonValueKind.Array) return null;
                var species = new List<SpeciesSummary>();
                foreach (var r in rows.EnumerateArray())
                    if (r.ValueKind == JsonValueKind.Object && Int(r, "speciesId") is { } sid)
                        species.Add(new SpeciesSummary(sid, Str(r, "nome") ?? "", Long(r, "minGold"), Long(r, "minOrb"), Int(r, "shinys") ?? 0));
                return new SpeciesMessage(species, Int(root, "total") ?? 0, Int(root, "porPagina") ?? 0, Int(root, "pagina") ?? 0);

            case "vitrine":
                return Rows(root, "linhas") is { } listing ? new ListingMessage(listing) : null;

            default:
                return null;
        }
    }

    private static List<MarketOffer>? Rows(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var rows) || rows.ValueKind != JsonValueKind.Array) return null;
        var offers = new List<MarketOffer>();
        foreach (var r in rows.EnumerateArray())
            if (r.ValueKind == JsonValueKind.Object && ParseOffer(r) is { } offer) offers.Add(offer);
        return offers;
    }

    private static MarketOffer? ParseOffer(JsonElement r)
    {
        if (Long(r, "id") is not { } id || Long(r, "preco") is not { } price || !CurrencyWire.TryParse(Str(r, "moeda"), out var currency))
            return null;
        r.TryGetProperty("ficha", out var ficha);
        var hasFicha = ficha.ValueKind == JsonValueKind.Object;
        return new MarketOffer(
            id,
            Str(r, "tipo") ?? "",
            Int(r, "itemId"),
            hasFicha ? Int(ficha, "speciesId") : null,
            hasFicha ? Str(ficha, "nome") ?? "" : "",
            Int(r, "qtd") ?? 1,
            price,
            currency,
            Str(r, "estado") ?? "",
            Long(r, "compravelEm") ?? 0,
            hasFicha && ficha.TryGetProperty("shiny", out var sh) && sh.ValueKind == JsonValueKind.True,
            hasFicha ? Double(ficha, "nota") : null,
            Str(r, "vendedor") ?? "",
            hasFicha ? Int(ficha, "potencia") : null);
    }

    private static IReadOnlyList<MarketMessage> ParseBattle(JsonElement root)
    {
        if (!root.TryGetProperty("ev", out var ev) || ev.ValueKind != JsonValueKind.Array) return [];
        var list = new List<MarketMessage>();
        foreach (var e in ev.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            switch (Str(e, "k"))
            {
                case "mktSorteio":
                    if (Long(e, "id") is not { } id) break;
                    var ok = e.TryGetProperty("ok", out var okValue) && okValue.ValueKind == JsonValueKind.True;
                    switch (Str(e, "fase"))
                    {
                        case "entrou": list.Add(new PurchaseMessage(id, PurchasePhase.Entered, false, null, null)); break;
                        case "lista": list.Add(new PurchaseMessage(id, PurchasePhase.List, false, null, null)); break;
                        case "fim": list.Add(new PurchaseMessage(id, PurchasePhase.End, ok, null, null, Int(e, "qtd"))); break;
                    }
                    break;
                case "marketComprado":
                    // Na compra com sorteio o aviso traz o id da oferta; na compra DIRETA (anúncio já liberado) não traz:
                    // id 0 = "uma compra minha, sem dizer qual" (quem espera confere o valor e a moeda).
                    Currency? currency = CurrencyWire.TryParse(Str(e, "moeda"), out var c) ? c : null;
                    list.Add(new PurchaseMessage(Long(e, "id") ?? 0, PurchasePhase.Bought, true, Long(e, "total"), currency,
                        Int(e, "qtd") ?? QtyFromDescription(Str(e, "descricao"))));
                    break;
            }
        }
        return list;
    }

    /// <summary>"20× Bronze Boss Token" → 20 (o aviso de compra conta as unidades só no texto).</summary>
    private static int? QtyFromDescription(string? text)
    {
        var mark = text?.IndexOf('×') ?? -1;
        return mark > 0 && int.TryParse(text![..mark].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var qty) && qty > 0 ? qty : null;
    }

    private static IReadOnlyList<MarketMessage> ParseWelcome(JsonElement root)
    {
        var list = new List<MarketMessage>();
        if (ParseBalance(root) is { } balance) list.Add(balance);
        if (root.TryGetProperty("mercado", out var mercado) && mercado.ValueKind == JsonValueKind.Object
            && mercado.TryGetProperty("catalogo", out var catalogo) && catalogo.ValueKind == JsonValueKind.Array)
        {
            var items = new List<CatalogItem>();
            foreach (var e in catalogo.EnumerateArray())
                if (ItemCatalog.ReadItem(e) is { } item) items.Add(item);
            if (items.Count > 0) list.Add(new CatalogMessage(items));
        }
        return list;
    }

    private static BalanceMessage? ParseBalance(JsonElement root)
    {
        if (!root.TryGetProperty("estado", out var estado) || estado.ValueKind != JsonValueKind.Object) return null;
        var gold = Long(estado, "gold");
        var orb = Long(estado, "orbs");
        var now = Long(estado, "servidorAgora");
        return gold is null && orb is null && now is null ? null : new BalanceMessage(gold, orb, now);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    private static int? Int(JsonElement e, string name) =>
        Long(e, name) is { } n && n is >= int.MinValue and <= int.MaxValue ? (int)n : null;

    private static double? Double(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
