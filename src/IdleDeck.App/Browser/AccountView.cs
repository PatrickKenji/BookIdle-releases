using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IdleDeck.Core.Modules;
using IdleDeck.Core.Net;
using IdleDeck.Core.Settings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IdleDeck.App.Browser;

public enum AccountStatus { Loading, Online, Offline, Reloading, Error, LoginError, NeedsUser }

/// <summary>
/// Uma conta: cabeçalho clicável (só nos layouts com várias contas visíveis) + WebView2 no perfil da conta.
/// O controle nunca sai do Grid central; o layout só muda posição e visibilidade.
/// </summary>
public sealed class AccountView : DockPanel
{
    private readonly Border _header;
    private readonly List<string> _startupScripts = [];
    private WebView2 _web = NewWebView();
    private bool _active = true;
    private double _zoom;
    private readonly TextBlock _headerText;
    private readonly Button _muteButton;
    private readonly Button _autoCatchButton;
    /// <summary>Script que deixa a chave da captura automática ligada/desligada em cada documento novo desta conta.</summary>
    private string? _autoCatchScriptId;
    /// <summary>Área do jogo: o WebView2 e, por cima dele só durante o arrasto, a "foto" (cobertura).</summary>
    private readonly Grid _body = new();
    private readonly Border _cover;
    private readonly Image _coverImage = new() { Stretch = Stretch.Fill };
    private readonly TextBlock _coverLabel;
    private bool _dragMode;
    /// <summary>Conta as entradas no modo de arrasto: uma saída durante a foto invalida a entrada que ainda esperava.</summary>
    private int _dragGeneration;
    /// <summary>Modo foto: a conta fora de foco mostra uma foto atualizada pelo timer, com a página escondida por trás.</summary>
    private bool _photoMode;
    private bool _photoBusy;
    private System.Windows.Threading.DispatcherTimer? _photoTimer;
    /// <summary>Página escondida não desenha (a foto sai preta): ao acordar, espera ela pintar antes de fotografar.</summary>
    private static readonly TimeSpan PhotoWakeDelay = TimeSpan.FromMilliseconds(700);
    private AccountStatus _status = AccountStatus.Loading;

    public AccountView(int runtimeId, GameProfile profile, AccountConfig config)
    {
        RuntimeId = runtimeId;
        Profile = profile;
        Config = config;
        _zoom = profile.Zoom;
        AttachWeb();

        _headerText = new TextBlock { FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        var expand = HeaderButton("⛶", Loc.T("Expandir: abre esta conta nas Abas"));
        expand.Click += (_, _) => ExpandRequested?.Invoke(this, EventArgs.Empty);
        _muteButton = HeaderButton("🔊", Loc.T("Mutar esta conta"));
        _muteButton.Click += (_, _) => MuteToggleRequested?.Invoke(this, EventArgs.Empty);
        _autoCatchButton = HeaderButton("🎯", "");
        _autoCatchButton.Click += (_, _) => AutoCatchToggleRequested?.Invoke(this, EventArgs.Empty);
        _autoCatchButton.Visibility = IsPokeIdle ? Visibility.Visible : Visibility.Collapsed;
        var bar = new DockPanel { LastChildFill = true };
        foreach (var button in new[] { expand, _muteButton, _autoCatchButton })
        {
            SetDock(button, Dock.Right);
            bar.Children.Add(button);
        }
        bar.Children.Add(_headerText);
        _header = new Border
        {
            Height = 24,
            Padding = new Thickness(10, 0, 4, 0),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Background = (Brush)Application.Current.Resources["Card"],
            Cursor = Cursors.Hand,
            Child = bar,
            ToolTip = Loc.T("Clique para dar destaque a esta conta"),
        };
        _header.MouseLeftButtonUp += (_, _) => HeaderClicked?.Invoke(this, EventArgs.Empty);
        SetDock(_header, Dock.Top);
        Children.Add(_header);

        _coverLabel = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["Muted"],
            Visibility = Visibility.Collapsed,
        };
        _cover = new Border
        {
            Background = (Brush)Application.Current.Resources["Card"],
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(3),
            Visibility = Visibility.Collapsed,
            Child = new Grid { Children = { _coverImage, _coverLabel } },
        };
        _cover.MouseLeftButtonUp += (_, _) =>
        {
            if (_photoMode && !_dragMode) HeaderClicked?.Invoke(this, EventArgs.Empty);
        };
        _body.Children.Add(_web);
        _body.Children.Add(_cover);
        Children.Add(_body);
        UpdateHeader();
    }

    /// <summary>A barrinha de título (alça de arrasto).</summary>
    public UIElement HeaderElement => _header;

    /// <summary>Destaque da célula sobre a qual a conta arrastada vai cair.</summary>
    public bool IsDropTarget
    {
        set => _cover.BorderBrush = value ? (Brush)Application.Current.Resources["Yellow"] : Brushes.Transparent;
    }

    /// <summary>A conta que está sendo arrastada fica meio apagada.</summary>
    public bool IsDragSource
    {
        set => _cover.Opacity = value ? 0.55 : 1;
    }

    /// <summary>
    /// Troca o WebView2 por uma foto dele (o controle nativo desenha por cima de tudo). O jogo continua
    /// rodando escondido; se a foto falhar, a cobertura mostra só o nome da conta.
    /// </summary>
    public async Task EnterDragModeAsync()
    {
        if (_dragMode) return;
        _dragMode = true;
        var generation = ++_dragGeneration;
        // No modo foto a página está escondida (sairia preta): a foto que já está na tela serve.
        var picture = _photoMode && _web.Visibility != Visibility.Visible ? _coverImage.Source as BitmapSource : await CaptureAsync();
        if (generation != _dragGeneration) return; // cancelou (Esc, mouse solto) enquanto a foto era tirada: nada a mostrar
        _coverImage.Source = picture;
        _coverLabel.Text = Config.Name;
        _coverLabel.Visibility = picture is null ? Visibility.Visible : Visibility.Collapsed;
        _cover.Visibility = Visibility.Visible;
        _web.Visibility = Visibility.Collapsed;
    }

    /// <summary>Volta o WebView2 no lugar da foto e reencaixa o tamanho (a página não recarrega).</summary>
    public void ExitDragMode()
    {
        if (!_dragMode) return;
        _dragMode = false;
        _dragGeneration++;
        if (_photoMode && _coverImage.Source is not null) return; // continua como foto; o timer atualiza
        ShowLive();
    }

    /// <summary>Foto da página como está na tela agora; null se não deu (página escondida ou sem navegador).</summary>
    private async Task<BitmapSource?> CaptureAsync()
    {
        if (_web.CoreWebView2 is not { } core || _web.ActualWidth <= 1) return null;
        try
        {
            using var stream = new MemoryStream();
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg, stream);
            stream.Position = 0;
            var picture = new BitmapImage();
            picture.BeginInit();
            picture.CacheOption = BitmapCacheOption.OnLoad;
            picture.StreamSource = stream;
            picture.EndInit();
            picture.Freeze();
            return picture;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            App.Log(e);
            return null;
        }
    }

    private void ShowLive()
    {
        _web.Visibility = Visibility.Visible;
        _cover.Visibility = Visibility.Collapsed;
        _coverImage.Source = null;
        RealinharTela();
    }

    /// <summary>
    /// Liga o modo foto (ou só troca o intervalo): a página fica escondida, gastando bem menos RAM, GPU e CPU,
    /// e a cada <paramref name="every"/> acorda por um instante para tirar uma foto nova. O jogo segue rodando.
    /// </summary>
    public void StartPhotoMode(TimeSpan every)
    {
        if (_photoTimer is { } timer)
        {
            if (timer.Interval != every) timer.Interval = every;
            return;
        }
        _photoMode = true;
        _cover.Cursor = Cursors.Hand;
        _photoTimer = new System.Windows.Threading.DispatcherTimer { Interval = every };
        _photoTimer.Tick += async (_, _) => await TakePhotoAsync();
        _photoTimer.Start();
        _ = TakePhotoAsync();
    }

    /// <summary>Volta a conta ao vivo.</summary>
    public void StopPhotoMode()
    {
        if (!_photoMode) return;
        _photoMode = false;
        _photoTimer?.Stop();
        _photoTimer = null;
        _cover.Cursor = null;
        if (!_dragMode) ShowLive(); // no arrasto, a saída dele devolve a página
    }

    private async Task TakePhotoAsync()
    {
        if (!_photoMode || _photoBusy || _dragMode) return;
        _photoBusy = true;
        try
        {
            if (_web.Visibility != Visibility.Visible)
            {
                _web.Visibility = Visibility.Visible; // a página desenha por cima da foto antiga (o WebView2 fica acima do WPF)
                await Task.Delay(PhotoWakeDelay);
            }
            if (!_photoMode || _dragMode) return;
            // Sem foto (navegador recriando, página carregando), a conta fica ao vivo até a próxima tentativa.
            if (await CaptureAsync() is not { } picture || !_photoMode || _dragMode) return;
            _coverImage.Source = picture;
            _coverLabel.Visibility = Visibility.Collapsed;
            _cover.Visibility = Visibility.Visible;
            _web.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _photoBusy = false;
        }
    }

    /// <summary>Identificador desta conta enquanto o app está aberto (políticas, login e módulos usam ele).</summary>
    public int RuntimeId { get; }
    public GameProfile Profile { get; }
    public AccountConfig Config { get; private set; }

    /// <summary>A conta é do PokeIdle (só lá existem o voto e a captura automática).</summary>
    public bool IsPokeIdle =>
        Uri.TryCreate(Profile.Url, UriKind.Absolute, out var uri) && ModuleRouter.HostMatches(uri.Host, "pokeidle.io");

    /// <summary>Endereço inicial da conta: a URL do perfil de jogo.</summary>
    public string Url => Profile.Url;
    public CoreWebView2? Core => _web.CoreWebView2;

    public AccountStatus Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? StatusChanged;
    public event EventHandler? HeaderClicked;
    /// <summary>O botão ⛶ da barrinha: o app abre esta conta nas Abas.</summary>
    public event EventHandler? ExpandRequested;
    /// <summary>O botão de mudo da barrinha.</summary>
    public event EventHandler? MuteToggleRequested;
    /// <summary>O botão 🎯 da barrinha (captura automática).</summary>
    public event EventHandler? AutoCatchToggleRequested;
    /// <summary>O usuário mudou o zoom dentro da página (Ctrl+scroll, Ctrl+−/+).</summary>
    public event EventHandler? ZoomChanged;

    public double Zoom => _zoom;

    public void SetZoom(double zoom)
    {
        _zoom = zoom;
        _web.ZoomFactor = zoom;
    }

    private void AttachWeb()
    {
        _web.ZoomFactor = _zoom;
        _web.ZoomFactorChanged += (_, _) =>
        {
            if (Math.Abs(_web.ZoomFactor - _zoom) < 0.005) return; // fomos nós que mudamos
            _zoom = _web.ZoomFactor;
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        };
    }
    /// <summary>Mensagem (string JSON) enviada pela página com <c>chrome.webview.postMessage</c>.</summary>
    public event EventHandler<string>? WebMessage;
    /// <summary>A página quer abrir um popup (ex.: login do Google). Quem tratar precisa usar o deferral.</summary>
    public event EventHandler<CoreWebView2NewWindowRequestedEventArgs>? NewWindowRequested;

    public bool ShowHeader
    {
        set => _header.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool IsHighlighted
    {
        set => _header.Background = (Brush)Application.Current.Resources[value ? "Purple" : "Card"];
    }

    public void UpdateConfig(AccountConfig config)
    {
        var autoCatchChanged = config.AutoCatch != Config.AutoCatch;
        Config = config;
        UpdateHeader();
        if (_web.CoreWebView2 is { } core)
        {
            core.IsMuted = config.Muted;
            if (autoCatchChanged) _ = ApplyAutoCatchAsync(core);
        }
    }

    /// <summary>
    /// Liga/desliga a captura automática: troca o script que vale para as próximas páginas e muda a chave na
    /// página aberta agora (o laço do auto-catch.js lê a chave a cada segundo; não precisa recarregar).
    /// </summary>
    private async Task ApplyAutoCatchAsync(CoreWebView2 core)
    {
        var script = $"window.__bookidleAutoCatch = {(Config.AutoCatch ? "true" : "false")};";
        try
        {
            if (_autoCatchScriptId is { } old) core.RemoveScriptToExecuteOnDocumentCreated(old);
            _autoCatchScriptId = null;
            _autoCatchScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
            await core.ExecuteScriptAsync(script);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { App.Log(e); }
    }

    public bool IsMuted => Config.Muted;

    private void UpdateHeader()
    {
        _headerText.Text = Config.Name;
        _muteButton.Content = Config.Muted ? "🔇" : "🔊";
        _muteButton.ToolTip = Config.Muted ? Loc.T("Tirar o mudo desta conta") : Loc.T("Mutar esta conta");
        _autoCatchButton.Opacity = Config.AutoCatch ? 1 : 0.4;
        _autoCatchButton.ToolTip = Config.AutoCatch
            ? Loc.T("Captura automática ligada: clique para desligar")
            : Loc.T("Captura automática desligada: clique para ligar (para quem não tem o VIP)");
    }

    /// <summary>Botão da barrinha: pequeno e sem moldura; o clique não vira "dar destaque".</summary>
    private static Button HeaderButton(string glyph, string tooltip) => new()
    {
        Content = glyph,
        ToolTip = tooltip,
        Style = (Style)Application.Current.Resources["IconButton"],
        FontSize = 12,
        Padding = new Thickness(6, 0, 6, 0),
        Margin = new Thickness(0),
        Cursor = Cursors.Hand,
        Focusable = false,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static WebView2 NewWebView() =>
        new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x15, 0x17, 0x1C) };

    /// <summary>
    /// Troca o WebView2 por um novo (usado quando o processo do navegador morreu).
    /// Depois, chamar <see cref="InitializeAsync"/> e <see cref="Start"/> de novo.
    /// </summary>
    public void Recreate()
    {
        // Esconde e tira da árvore ANTES de descartar: WebView2 visível pode deixar a tela antiga
        // "fantasma" desenhada por cima da nova (o host de HWND não é composto pelo WPF).
        _web.Visibility = Visibility.Collapsed;
        _body.Children.Remove(_web);
        try { _web.Dispose(); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { App.Log(e); }
        _web = NewWebView();
        _autoCatchScriptId = null; // o script era do navegador antigo
        AttachWeb();
        _body.Children.Insert(0, _web);
        Status = AccountStatus.Loading;
    }

    /// <summary>
    /// Depois de maximizar/restaurar (ou de recriar o navegador) o WebView2 às vezes fica com o tamanho
    /// antigo: sobra faixa vazia na célula ou o conteúdo aparece deslocado. Um empurrão de meio pixel
    /// faz o controle recalcular as dimensões, sem recarregar a página nem perder a sessão.
    /// </summary>
    public void RealinharTela()
    {
        Dispatcher.InvokeAsync(() =>
        {
            var largura = _web.ActualWidth > 0 ? _web.ActualWidth : Width;
            if (double.IsNaN(largura) || largura <= 1) return;
            _web.Width = largura - 0.5;
            Dispatcher.InvokeAsync(() =>
            {
                _web.Width = double.NaN; // volta a esticar na célula
                _web.Height = double.NaN;
                UpdateLayout();          // força a nova medição na hora
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Apaga cookies/login desta conta (o perfil do navegador é removido quando o controle fecha)
    /// e descarta o WebView2. A conta não pode ser usada depois disso.
    /// </summary>
    public async Task DeleteBrowserDataAsync()
    {
        await Task.Yield();
        try { _web.CoreWebView2?.Profile.Delete(); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { App.Log(e); }
        _body.Children.Remove(_web);
        _web.Dispose();
    }

    /// <summary>Cria o WebView2 no perfil da conta, sem navegar ainda. Reaplica os scripts já registrados.</summary>
    public async Task InitializeAsync(BrowserHost host, DomainBlocker blocker)
    {
        var options = host.Environment.CreateCoreWebView2ControllerOptions();
        options.ProfileName = Config.ProfileName;
        await _web.EnsureCoreWebView2Async(host.Environment, options);

        var core = _web.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.IsMuted = Config.Muted;

        // filtro só para os domínios bloqueados: o resto do tráfego do jogo não passa pelo app
        foreach (var domain in blocker.Domains)
        {
            core.AddWebResourceRequestedFilter($"*://{domain}/*", CoreWebView2WebResourceContext.All);
            core.AddWebResourceRequestedFilter($"*://*.{domain}/*", CoreWebView2WebResourceContext.All);
        }
        core.WebResourceRequested += (_, e) =>
        {
            if (Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) && blocker.IsBlocked(uri))
                e.Response = host.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
        };

        core.WebMessageReceived += (_, e) =>
        {
            string message;
            try { message = e.TryGetWebMessageAsString(); }
            catch (ArgumentException) { message = e.WebMessageAsJson; }
            WebMessage?.Invoke(this, message);
        };

        core.NewWindowRequested += (_, e) => NewWindowRequested?.Invoke(this, e);
        core.NavigationStarting += (_, _) =>
        {
            if (Status is not (AccountStatus.Reloading or AccountStatus.LoginError or AccountStatus.NeedsUser))
                Status = AccountStatus.Loading;
        };
        core.NavigationCompleted += (_, e) =>
        {
            ApplyMemoryLevel();
            if (Status is not (AccountStatus.LoginError or AccountStatus.NeedsUser) || !e.IsSuccess)
                Status = e.IsSuccess ? AccountStatus.Online : AccountStatus.Offline;
        };

        foreach (var script in _startupScripts)
            await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
        await ApplyAutoCatchAsync(core);
    }

    /// <summary>Script injetado em todo documento desta conta (sobrevive a recargas e recriações).</summary>
    public async Task AddStartupScriptAsync(string script)
    {
        _startupScripts.Add(script);
        if (_web.CoreWebView2 is { } core)
            await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
    }

    public void Start() => _web.CoreWebView2?.Navigate(Url);

    /// <summary>Recarrega a página da conta. Nunca apaga cookies/login.</summary>
    public void Reload()
    {
        var core = _web.CoreWebView2;
        if (core is null) return;
        Status = AccountStatus.Reloading;
        core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
        if (string.IsNullOrEmpty(core.Source) || core.Source == "about:blank") core.Navigate(Url);
        else core.Reload();
    }

    /// <summary>
    /// Recarrega a conta num processo novo para devolver a memória ao Windows: o <see cref="Reload"/> reaproveita
    /// o mesmo processo do renderizador, e a RAM que ele já pegou fica com ele. Encerra o renderizador desta conta;
    /// o <see cref="Health.KeepAliveService"/> vê a queda e recarrega a página. Nunca apaga cookies/login.
    /// </summary>
    public async Task RestartAsync()
    {
        if (_web.CoreWebView2 is not { } core) return;
        int? pid = null;
        try { pid = await OwnRendererAsync(core); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { App.Log(e); }
        if (pid is null || _web.CoreWebView2 != core)
        {
            Reload();
            return;
        }
        Status = AccountStatus.Reloading;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid.Value);
            process.Kill();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            App.Log(e);
            Reload();
        }
    }

    /// <summary>Processo do renderizador da página principal, só se nenhuma outra conta usar o mesmo processo.</summary>
    private static async Task<int?> OwnRendererAsync(CoreWebView2 core)
    {
        var main = (ulong)core.FrameId;
        foreach (var info in await core.Environment.GetProcessExtendedInfosAsync())
        {
            if (info.ProcessInfo.Kind != CoreWebView2ProcessKind.Renderer) continue;
            if (!info.AssociatedFrameInfos.Any(f => f.FrameId == main)) continue;
            return Health.MemoryMonitor.RootFrames(info).All(root => root == main) ? info.ProcessInfo.ProcessId : null;
        }
        return null;
    }

    /// <summary>Contas fora da tela pedem menos memória ao navegador, mas continuam rodando.</summary>
    public void SetActive(bool visible)
    {
        _active = visible;
        ApplyMemoryLevel();
    }

    private void ApplyMemoryLevel()
    {
        if (_web.CoreWebView2 is { } core)
            core.MemoryUsageTargetLevel = _active ? CoreWebView2MemoryUsageTargetLevel.Normal : CoreWebView2MemoryUsageTargetLevel.Low;
    }
}
