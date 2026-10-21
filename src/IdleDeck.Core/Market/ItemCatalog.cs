using System.Text.Json;

namespace IdleDeck.Core.Market;

public sealed record CatalogItem(int Id, string Name, string Category, bool Rare)
{
    /// <summary>Grupo da tela (Pedras, TMs, Casas…), tirado do nome.</summary>
    public string Group => ItemCategories.Classify(Name, Category, Id);
}

/// <summary>
/// Os itens do jogo: <c>/assets/items.json</c> mais o catálogo do mercado que o próprio jogo manda ao conectar
/// (casas, boxes e bicicletas só existem nele). Alimenta a busca da regra.
/// </summary>
public sealed class ItemCatalog
{
    private static readonly HashSet<string> NotTradeable = ["heal", "potion"];

    private readonly List<CatalogItem> _items;
    private readonly Dictionary<int, CatalogItem> _byId;

    private ItemCatalog(List<CatalogItem> items)
    {
        _items = items;
        _byId = [];
        foreach (var item in items) _byId.TryAdd(item.Id, item);
    }

    public IReadOnlyList<CatalogItem> All => _items;

    public CatalogItem? ById(int id) => _byId.GetValueOrDefault(id);

    /// <summary>Aceita <c>{"items":[…]}</c> ou uma lista pura; ignora entradas sem id/nome; texto inválido vira catálogo vazio.</summary>
    public static ItemCatalog Parse(string json)
    {
        var items = new List<CatalogItem>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = doc.RootElement;
            if (list.ValueKind == JsonValueKind.Object && list.TryGetProperty("items", out var inner)) list = inner;
            if (list.ValueKind == JsonValueKind.Array)
                foreach (var e in list.EnumerateArray())
                    if (ReadItem(e) is { } item) items.Add(item);
        }
        catch (JsonException) { }
        return new ItemCatalog(items);
    }

    /// <summary>Lê um item de <c>{id, name|nome, category|categoria, rare}</c>; null se faltar id ou nome.</summary>
    public static CatalogItem? ReadItem(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object
            || !e.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var itemId))
            return null;
        var name = Text(e, "name") ?? Text(e, "nome");
        if (string.IsNullOrWhiteSpace(name)) return null;
        var category = Text(e, "category") ?? Text(e, "categoria") ?? "";
        var rare = e.TryGetProperty("rare", out var r) && r.ValueKind == JsonValueKind.True;
        return new CatalogItem(itemId, name, category, rare);
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Acrescenta os itens que ainda não existem (nunca renomeia). Itens que não vão ao mercado ficam de fora. Devolve quantos entraram.</summary>
    public int Merge(IEnumerable<CatalogItem> items)
    {
        var added = 0;
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Name) || item.Name.StartsWith("Item #", StringComparison.Ordinal)) continue;
            if (NotTradeable.Contains(item.Category.ToLowerInvariant()) || _byId.ContainsKey(item.Id)) continue;
            _items.Add(item);
            _byId[item.Id] = item;
            added++;
        }
        return added;
    }

    /// <summary>Itens cujo nome tem o texto (sem ligar para maiúsculas nem acentos), em ordem de nome; grupo opcional.</summary>
    public IReadOnlyList<CatalogItem> Search(string text, string? group)
    {
        var needle = TextFold.Fold(text.Trim());
        return _items
            .Where(i => group is null || i.Group == group)
            .Where(i => needle.Length == 0 || TextFold.Fold(i.Name).Contains(needle, StringComparison.Ordinal))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Lista pura que <see cref="Parse"/> lê de volta (cópia em disco).</summary>
    public string ToJson() =>
        JsonSerializer.Serialize(_items.Select(i => new { id = i.Id, name = i.Name, category = i.Category, rare = i.Rare }));
}
