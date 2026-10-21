namespace IdleDeck.Core.Market;

public enum SearchKind { Item, Pokemon }

/// <summary>Um resultado da busca da regra: um item ou um Pokémon, com o rótulo do grupo.</summary>
public sealed record SearchEntry(SearchKind Kind, int Id, string Name, string Label);

/// <summary>A busca única "Item / Pokémon" da nova regra.</summary>
public static class SniperSearch
{
    /// <param name="group">null = tudo; <see cref="ItemCategories.Pokemon"/> = só Pokémon; outro grupo = só itens dele.</param>
    public static IReadOnlyList<SearchEntry> Find(ItemCatalog? items, SpeciesCatalog? species, string text, string? group, int max)
    {
        var found = new List<SearchEntry>();
        if (group != ItemCategories.Pokemon && items is not null)
            found.AddRange(items.Search(text, group).Select(i => new SearchEntry(SearchKind.Item, i.Id, i.Name, ItemCategories.Label(i.Group))));
        if ((group is null || group == ItemCategories.Pokemon) && species is not null)
            found.AddRange(species.Search(text).Select(s => new SearchEntry(SearchKind.Pokemon, s.Id, s.Name, ItemCategories.Label(ItemCategories.Pokemon))));
        return found.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Take(max).ToList();
    }
}
