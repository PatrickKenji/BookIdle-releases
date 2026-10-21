using IdleDeck.Core;
using IdleDeck.Core.Settings;
using Microsoft.Web.WebView2.Core;

namespace IdleDeck.App.Browser;

/// <summary>
/// O único ambiente WebView2 do app. Todas as contas são perfis dentro dele, então
/// compartilham o processo do navegador, a GPU e a rede.
/// </summary>
public sealed class BrowserHost
{
    private static readonly string[] BaseArguments =
    [
        "--disable-features=Translate,AutofillServerCommunication,SpellcheckService,msSmartScreenProtection",
    ];

    private static readonly string[] NoThrottlingArguments =
    [
        "--disable-background-timer-throttling",
        "--disable-renderer-backgrounding",
        "--disable-backgrounding-occluded-windows",
    ];

    private BrowserHost(CoreWebView2Environment environment, bool throttlingDisabled, bool extensionsEnabled)
    {
        Environment = environment;
        ThrottlingDisabled = throttlingDisabled;
        ExtensionsEnabled = extensionsEnabled;
    }

    public CoreWebView2Environment Environment { get; }

    /// <summary>Valores com que o ambiente foi criado; mudá-los exige reiniciar o app.</summary>
    public bool ThrottlingDisabled { get; }
    public bool ExtensionsEnabled { get; }

    public static async Task<BrowserHost> CreateAsync(AppSettings settings)
    {
        var args = settings.DisableBackgroundThrottling ? BaseArguments.Concat(NoThrottlingArguments) : BaseArguments;
        var options = new CoreWebView2EnvironmentOptions(string.Join(' ', args))
        {
            AreBrowserExtensionsEnabled = settings.ExtensionsEnabled,
        };
        var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewData, options);
        return new BrowserHost(env, settings.DisableBackgroundThrottling, settings.ExtensionsEnabled);
    }
}
