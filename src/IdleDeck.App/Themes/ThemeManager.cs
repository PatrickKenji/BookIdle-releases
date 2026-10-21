using System.Windows;
using System.Windows.Media;
using IdleDeck.Core.Settings;
using IdleDeck.Core.Theme;

namespace IdleDeck.App.Themes;

/// <summary>
/// Troca o tema em tempo real: muda a cor dos pincéis do <c>Application.Resources</c> no lugar.
/// Quem referencia o pincel (XAML com DynamicResource ou código que guardou o pincel) acompanha,
/// e nada precisa ser recriado — as contas do jogo não recarregam.
/// </summary>
public static class ThemeManager
{
    public static void Apply(ThemeKind kind)
    {
        var resources = Application.Current.Resources;
        foreach (var (key, hex) in ThemePalettes.For(kind))
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            if (resources[key] is not SolidColorBrush brush) continue;
            if (!brush.IsFrozen)
            {
                brush.Color = color;
                continue;
            }
            // pincel congelado (não deveria acontecer): troca o recurso; o DynamicResource acompanha
            resources[key] = new SolidColorBrush(color);
        }
        WindowChrome.SetCaptionColor(ThemePalettes.For(kind)["TopBar"]);
    }
}
