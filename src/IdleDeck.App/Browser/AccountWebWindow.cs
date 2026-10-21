using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace IdleDeck.App.Browser;

/// <summary>
/// Janela com uma página aberta no perfil de navegador de UMA conta (votar no TopIdle, autorizar a Twitch…).
/// Como usa o perfil da conta, os logins dela (Google, Twitch…) valem na página, e nada se mistura com as outras contas.
/// </summary>
public sealed class AccountWebWindow : Window
{
    private readonly WebView2 _web = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1B, 0x10, 0x30) };

    private AccountWebWindow(Window owner, string title)
    {
        Owner = owner;
        Title = title;
        Width = 1000;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)Application.Current.Resources["ArcadeWindow"];
        Content = _web;
        Closed += (_, _) =>
        {
            try { _web.Dispose(); }
            catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { App.Log(e); }
        };
    }

    /// <summary>
    /// Abre a janela e navega até <paramref name="url"/>. <paramref name="script"/> roda em toda página;
    /// <paramref name="onMessage"/> recebe o que ela enviar com <c>chrome.webview.postMessage</c>.
    /// Devolve null se o navegador não subiu.
    /// </summary>
    public static async Task<AccountWebWindow?> OpenAsync(Window owner, BrowserHost host, AccountView view,
        string title, string url, string? script = null, Action<string>? onMessage = null)
    {
        var window = new AccountWebWindow(owner, $"{title} · {view.Config.Name}");
        window.Show();
        try
        {
            var options = host.Environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = view.Config.ProfileName;
            await window._web.EnsureCoreWebView2Async(host.Environment, options);

            var core = window._web.CoreWebView2;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            if (script is not null) await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
            core.WebMessageReceived += (_, e) =>
            {
                string message;
                try { message = e.TryGetWebMessageAsString(); }
                catch (ArgumentException) { return; }
                onMessage?.Invoke(message);
            };
            // "Entrar com Google/Twitch": o popup abre no mesmo perfil da conta (nunca no navegador do sistema)
            core.NewWindowRequested += (_, e) =>
                _ = PopupWindow.OpenAsync(window, host, view, e, (_, _) => { }, _ => { });
            core.WindowCloseRequested += (_, _) => window.Close();
            core.Navigate(url);
            return window;
        }
        catch (Exception e)
        {
            App.Log(e);
            window.Close();
            return null;
        }
    }

    /// <summary>Lê um script embutido no app (ver <c>LogicalName</c> no csproj).</summary>
    public static string LoadScript(string resourceName)
    {
        using var stream = typeof(AccountWebWindow).Assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Script {resourceName} não embutido.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
