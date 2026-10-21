using System.Windows;
using System.Windows.Input;
using IdleDeck.Core.Vault;

namespace IdleDeck.App;

/// <summary>Pede a senha mestra até acertar ou o usuário pular.</summary>
public partial class UnlockWindow : Window
{
    private readonly VaultStore _vault;

    private UnlockWindow(VaultStore vault, string? hint)
    {
        _vault = vault;
        InitializeComponent();
        if (hint is not null) Hint.Text = hint;
        Loaded += (_, _) => Password.Focus();
    }

    public Dictionary<string, Credential>? Credentials { get; private set; }
    public string? MasterPassword { get; private set; }

    /// <summary>Mostra a janela; devolve true se destravou.</summary>
    public static bool Prompt(Window? owner, VaultStore vault, out Dictionary<string, Credential>? credentials,
        out string? masterPassword, string? hint = null)
    {
        var window = new UnlockWindow(vault, hint);
        if (owner is { IsVisible: true }) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var ok = window.ShowDialog() == true;
        credentials = window.Credentials;
        masterPassword = window.MasterPassword;
        return ok;
    }

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Credentials = _vault.Unlock(Password.Password);
            MasterPassword = Password.Password;
            DialogResult = true;
        }
        catch (VaultUnlockException)
        {
            ErrorText.Visibility = Visibility.Visible;
            Password.SelectAll();
            Password.Focus();
        }
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Password_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Unlock_Click(sender, e);
    }
}
