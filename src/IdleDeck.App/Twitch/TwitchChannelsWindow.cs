using System.Windows;
using System.Windows.Controls;
using IdleDeck.Core.Twitch;

namespace IdleDeck.App.Twitch;

/// <summary>Lista dos canais da Twitch que dão bônus neste jogo (um por linha). Todas as contas do perfil entram neles.</summary>
public sealed class TwitchChannelsWindow : Window
{
    private readonly TextBox _box;

    public List<string> Channels { get; private set; } = [];

    public TwitchChannelsWindow(Window owner, string profileName, IEnumerable<string> current)
    {
        Owner = owner;
        Title = $"Twitch · {profileName}";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)Application.Current.Resources["ArcadeWindow"];

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "BÔNUS DE LIVE (TWITCH)",
            Style = (Style)Application.Current.Resources["SectionTitle"],
            Margin = new Thickness(0, 0, 0, 8),
        });
        var hint = new TextBlock
        {
            Text = "Um canal por linha (pode colar o endereço, ex.: twitch.tv/canal). Cada conta que tiver a Twitch conectada " +
                   "fica presente no chat desses canais em segundo plano — sem abrir vídeo nem chat na tela.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8),
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        panel.Children.Add(hint);

        _box = new TextBox
        {
            AcceptsReturn = true,
            Height = 150,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = string.Join(Environment.NewLine, current),
        };
        panel.Children.Add(_box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(new Button { Content = "Cancelar", IsCancel = true });
        var save = new Button { Content = "Salvar", IsDefault = true, Style = (Style)Application.Current.Resources["ArcadeSelectedButton"] };
        save.Click += (_, _) =>
        {
            Channels = TwitchChannels.Normalize(_box.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            DialogResult = true;
        };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => _box.Focus();
    }
}
