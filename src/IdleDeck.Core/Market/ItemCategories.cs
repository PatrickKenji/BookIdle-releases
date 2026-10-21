using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IdleDeck.Core.Market;

/// <summary>Os grupos de item que o jogador conhece (Pedras, TMs, Casas…). O jogo não manda esse grupo: sai do nome do item.</summary>
public static class ItemCategories
{
    public const string Stone = "stone";
    public const string Tm = "tm";
    public const string RareLoot = "rare_loot";
    public const string Beast = "beast";
    public const string Box = "box";
    public const string Casa = "casa";
    public const string Bicicleta = "bicicleta";
    public const string Skin = "skin";
    public const string Other = "outros";
    public const string Pokemon = "pokemon";

    /// <summary>Os atalhos da tela, na ordem em que aparecem (depois de "Todas").</summary>
    public static readonly IReadOnlyList<string> Chips = [Pokemon, Stone, Tm, RareLoot, Beast, Casa, Box, Bicicleta];

    private static readonly Regex TmWord = new(@"\btm\b|tm disk", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Classify(string name, string category, int id)
    {
        var n = (name ?? "").ToLowerInvariant();
        if (id is >= 71000 and < 73000) return Stone; // pedras Mega têm nome próprio (Gengarite…)
        if (n.Contains("skin")) return Skin;
        if (n.Contains("beast ball")) return Beast;
        if (n.Contains("stone") && !n.Contains("fragment")) return Stone;
        if (TmWord.IsMatch(n)) return Tm;
        if (n.Contains("fragment") || n.Contains("token")) return RareLoot;
        if (n.Contains("box")) return Box;
        if (n.Contains("house")) return Casa;
        if (n.Contains("bicycle")) return Bicicleta;
        return Other;
    }

    public static string Label(string group) => group switch
    {
        Stone => Loc.T("Pedras"),
        Tm => "TMs",
        RareLoot => Loc.T("Loots Raros"),
        Beast => "Beast Balls",
        Casa => Loc.T("Casas"),
        Box => "Boxes",
        Bicicleta => Loc.T("Bicicletas"),
        Skin => "Skins",
        Pokemon => "Pokémon",
        _ => Loc.T("Outros"),
    };
}

/// <summary>Comparação de texto sem ligar para maiúsculas nem acentos.</summary>
public static class TextFold
{
    public static string Fold(string text)
    {
        var decomposed = (text ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }
}
