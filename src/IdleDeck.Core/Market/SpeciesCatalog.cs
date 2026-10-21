using System.Text.Json;

namespace IdleDeck.Core.Market;

public sealed record CatalogSpecies(int Id, string Name);

/// <summary>
/// As espécies que o mercado já mostrou (id e nome). O jogo não tem uma lista pronta: ela é aprendida das respostas
/// <c>market.especies</c> e guardada em disco, para a busca de Pokémon da regra.
/// </summary>
public sealed class SpeciesCatalog
{
    private readonly Dictionary<int, string> _names = [];

    public IReadOnlyList<CatalogSpecies> All =>
        _names.Select(p => new CatalogSpecies(p.Key, p.Value)).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public string? NameOf(int id) => _names.GetValueOrDefault(id);

    /// <summary>Guarda as espécies vistas. True se algo novo (ou com outro nome) entrou.</summary>
    public bool Merge(IEnumerable<SpeciesSummary> species)
    {
        var changed = false;
        foreach (var s in species)
        {
            var name = s.Name?.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            if (_names.TryGetValue(s.SpeciesId, out var known) && known == name) continue;
            _names[s.SpeciesId] = name;
            changed = true;
        }
        return changed;
    }

    public IReadOnlyList<CatalogSpecies> Search(string text)
    {
        var needle = TextFold.Fold(text.Trim());
        return All.Where(s => needle.Length == 0 || TextFold.Fold(s.Name).Contains(needle, StringComparison.Ordinal)).ToList();
    }

    public string ToJson() => JsonSerializer.Serialize(_names.ToDictionary(p => p.Key.ToString(), p => p.Value));

    public static SpeciesCatalog FromJson(string json)
    {
        var catalog = new SpeciesCatalog();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return catalog;
            foreach (var p in doc.RootElement.EnumerateObject())
                if (int.TryParse(p.Name, out var id) && p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString()))
                    catalog._names[id] = p.Value.GetString()!;
        }
        catch (JsonException) { }
        return catalog;
    }
}
