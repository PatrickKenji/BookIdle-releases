using System.Globalization;
using System.Text.Json;

namespace IdleDeck.Core.Localization;

/// <summary>
/// Idiomas do app. O texto original (a chave) é o português: <c>Loc.T("Salvar")</c> devolve "Salvar" em português e
/// "Save" / "Guardar" nos outros, lidos de <c>Localization/en.json</c> e <c>es.json</c>. Texto sem tradução cai no português.
/// </summary>
public static class Loc
{
    public const string Auto = "auto";

    /// <summary>Código e nome de cada idioma (o nome sempre no próprio idioma), na ordem do seletor.</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Languages =
    [
        ("pt", "Português"),
        ("en", "English"),
        ("es", "Español"),
    ];

    private static Dictionary<string, string>? _table;

    /// <summary>Idioma em uso: "pt", "en" ou "es".</summary>
    public static string Code { get; private set; } = "pt";

    /// <summary>Formato de números e datas do idioma em uso (milhar, vírgula decimal…).</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Escolhe o idioma. "auto" (ou vazio) segue o do Windows: português e espanhol quando for o caso, inglês para o resto.
    /// </summary>
    public static void Use(string? setting) => Use(setting, CultureInfo.CurrentUICulture);

    public static void Use(string? setting, CultureInfo windows)
    {
        Code = Resolve(setting, windows);
        Culture = CultureInfo.GetCultureInfo(Code switch { "en" => "en-US", "es" => "es-ES", _ => "pt-BR" });
        _table = Code == "pt" ? null : Load(Code);
    }

    public static string Resolve(string? setting, CultureInfo windows)
    {
        var chosen = setting?.Trim().ToLowerInvariant();
        if (Languages.Any(l => l.Code == chosen)) return chosen!;
        return windows.TwoLetterISOLanguageName switch
        {
            "pt" => "pt",
            "es" => "es",
            _ => "en",
        };
    }

    /// <summary>O texto no idioma em uso. Sem tradução, devolve o próprio texto (português).</summary>
    public static string T(string portuguese) => Translate(portuguese, _table);

    /// <summary>A tradução na tabela dada; sem tabela ou sem a entrada, o próprio texto em português.</summary>
    public static string Translate(string portuguese, IReadOnlyDictionary<string, string>? table) =>
        table is not null && table.TryGetValue(portuguese, out var translated) ? translated : portuguese;

    /// <summary>Como <see cref="T(string)"/>, preenchendo <c>{0}</c>, <c>{1}</c>… com os valores.</summary>
    public static string T(string portuguese, params object?[] args) =>
        string.Format(Culture, T(portuguese), args);

    /// <summary>Lê a tabela de um idioma (texto em português → tradução). Vazia se o arquivo não existir.</summary>
    public static Dictionary<string, string> Load(string code)
    {
        var assembly = typeof(Loc).Assembly;
        using var stream = assembly.GetManifestResourceStream($"Localization.{code}.json");
        if (stream is null) return [];
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
