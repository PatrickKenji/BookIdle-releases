using System.Windows;
using IdleDeck.Core.Settings;

namespace IdleDeck.App;

/// <summary>Cria ou edita um perfil de jogo (nome, URL, limite de contas).</summary>
public partial class ProfileWindow : Window
{
    public ProfileWindow(GameProfile? existing = null)
    {
        InitializeComponent();
        Title = existing is null ? Loc.T("Novo perfil de jogo") : Loc.T("Editar perfil");
        if (existing is not null)
        {
            Heading.Text = Loc.T("EDITAR PERFIL");
            NameBox.Text = existing.Name;
            UrlBox.Text = existing.Url;
            LimitBox.Text = existing.MaxAccounts.ToString();
        }
        Loaded += (_, _) => NameBox.Focus();
    }

    public string ProfileName { get; private set; } = "";
    public string Url { get; private set; } = "";
    public int MaxAccounts { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var url = UrlBox.Text.Trim();
        if (name.Length == 0) { ShowError(Loc.T("Dê um nome ao jogo.")); return; }
        if (!AppSettings.IsHttpUrl(url)) { ShowError(Loc.T("Endereço inválido. Use algo como https://pokeidle.io")); return; }
        if (!int.TryParse(LimitBox.Text.Trim(), out var limit) || limit < 1 || limit > GameProfile.MaxAccountsLimit)
        {
            ShowError(Loc.T("O limite de contas vai de 1 a {0}.", GameProfile.MaxAccountsLimit));
            return;
        }

        ProfileName = name;
        Url = url;
        MaxAccounts = limit;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
