using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IdleDeck.App.Browser;

/// <summary>
/// Janela para popups das páginas (ex.: "Entrar com Google"), no mesmo perfil de navegador da conta —
/// nunca no navegador do sistema, para não misturar sessões. Fecha quando a página pede.
/// </summary>
public sealed class PopupWindow : Window
{
    private readonly WebView2 _web = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1B, 0x10, 0x30) };

    private PopupWindow(Window owner, string title)
    {
        Owner = owner;
        Title = title;
        Width = 480;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowActivated = false;
        Style = (Style)Application.Current.Resources["ArcadeWindow"];
        Content = _web;
    }

    /// <summary>
    /// Atende <c>NewWindowRequested</c> da conta: abre a janela, cria o WebView2 no mesmo perfil e
    /// entrega a ele a nova página. <paramref name="onMessage"/> recebe as mensagens da página do popup.
    /// </summary>
    public static async Task OpenAsync(Window owner, BrowserHost host, AccountView view,
        CoreWebView2NewWindowRequestedEventArgs e, Action<CoreWebView2, string> onMessage, Action<bool> onOpenChanged)
    {
        var deferral = e.GetDeferral();
        e.Handled = true;
        PopupWindow? popup = null;
        try
        {
            popup = new PopupWindow(owner, view.Config.Name);
            popup.Closed += (_, _) => onOpenChanged(false);
            onOpenChanged(true);
            popup.Show();

            var options = host.Environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = view.Config.ProfileName;
            await popup._web.EnsureCoreWebView2Async(host.Environment, options);

            var core = popup._web.CoreWebView2;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(Login.AutoLoginService.DetectScript);
            core.WebMessageReceived += (_, m) =>
            {
                try { onMessage(core, m.TryGetWebMessageAsString()); }
                catch (ArgumentException) { }
            };
            core.WindowCloseRequested += (_, _) => popup.Close();
            core.DocumentTitleChanged += (_, _) => popup.Title = $"{view.Config.Name} · {core.DocumentTitle}";
            popup.Closed += (_, _) => popup._web.Dispose();

            e.NewWindow = core;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            popup?.Close();
            e.Handled = true; // sem popup: nunca deixar abrir uma janela solta fora do app
        }
        finally
        {
            deferral.Complete();
        }
    }
}
