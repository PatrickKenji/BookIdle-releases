using System.Windows;
using System.Windows.Controls;
namespace IdleDeck.App.Lives;

/// <summary>Lista dos canais (Twitch ou Kick) que dão bônus neste jogo, um por linha, em ordem de prioridade.</summary>
public sealed class LiveChannelsWindow : Window
{
    private readonly TextBox _box;

    public List<string> Channels { get; private set; } = [];

    /// <param name="official">Canais oficiais do PokeIdle nesta plataforma (o botão "Usar lista oficial" põe todos).</param>
    /// <param name="normalize">Limpa a lista digitada (endereço colado, maiúsculas, repetidos).</param>
    public LiveChannelsWindow(Window owner, string site, string profileName, string header, string hint,
        IEnumerable<string> current, IReadOnlyList<string> official, Func<IEnumerable<string>, List<string>> normalize)
    {
        Owner = owner;
        Title = $"{site} · {profileName}";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)Application.Current.Resources["ArcadeWindow"];

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = header,
            Style = (Style)Application.Current.Resources["SectionTitle"],
            Margin = new Thickness(0, 0, 0, 8),
        });
        var hintBlock = new TextBlock
        {
            Text = hint,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8),
        };
        hintBlock.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        panel.Children.Add(hintBlock);

        // assinante ganha o dobro de pontos: esses canais devem ocupar as 2 vagas primeiro
        var subTip = new TextBlock
        {
            Text = Loc.T("⭐ Coloque em primeiro lugar os canais em que você é sub: sub ganha o dobro de pontos. O app assiste sempre as 2 primeiras lives da lista que estiverem ao vivo (a Twitch e a Kick só dão pontos em 2 por vez). Quando uma acaba, entra a próxima; quando um canal mais acima entra ao vivo, ele assume a vaga na hora."),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        subTip.SetResourceReference(TextBlock.ForegroundProperty, "Yellow");
        panel.Children.Add(subTip);

        _box = new TextBox
        {
            AcceptsReturn = true,
            Height = 150,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = string.Join(Environment.NewLine, current),
        };
        panel.Children.Add(_box);

        var useOfficial = new Button
        {
            Content = Loc.T("Usar lista oficial do PokeIdle ({0} canais)", official.Count),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            ToolTip = Loc.T("Troca a lista pelos canais oficiais do jogo nesta plataforma. Depois, suba os canais em que você é sub."),
        };
        useOfficial.Click += (_, _) => _box.Text = string.Join(Environment.NewLine, official);
        panel.Children.Add(useOfficial);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(new Button { Content = Loc.T("Cancelar"), IsCancel = true });
        var save = new Button { Content = Loc.T("Salvar"), IsDefault = true, Style = (Style)Application.Current.Resources["ArcadeSelectedButton"] };
        save.Click += (_, _) =>
        {
            Channels = normalize(_box.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            DialogResult = true;
        };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => _box.Focus();
    }
}
