using IdleDeck.Core.Market;

namespace IdleDeck.Core.Tests;

public class SniperSearchTests
{
    private static ItemCatalog Items() => ItemCatalog.Parse("""
        {"items":[
          {"id":23,"name":"Cocoon Stone","category":"stone"},
          {"id":2,"name":"Ancient Stone","category":"stone"},
          {"id":91,"name":"Fire-Type TM Disk","category":"tm"}
        ]}
        """);

    private static SpeciesCatalog Species()
    {
        var species = new SpeciesCatalog();
        species.Merge([new SpeciesSummary(6, "Charizard", null, 2500, 13), new SpeciesSummary(2050, "Furious Scizor", 1950000, 2, 0),
                       new SpeciesSummary(248, "Tyranitar", null, 6000, 5)]);
        return species;
    }

    [Fact]
    public void Without_a_group_it_searches_items_and_pokemon_together_sorted_by_name()
    {
        var found = SniperSearch.Find(Items(), Species(), "", null, 50);
        Assert.Equal(found.Select(f => f.Name).Order(StringComparer.OrdinalIgnoreCase), found.Select(f => f.Name));
        Assert.Contains(found, f => f is { Kind: SearchKind.Pokemon, Name: "Charizard", Id: 6 });
        Assert.Contains(found, f => f is { Kind: SearchKind.Item, Name: "Cocoon Stone", Id: 23 });
    }

    [Fact]
    public void The_pokemon_group_returns_only_species()
    {
        var found = SniperSearch.Find(Items(), Species(), "", ItemCategories.Pokemon, 50);
        Assert.All(found, f => Assert.Equal(SearchKind.Pokemon, f.Kind));
        Assert.Equal(3, found.Count);
    }

    [Fact]
    public void An_item_group_returns_only_items_of_that_group()
    {
        var found = SniperSearch.Find(Items(), Species(), "", ItemCategories.Stone, 50);
        Assert.Equal(["Ancient Stone", "Cocoon Stone"], found.Select(f => f.Name));
        Assert.All(found, f => Assert.Equal("Pedras", f.Label));
    }

    [Fact]
    public void The_result_is_limited_and_missing_catalogs_are_fine()
    {
        Assert.Equal(2, SniperSearch.Find(Items(), Species(), "", null, 2).Count);
        Assert.Empty(SniperSearch.Find(null, null, "", null, 10));
        Assert.Equal(3, SniperSearch.Find(null, Species(), "", null, 10).Count);
        Assert.Equal(3, SniperSearch.Find(Items(), null, "", null, 10).Count);
    }
}
