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

    /// <summary>Tudo desenhado pela CPU: o processo da GPU fica com ~20 MB em vez de 450+ MB que crescem com as horas.</summary>
    private static readonly string[] NoGpuArguments =
    [
        "--disable-gpu",
        "--disable-gpu-compositing",
    ];

    private BrowserHost(CoreWebView2Environment environment, bool throttlingDisabled, bool extensionsEnabled, bool gpuEnabled)
    {
        Environment = environment;
        ThrottlingDisabled = throttlingDisabled;
        ExtensionsEnabled = extensionsEnabled;
        GpuEnabled = gpuEnabled;
    }

    public CoreWebView2Environment Environment { get; }

    /// <summary>Valores com que o ambiente foi criado; mudá-los exige reiniciar o app.</summary>
    public bool ThrottlingDisabled { get; }
    public bool ExtensionsEnabled { get; }
    public bool GpuEnabled { get; }

    public static async Task<BrowserHost> CreateAsync(AppSettings settings)
    {
        var args = settings.DisableBackgroundThrottling ? BaseArguments.Concat(NoThrottlingArguments) : BaseArguments;
        if (!settings.UseGpu) args = args.Concat(NoGpuArguments);
        var options = new CoreWebView2EnvironmentOptions(string.Join(' ', args))
        {
            AreBrowserExtensionsEnabled = settings.ExtensionsEnabled,
        };
        var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewData, options);
        return new BrowserHost(env, settings.DisableBackgroundThrottling, settings.ExtensionsEnabled, settings.UseGpu);
    }
}
