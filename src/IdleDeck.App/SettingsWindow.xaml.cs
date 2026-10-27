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
        PhotoMode.IsChecked = settings.PhotoMode;
        PhotoEvery.ItemsSource = AppSettings.PhotoIntervals;
        PhotoEvery.SelectedItem = settings.PhotoEverySeconds;
        NoThrottling.IsChecked = settings.DisableBackgroundThrottling;
        UseGpu.IsChecked = settings.UseGpu;
        Extensions.IsChecked = settings.ExtensionsEnabled;
        BlockedDomains.Text = string.Join(Environment.NewLine, settings.BlockedDomains);
        StartWithWindows.IsChecked = settings.StartWithWindows;

        LanguageBox.ItemsSource = new[] { (Code: Loc.Auto, Name: Loc.T("Automático (idioma do Windows)")) }
            .Concat(Loc.Languages)
            .Select(l => new LanguageChoice(l.Code, l.Name))
            .ToList();
        LanguageBox.SelectedValue = settings.Language;
    }

    private sealed record LanguageChoice(string Code, string Name);

    /// <summary>True se mudou algo que só vale depois de reiniciar o app (navegador, idioma).</summary>
    public bool RestartRequired { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MemoryLimit.Text.Trim(), out var limit) || limit < 100)
        {
            ShowError(Loc.T("Limite de memória: informe um número a partir de 100 MB."));
            return;
        }

        var blocked = BlockedDomains.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var language = LanguageBox.SelectedValue as string ?? Loc.Auto;
        RestartRequired = _settings.Language != language
                          || _settings.DisableBackgroundThrottling != (NoThrottling.IsChecked == true)
                          || _settings.UseGpu != (UseGpu.IsChecked == true)
                          || _settings.ExtensionsEnabled != (Extensions.IsChecked == true)
                          || !_settings.BlockedDomains.SequenceEqual(blocked);

        try
        {
            StartupRegistration.Set(StartWithWindows.IsChecked == true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            ShowError(Loc.T("Não foi possível alterar o início com o Windows: {0}", ex.Message));
            return;
        }

        _settings.AutoResetEnabled = AutoReset.IsChecked == true;
        _settings.MemoryLimitMb = limit;
        _settings.PhotoMode = PhotoMode.IsChecked == true;
        if (PhotoEvery.SelectedItem is int every) _settings.PhotoEverySeconds = every;
        _settings.DisableBackgroundThrottling = NoThrottling.IsChecked == true;
        _settings.UseGpu = UseGpu.IsChecked == true;
        _settings.ExtensionsEnabled = Extensions.IsChecked == true;
        _settings.BlockedDomains = blocked;
        _settings.StartWithWindows = StartWithWindows.IsChecked == true;
        _settings.Language = language;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
