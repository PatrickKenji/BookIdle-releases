using System.Windows;
using IdleDeck.Core.Settings;
using IdleDeck.Core.Vote;

namespace IdleDeck.App;

/// <summary>Cria ou edita um perfil de jogo (nome, URL, limite de contas, site de votação).</summary>
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
            VoteBox.Text = existing.VoteUrl ?? "";
        }
        Loaded += (_, _) => NameBox.Focus();
    }

    public string ProfileName { get; private set; } = "";
    public string Url { get; private set; } = "";
    public int MaxAccounts { get; private set; }

    /// <summary>Página de votação do jogo ("" = sem voto).</summary>
    public string VoteUrl { get; private set; } = "";

    /// <summary>Endereço do PokeIdle com o campo de voto vazio: já preenche o IdleHub.</summary>
    private void UrlBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (VoteBox.Text.Trim().Length == 0) VoteBox.Text = VoteSites.DefaultFor(UrlBox.Text.Trim());
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var url = UrlBox.Text.Trim();
        var vote = VoteBox.Text.Trim();
        if (name.Length == 0) { ShowError(Loc.T("Dê um nome ao jogo.")); return; }
        if (!AppSettings.IsHttpUrl(url)) { ShowError(Loc.T("Endereço inválido. Use algo como https://pokeidle.io")); return; }
        if (!int.TryParse(LimitBox.Text.Trim(), out var limit) || limit < 1 || limit > GameProfile.MaxAccountsLimit)
        {
            ShowError(Loc.T("O limite de contas vai de 1 a {0}.", GameProfile.MaxAccountsLimit));
            return;
        }
        if (vote.Length > 0 && !AppSettings.IsHttpUrl(vote))
        {
            ShowError(Loc.T("Site de votação inválido. Use um endereço https:// ou deixe vazio."));
            return;
        }

        ProfileName = name;
        Url = url;
        MaxAccounts = limit;
        VoteUrl = vote;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
