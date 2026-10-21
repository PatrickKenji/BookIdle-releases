using IdleDeck.Core.Market;

namespace IdleDeck.Core.Tests;

public class SpeciesCatalogTests
{
    private static SpeciesSummary S(int id, string name) => new(id, name, null, 1, 0);

    [Fact]
    public void Merge_adds_new_species_and_reports_whether_anything_changed()
    {
        var catalog = new SpeciesCatalog();
        Assert.True(catalog.Merge([S(6, "Charizard"), S(9, "Blastoise")]));
        Assert.False(catalog.Merge([S(6, "Charizard")]));
        Assert.True(catalog.Merge([S(6, "Charizard Mega")]));
        Assert.Equal("Charizard Mega", catalog.NameOf(6));
        Assert.Equal(2, catalog.All.Count);
    }

    [Fact]
    public void Merge_ignores_entries_without_a_name()
    {
        var catalog = new SpeciesCatalog();
        Assert.False(catalog.Merge([S(1, ""), S(2, "  ")]));
        Assert.Empty(catalog.All);
    }

    [Fact]
    public void Search_ignores_case_and_accents_and_sorts_by_name()
    {
        var catalog = new SpeciesCatalog();
        catalog.Merge([S(3, "Venusaur"), S(1, "Bulbasaur"), S(2, "Ivysaur"), S(4, "Charmander")]);
        Assert.Equal(["Bulbasaur", "Ivysaur", "Venusaur"], catalog.Search("SAUR").Select(s => s.Name));
        Assert.Equal(4, catalog.Search("").Count);
    }

    [Fact]
    public void Json_roundtrips_and_garbage_is_an_empty_catalog()
    {
        var catalog = new SpeciesCatalog();
        catalog.Merge([S(6, "Charizard"), S(2050, "Furious Scizor")]);
        var again = SpeciesCatalog.FromJson(catalog.ToJson());
        Assert.Equal("Furious Scizor", again.NameOf(2050));
        Assert.Equal(2, again.All.Count);

        Assert.Empty(SpeciesCatalog.FromJson("lixo").All);
        Assert.Empty(SpeciesCatalog.FromJson("").All);
        Assert.Empty(SpeciesCatalog.FromJson("""{"a":1}""").All);
    }
}
