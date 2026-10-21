using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using IdleDeck.Core.Settings;
using IdleDeck.Core.Vault;

namespace IdleDeck.App;

/// <summary>
/// Edita, por conta, o método de login (Senha / Google / Nenhum) e as credenciais; e a senha mestra.
/// Só abre com o cofre destravado. Para Google guarda só o e-mail: <c>Credential(email, "")</c>.
/// </summary>
public partial class VaultWindow : Window
{
    private readonly VaultStore _vault;
    private readonly List<Row> _rows = [];

    private sealed class Row
    {
        public required GameProfile Profile;
        public required AccountConfig Account;
        public required RadioButton Password, Google, None;
        public required StackPanel PasswordFields, GoogleFields;
        public required TextBox User, Email;
        public required PasswordBox Pass;

        public LoginMethod Method =>
            Password.IsChecked == true ? LoginMethod.Password : Google.IsChecked == true ? LoginMethod.Google : LoginMethod.None;

        public void ShowFields()
        {
            PasswordFields.Visibility = Method == LoginMethod.Password ? Visibility.Visible : Visibility.Collapsed;
            GoogleFields.Visibility = Method == LoginMethod.Google ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public VaultWindow(VaultStore vault, AppSettings settings, Dictionary<string, Credential> credentials, string? masterPassword)
    {
        _vault = vault;
        InitializeComponent();

        foreach (var profile in settings.Profiles)
        {
            ProfilesPanel.Children.Add(new TextBlock
            {
                Text = profile.Name.ToUpperInvariant(),
                Style = (Style)FindResource("SectionTitle"),
            });
            if (profile.Accounts.Count == 0)
                ProfilesPanel.Children.Add(new TextBlock { Text = Loc.T("Nenhuma conta."), Style = (Style)FindResource("MutedText") });
            foreach (var account in profile.Accounts)
                AddRow(profile, account, credentials.GetValueOrDefault(account.ProfileName));
        }

        UseMaster.IsChecked = !string.IsNullOrEmpty(masterPassword);
        Master.Password = MasterConfirm.Password = masterPassword ?? "";
        UseMaster_Changed(this, new RoutedEventArgs());
    }

    public Dictionary<string, Credential>? SavedCredentials { get; private set; }
    public string? SavedMasterPassword { get; private set; }
    /// <summary>Método escolhido por conta (ProfileName → método). O chamador grava nas configurações.</summary>
    public Dictionary<string, LoginMethod> SavedMethods { get; } = [];
    public bool Deleted { get; private set; }

    private void AddRow(GameProfile profile, AccountConfig account, Credential? credential)
    {
        var group = "login-" + account.ProfileName;
        RadioButton Choice(string text, LoginMethod method) => new()
        {
            Content = text,
            GroupName = group,
            Style = (Style)FindResource("ArcadeChoice"),
            IsChecked = account.Login == method,
        };

        var user = new TextBox { Margin = new Thickness(0, 0, 6, 0), Width = 170 };
        var pass = new PasswordBox { Width = 150 };
        var email = new TextBox { Width = 326 };
        var row = new Row
        {
            Profile = profile,
            Account = account,
            Password = Choice(Loc.T("Senha"), LoginMethod.Password),
            Google = Choice("Google", LoginMethod.Google),
            None = Choice(Loc.T("Nenhum"), LoginMethod.None),
            User = user,
            Pass = pass,
            Email = email,
            PasswordFields = Labeled((Loc.T("Usuário / e-mail"), user), (Loc.T("Senha"), pass)),
            GoogleFields = Labeled((Loc.T("E-mail da conta Google"), email)),
        };

        if (credential is not null)
        {
            if (account.Login == LoginMethod.Google) email.Text = credential.Username;
            else
            {
                user.Text = credential.Username;
                pass.Password = credential.Password;
            }
        }

        foreach (var choice in new[] { row.Password, row.Google, row.None })
            choice.Checked += (_, _) => row.ShowFields();
        row.ShowFields();

        var choices = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
        choices.Children.Add(row.Password);
        choices.Children.Add(row.Google);
        choices.Children.Add(row.None);

        var fields = new Grid();
        fields.Children.Add(row.PasswordFields);
        fields.Children.Add(row.GoogleFields);

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = account.Name, FontWeight = FontWeights.Bold, FontSize = 13 });
        panel.Children.Add(choices);
        panel.Children.Add(fields);

        ProfilesPanel.Children.Add(new Border
        {
            Style = (Style)FindResource("ArcadeCard"),
            Padding = new Thickness(12, 10, 12, 12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = panel,
        });
        _rows.Add(row);
    }

    private StackPanel Labeled(params (string Label, Control Field)[] items)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, field) in items)
        {
            var column = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            column.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 0, 0, 3) });
            field.Margin = new Thickness(0);
            column.Children.Add(field);
            panel.Children.Add(column);
        }
        return panel;
    }

    private void UseMaster_Changed(object sender, RoutedEventArgs e) =>
        MasterPanel.IsEnabled = UseMaster.IsChecked == true;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string? master = null;
        if (UseMaster.IsChecked == true)
        {
            if (Master.Password.Length == 0) { ShowError(Loc.T("Digite a senha mestra.")); return; }
            if (Master.Password != MasterConfirm.Password) { ShowError(Loc.T("As senhas mestras não conferem.")); return; }
            master = Master.Password;
        }

        var credentials = new Dictionary<string, Credential>();
        foreach (var row in _rows)
        {
            switch (row.Method)
            {
                case LoginMethod.Password when row.User.Text.Trim().Length > 0 && row.Pass.Password.Length > 0:
                    credentials[row.Account.ProfileName] = new Credential(row.User.Text.Trim(), row.Pass.Password);
                    break;
                case LoginMethod.Password:
                    ShowError(Loc.T("{0}: preencha usuário e senha, ou escolha Nenhum.", row.Account.Name));
                    return;
                case LoginMethod.Google when row.Email.Text.Trim().Contains('@'):
                    credentials[row.Account.ProfileName] = new Credential(row.Email.Text.Trim(), "");
                    break;
                case LoginMethod.Google:
                    ShowError(Loc.T("{0}: digite o e-mail da conta Google.", row.Account.Name));
                    return;
            }
            SavedMethods[row.Account.ProfileName] = row.Method;
        }

        try
        {
            _vault.Save(credentials, master);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            ShowError(Loc.T("Não foi possível salvar o cofre: {0}", ex.Message));
            return;
        }

        SavedCredentials = credentials;
        SavedMasterPassword = master;
        DialogResult = true;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!ArcadeDialog.Show(this, Loc.T("Apagar cofre"), Loc.T("Apagar todas as credenciais salvas? Isso não pode ser desfeito."),
                ArcadeDialogButtons.YesNo, ArcadeDialogKind.Warning)) return;

        try
        {
            _vault.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(Loc.T("Não foi possível apagar o cofre: {0}", ex.Message));
            return;
        }
        Deleted = true;
        SavedCredentials = [];
        SavedMasterPassword = null;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
