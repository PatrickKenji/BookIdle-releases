using System.Text.Json;
using IdleDeck.Core.Market;

namespace IdleDeck.Core.Tests;

public class MarketProtocolTests
{
    private const string ItemsSummary = """
        {"t":"market","aba":"itens","resumo":{"23":{"anuncios":982,"unidades":224262,"minGold":16315,"minOrb":2},
        "70041":{"anuncios":2,"unidades":2,"minGold":null,"minOrb":9000}}}
        """;

    private const string ItemOffers = """
        {"t":"market","aba":"item","itemId":70000,"moeda":"gold","linhas":[
        {"id":330733,"vendedor":"vendedor_a","tipo":"item","itemId":70000,"pokemonId":null,"qtd":1,
         "ficha":{"npc":0,"nome":"Bronze Boss Token","categoria":"loot"},
         "preco":17325000,"moeda":"gold","estado":"aberto","criadoEm":1792592237636,"compravelEm":1792592357636}]}
        """;

    private const string Species = """
        {"t":"market","aba":"especies","total":113,"pagina":0,"porPagina":24,"linhas":[
        {"speciesId":6,"nome":"Charizard","anuncios":13,"shinys":13,"minGold":null,"minOrb":2500,"temP5":false}]}
        """;

    private const string Listing = """
        {"t":"market","aba":"vitrine","total":2,"pagina":0,"linhas":[
        {"id":330584,"vendedor":"vendedor_b","tipo":"pokemon","itemId":null,"pokemonId":24574571,"qtd":1,
         "ficha":{"id":24574571,"nome":"Charizard","nota":5.142,"level":1039,"shiny":true,"speciesId":6,"potencia":1},
         "preco":4000,"moeda":"orb","estado":"aberto","criadoEm":1792591831592,"compravelEm":1792591951592}]}
        """;

    [Fact]
    public void Parse_items_summary()
    {
        var msg = Assert.IsType<ItemsMessage>(MarketProtocol.Parse(ItemsSummary));
        Assert.Equal(2, msg.Items.Count);
        var cocoon = msg.Items.Single(i => i.ItemId == 23);
        Assert.Equal(16315, cocoon.MinGold);
        Assert.Equal(2, cocoon.MinOrb);
        var house = msg.Items.Single(i => i.ItemId == 70041);
        Assert.Null(house.MinGold);
        Assert.Equal(9000, house.MinOrb);
    }

    [Fact]
    public void Parse_item_offers()
    {
        var msg = Assert.IsType<ItemOffersMessage>(MarketProtocol.Parse(ItemOffers));
        Assert.Equal(70000, msg.ItemId);
        Assert.Equal(Currency.Gold, msg.Currency);
        var offer = Assert.Single(msg.Offers);
        Assert.Equal(330733, offer.Id);
        Assert.Equal("item", offer.Kind);
        Assert.Equal(70000, offer.ItemId);
        Assert.Equal("Bronze Boss Token", offer.Name);
        Assert.Equal(17325000, offer.Price);
        Assert.Equal(Currency.Gold, offer.Currency);
        Assert.Equal("aberto", offer.State);
        Assert.Equal(1792592357636, offer.BuyableAtMs);
        Assert.False(offer.Shiny);
        Assert.Null(offer.Nota);
        Assert.Equal("vendedor_a", offer.Seller);
    }

    [Fact]
    public void Parse_species_summary()
    {
        var msg = Assert.IsType<SpeciesMessage>(MarketProtocol.Parse(Species));
        var s = Assert.Single(msg.Species);
        Assert.Equal(6, s.SpeciesId);
        Assert.Equal("Charizard", s.Name);
        Assert.Null(s.MinGold);
        Assert.Equal(2500, s.MinOrb);
        Assert.Equal(13, s.Shinys);
    }

    [Fact]
    public void Parse_pokemon_listing_reads_the_ficha()
    {
        var msg = Assert.IsType<ListingMessage>(MarketProtocol.Parse(Listing));
        var offer = Assert.Single(msg.Offers);
        Assert.Equal("pokemon", offer.Kind);
        Assert.Equal(6, offer.SpeciesId);
        Assert.True(offer.Shiny);
        Assert.Equal(5.142, offer.Nota);
        Assert.Equal(1, offer.Potencia);
        Assert.Equal(4000, offer.Price);
        Assert.Equal(Currency.Orb, offer.Currency);
    }

    [Fact]
    public void Parse_purchase_events_inside_batalha()
    {
        var entered = MarketProtocol.Parse("""{"t":"batalha","ev":[{"k":"mktSorteio","fase":"entrou","id":330726}]}""");
        Assert.Equal(new PurchaseMessage(330726, PurchasePhase.Entered, false, null, null), entered);

        var list = MarketProtocol.Parse("""{"t":"batalha","ev":[{"k":"mktSorteio","fase":"lista","id":330726}]}""");
        Assert.Equal(new PurchaseMessage(330726, PurchasePhase.List, false, null, null), list);

        var end = MarketProtocol.Parse("""{"t":"batalha","ev":[{"k":"mktSorteio","fase":"fim","id":330726,"ok":true,"qtd":1}]}""");
        Assert.Equal(new PurchaseMessage(330726, PurchasePhase.End, true, null, null), end);

        var lost = MarketProtocol.Parse("""{"t":"batalha","ev":[{"k":"mktSorteio","fase":"fim","id":9,"ok":false}]}""");
        Assert.Equal(new PurchaseMessage(9, PurchasePhase.End, false, null, null), lost);

        var bought = MarketProtocol.Parse("""{"t":"batalha","ev":[{"k":"marketComprado","total":17325000,"moeda":"gold","id":330726}]}""");
        Assert.Equal(new PurchaseMessage(330726, PurchasePhase.Bought, true, 17325000, Currency.Gold), bought);
    }

    [Fact]
    public void Parse_purchase_picks_the_market_event_among_battle_events()
    {
        var msg = MarketProtocol.Parse("""{"t":"batalha","ev":[{"k":"ataque","dano":5},{"k":"mktSorteio","fase":"fim","id":7,"ok":true}]}""");
        Assert.Equal(new PurchaseMessage(7, PurchasePhase.End, true, null, null), msg);
    }

    [Fact]
    public void Parse_balance_from_welcome_and_estado()
    {
        var welcome = MarketProtocol.Parse("""{"t":"welcome","estado":{"gold":49474677,"orbs":1,"servidorAgora":1792590000000}}""");
        Assert.Equal(new BalanceMessage(49474677, 1, 1792590000000), welcome);

        var partial = MarketProtocol.Parse("""{"t":"estado","estado":{"gold":49410630,"servidorAgora":1792589876505}}""");
        Assert.Equal(new BalanceMessage(49410630, null, 1792589876505), partial);
    }

    [Theory]
    [InlineData("lixo")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"t":"campo","seq":1}""")]
    [InlineData("""{"t":"batalha","ev":[{"k":"ataque"}]}""")]
    [InlineData("""{"t":"market","aba":"item","linhas":"x"}""")]
    [InlineData("")]
    public void Parse_returns_null_for_anything_else(string json)
    {
        Assert.Null(MarketProtocol.Parse(json));
    }

    [Fact]
    public void Buy_sends_the_offer_price_and_currency_exactly()
    {
        var offer = new MarketOffer(330726, "item", 70000, null, "Bronze Boss Token", 1, 17325000, Currency.Gold, "aberto", 0, false, null, "x");
        Assert.Equal("""{"t":"market.comprar","id":330726,"qtd":1,"preco":17325000,"moeda":"gold","sorteioUi":1}""", MarketProtocol.Buy(offer));
    }

    [Fact]
    public void Items_and_item_offers_requests()
    {
        Assert.Equal("""{"t":"market.itens","elementos":[],"categoria":""}""", MarketProtocol.Items());
        Assert.Equal("""{"t":"market.item","itemId":70060,"moeda":"orb"}""", MarketProtocol.ItemOffers(70060, Currency.Orb));
    }

    [Fact]
    public void Listing_for_shiny_asks_for_orb_and_only_shiny()
    {
        var json = MarketProtocol.Listing(new PokemonFilter(Currency.Orb, OnlyShiny: true, OnlyP5: false, MinNota: null, Order: "baratos"), 6);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("market.listar", root.GetProperty("t").GetString());
        Assert.Equal("pokemon", root.GetProperty("tipo").GetString());
        Assert.Equal("orb", root.GetProperty("moeda").GetString());
        Assert.True(root.GetProperty("soShiny").GetBoolean());
        Assert.False(root.GetProperty("soP5").GetBoolean());
        Assert.Equal("baratos", root.GetProperty("ordem").GetString());
        Assert.Equal(6, root.GetProperty("especieId").GetInt32());
        Assert.Equal(0, root.GetProperty("pagina").GetInt32());
        Assert.Equal("", root.GetProperty("notaMin").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("criterios").ValueKind);
    }

    [Fact]
    public void Species_request_carries_the_nota_filter_and_has_no_species_id()
    {
        var json = MarketProtocol.Species(new PokemonFilter(Currency.Gold, false, true, 7.5, "baratos"));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("market.especies", root.GetProperty("t").GetString());
        Assert.Equal("gold", root.GetProperty("moeda").GetString());
        Assert.True(root.GetProperty("soP5").GetBoolean());
        Assert.Equal("7.5", root.GetProperty("notaMin").GetString());
        Assert.False(root.TryGetProperty("especieId", out _));
    }

    [Fact]
    public void ParseAll_returns_every_market_event_of_one_frame_in_order()
    {
        var all = MarketProtocol.ParseAll("""{"t":"batalha","ev":[{"k":"mktSorteio","fase":"fim","id":330726,"ok":true,"qtd":1},{"k":"marketComprado","total":17325000,"moeda":"gold","id":330726,"sorteio":true}]}""");
        Assert.Equal(2, all.Count);
        Assert.Equal(new PurchaseMessage(330726, PurchasePhase.End, true, null, null), all[0]);
        Assert.Equal(new PurchaseMessage(330726, PurchasePhase.Bought, true, 17325000, Currency.Gold), all[1]);
        Assert.Empty(MarketProtocol.ParseAll("lixo"));
    }

    [Fact]
    public void Parse_species_page_info()
    {
        var msg = Assert.IsType<SpeciesMessage>(MarketProtocol.Parse(Species));
        Assert.Equal(113, msg.Total);
        Assert.Equal(24, msg.PerPage);
        Assert.Equal(0, msg.Page);
    }

    [Fact]
    public void Parse_welcome_brings_the_market_catalog_after_the_balance()
    {
        var all = MarketProtocol.ParseAll("""
            {"t":"welcome","estado":{"gold":10,"orbs":2,"servidorAgora":99},
             "mercado":{"catalogo":[{"id":70041,"name":"Uncommon House","category":"casa"},{"id":70060,"nome":"Founder Box","categoria":"caixa"},{"name":"sem id"}]}}
            """);
        Assert.Equal(2, all.Count);
        Assert.IsType<BalanceMessage>(all[0]);
        var catalog = Assert.IsType<CatalogMessage>(all[1]);
        Assert.Equal(["Uncommon House", "Founder Box"], catalog.Items.Select(i => i.Name));
        Assert.Equal(70060, catalog.Items[1].Id);
    }

    [Fact]
    public void Parse_welcome_catalog_alone_when_there_is_no_balance()
    {
        var all = MarketProtocol.ParseAll("""{"t":"welcome","mercado":{"catalogo":[{"id":1,"name":"Air Tank"}]}}""");
        Assert.Single(all);
        Assert.IsType<CatalogMessage>(all[0]);
    }

    [Fact]
    public void Species_request_can_ask_for_another_page()
    {
        using var doc = JsonDocument.Parse(MarketProtocol.Species(new PokemonFilter(Currency.Gold, false, false, null, "recentes"), 3));
        Assert.Equal(3, doc.RootElement.GetProperty("pagina").GetInt32());
    }

    [Fact]
    public void Listing_without_a_species_asks_for_the_whole_market()
    {
        using var doc = JsonDocument.Parse(MarketProtocol.Listing(new PokemonFilter(Currency.Orb, true, false, null, "baratos"), null));
        Assert.False(doc.RootElement.TryGetProperty("especieId", out _));
        Assert.True(doc.RootElement.GetProperty("soShiny").GetBoolean());
    }

    [Fact]
    public void A_direct_purchase_confirmation_without_an_offer_id_is_still_a_purchase()
    {
        // compra fora do sorteio (anúncio já liberado há mais de 3 s): o aviso não traz `id` nem `sorteio`
        var msg = MarketProtocol.Parse("""{"t":"batalha","ev":[{"k":"marketComprado","descricao":"1× Bronze Boss Token","total":17325000,"moeda":"gold","sobra":0,"trancado":false}]}""");
        Assert.Equal(new PurchaseMessage(0, PurchasePhase.Bought, true, 17325000, Currency.Gold), msg);
    }

    [Fact]
    public void Events_without_an_id_that_are_not_purchases_are_still_ignored()
    {
        Assert.Null(MarketProtocol.Parse("""{"t":"batalha","ev":[{"k":"mktSorteio","fase":"fim","ok":true}]}"""));
        Assert.Null(MarketProtocol.Parse("""{"t":"batalha","ev":[{"k":"ataque","dano":5}]}"""));
    }
}
