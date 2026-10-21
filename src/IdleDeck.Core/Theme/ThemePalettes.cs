using IdleDeck.Core.Settings;

namespace IdleDeck.Core.Theme;

/// <summary>
/// Cores de cada tema: chave do pincel (a mesma de <c>Themes/Arcade.xaml</c>) → <c>#RRGGBB</c>.
/// O app troca a cor dos pincéis no lugar, então tudo que usa o pincel acompanha sem recarregar nada.
/// O <c>HoverGlow</c> (com transparência) não entra: é igual nos dois temas.
/// </summary>
public static class ThemePalettes
{
    private static readonly IReadOnlyDictionary<string, string> Arcade = new Dictionary<string, string>
    {
        ["Bg"] = "#1B1030",
        ["Side"] = "#24143F",
        ["TopBar"] = "#2A1748",
        ["Card"] = "#2F1A52",
        ["CardActive"] = "#4A2785",
        ["Pill"] = "#3B2263",
        ["Field"] = "#160C28",
        ["Outline"] = "#120A22",
        ["Purple"] = "#C45CFF",
        ["Yellow"] = "#FFD25A",
        ["YellowText"] = "#3B1D00",
        ["Green"] = "#5CFF9D",
        ["Orange"] = "#FF8A3D",
        ["Text"] = "#F4ECFF",
        ["Muted"] = "#C4B2E6",
        ["Err"] = "#FF5C7A",
        ["CheckerA"] = "#27144A",
        ["CheckerB"] = "#2D1850",
    };

    /// <summary>Preto e cinza neutros; mesmos acentos amarelo/verde/laranja/vermelho, roxo mais frio.</summary>
    private static readonly IReadOnlyDictionary<string, string> Dark = new Dictionary<string, string>
    {
        ["Bg"] = "#121214",
        ["Side"] = "#1A1A1E",
        ["TopBar"] = "#1E1E23",
        ["Card"] = "#232329",
        ["CardActive"] = "#34343C",
        ["Pill"] = "#2B2B32",
        ["Field"] = "#0D0D0F",
        ["Outline"] = "#000000",
        ["Purple"] = "#9D8CFF",
        ["Yellow"] = "#FFD25A",
        ["YellowText"] = "#3B1D00",
        ["Green"] = "#5CFF9D",
        ["Orange"] = "#FF8A3D",
        ["Text"] = "#EDEDED",
        ["Muted"] = "#A0A0AA",
        ["Err"] = "#FF5C7A",
        ["CheckerA"] = "#18181C",
        ["CheckerB"] = "#1C1C21",
    };

    public static IReadOnlyDictionary<string, string> For(ThemeKind kind) => kind == ThemeKind.Dark ? Dark : Arcade;
}
