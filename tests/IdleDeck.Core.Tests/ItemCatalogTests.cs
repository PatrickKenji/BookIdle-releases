using IdleDeck.Core.Market;

namespace IdleDeck.Core.Tests;

public class ItemCatalogTests
{
    private const string Json = """
        {"items":[
          {"id":2,"name":"Ancient Stone","icon":"a.gif","category":"stone","rare":true,"npcPrice":50000},
          {"id":23,"name":"Cocoon Stone","icon":"c.gif","category":"stone","rare":true,"npcPrice":5000},
          {"id":40530,"name":"AoE TM Disk Piece","icon":"p.png","category":"loot","rare":false,"npcPrice":0},
          {"id":70000,"name":"Bronze Boss Token","category":"loot","rare":true},
          {"id":91,"name":"Fire-Type TM Disk","category":"tm","rare":false},
          {"name":"sem id","category":"loot"},
          {"id":5}
        ]}
        """;

    [Fact]
    public void Parse_reads_the_items_and_skips_broken_entries()
    {
        var catalog = ItemCatalog.Parse(Json);
        Assert.Equal(5, catalog.All.Count);
        Assert.Equal("Cocoon Stone", catalog.ById(23)!.Name);
        Assert.True(catalog.ById(70000)!.Rare);
        Assert.Null(catalog.ById(5));
    }

    [Fact]
    public void Parse_accepts_a_plain_list_too()
    {
        var catalog = ItemCatalog.Parse("""[{"id":1,"name":"Air Tank","category":"loot"}]""");
        Assert.Single(catalog.All);
    }

    [Theory]
    [InlineData("")]
    [InlineData("lixo")]
    [InlineData("{}")]
    [InlineData("""{"items":"x"}""")]
    public void Parse_of_garbage_is_an_empty_catalog(string json)
    {
        Assert.Empty(ItemCatalog.Parse(json).All);
    }

    [Fact]
    public void Search_matches_a_piece_of_the_name_ignoring_case_and_accents()
    {
        var catalog = ItemCatalog.Parse(Json);
        Assert.Equal(["Cocoon Stone"], catalog.Search("COCOON", null).Select(i => i.Name));
        Assert.Equal(["Ancient Stone", "Cocoon Stone"], catalog.Search("stone", null).Select(i => i.Name));
    }

    [Fact]
    public void Search_with_empty_text_returns_everything_sorted_by_name()
    {
        var names = ItemCatalog.Parse(Json).Search("  ", null).Select(i => i.Name).ToList();
        Assert.Equal(5, names.Count);
        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), names);
    }

    [Fact]
    public void Search_can_be_limited_to_a_group()
    {
        var catalog = ItemCatalog.Parse(Json);
        Assert.Equal(["Ancient Stone", "Cocoon Stone"], catalog.Search("", ItemCategories.Stone).Select(i => i.Name));
        Assert.Equal(["AoE TM Disk Piece", "Fire-Type TM Disk"], catalog.Search("", ItemCategories.Tm).Select(i => i.Name));
        Assert.Equal(["Bronze Boss Token"], catalog.Search("", ItemCategories.RareLoot).Select(i => i.Name));
        Assert.Empty(catalog.Search("cocoon", ItemCategories.Tm));
    }

    [Theory]
    [InlineData(23, "Cocoon Stone", "stone", "stone")]
    [InlineData(71500, "Gengarite", "loot", "stone")]
    [InlineData(40530, "AoE TM Disk Piece", "loot", "tm")]
    [InlineData(91, "Fire-Type TM Disk", "tm", "tm")]
    [InlineData(70000, "Bronze Boss Token", "loot", "rare_loot")]
    [InlineData(70103, "Beautiful Scales Fragment", "loot", "rare_loot")]
    [InlineData(70015, "Mega Shiny Stone Fragment", "loot", "rare_loot")]
    [InlineData(70060, "Founder Box", "loot", "box")]
    [InlineData(70041, "Uncommon House", "", "casa")]
    [InlineData(70080, "Mountain Bicycle", "", "bicicleta")]
    [InlineData(70200, "Beast Ball", "loot", "beast")]
    [InlineData(70300, "Hero Skin", "", "skin")]
    [InlineData(41, "Electric Box", "loot", "box")]
    [InlineData(1, "Air Tank", "loot", "outros")]
    public void Items_are_grouped_by_name_like_the_game_players_expect(int id, string name, string category, string group)
    {
        Assert.Equal(group, ItemCategories.Classify(name, category, id));
    }

    [Theory]
    [InlineData("stone", "Pedras")]
    [InlineData("tm", "TMs")]
    [InlineData("rare_loot", "Loots Raros")]
    [InlineData("beast", "Beast Balls")]
    [InlineData("casa", "Casas")]
    [InlineData("box", "Boxes")]
    [InlineData("bicicleta", "Bicicletas")]
    [InlineData("skin", "Skins")]
    [InlineData("outros", "Outros")]
    [InlineData("pokemon", "Pokémon")]
    public void Group_labels_are_in_portuguese(string group, string label)
    {
        Assert.Equal(label, ItemCategories.Label(group));
    }

    [Fact]
    public void Merge_adds_unknown_items_without_renaming_known_ones_and_skips_untradeable()
    {
        var catalog = ItemCatalog.Parse(Json);
        var added = catalog.Merge(
        [
            new CatalogItem(23, "Outro nome", "stone", true),
            new CatalogItem(70041, "Uncommon House", "", false),
            new CatalogItem(70060, "Founder Box", "", false),
            new CatalogItem(204, "Super Potion", "heal", false),
        ]);

        Assert.Equal(2, added);
        Assert.Equal("Cocoon Stone", catalog.ById(23)!.Name);
        Assert.Equal("casa", catalog.ById(70041)!.Group);
        Assert.Null(catalog.ById(204));
        Assert.Equal(7, catalog.All.Count);
    }

    [Fact]
    public void ToJson_roundtrips_through_Parse()
    {
        var catalog = ItemCatalog.Parse(Json);
        catalog.Merge([new CatalogItem(70041, "Uncommon House", "", false)]);
        var again = ItemCatalog.Parse(catalog.ToJson());
        Assert.Equal(catalog.All.Select(i => (i.Id, i.Name)).Order(), again.All.Select(i => (i.Id, i.Name)).Order());
    }
}
