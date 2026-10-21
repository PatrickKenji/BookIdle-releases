using System.Windows;
using System.Windows.Controls;
using IdleDeck.Core.Settings;

namespace IdleDeck.App;

/// <summary>Edita as configurações. Ao salvar, altera o <see cref="AppSettings"/> recebido.</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        AutoReset.IsChecked = settings.AutoResetEnabled;
        MemoryLimit.Text = settings.MemoryLimitMb.ToString();
        NoThrottling.IsChecked = settings.DisableBackgroundThrottling;
        Extensions.IsChecked = settings.ExtensionsEnabled;
        BlockedDomains.Text = string.Join(Environment.NewLine, settings.BlockedDomains);
        StartWithWindows.IsChecked = settings.StartWithWindows;
    }

    /// <summary>True se mudou algo que só vale depois de reiniciar o navegador.</summary>
    public bool RestartRequired { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MemoryLimit.Text.Trim(), out var limit) || limit < 100)
        {
            ShowError("Limite de memória: informe um número a partir de 100 MB.");
            return;
        }

        var blocked = BlockedDomains.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        RestartRequired = _settings.DisableBackgroundThrottling != (NoThrottling.IsChecked == true)
                          || _settings.ExtensionsEnabled != (Extensions.IsChecked == true)
                          || !_settings.BlockedDomains.SequenceEqual(blocked);

        try
        {
            StartupRegistration.Set(StartWithWindows.IsChecked == true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            ShowError($"Não foi possível alterar o início com o Windows: {ex.Message}");
            return;
        }

        _settings.AutoResetEnabled = AutoReset.IsChecked == true;
        _settings.MemoryLimitMb = limit;
        _settings.DisableBackgroundThrottling = NoThrottling.IsChecked == true;
        _settings.ExtensionsEnabled = Extensions.IsChecked == true;
        _settings.BlockedDomains = blocked;
        _settings.StartWithWindows = StartWithWindows.IsChecked == true;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
