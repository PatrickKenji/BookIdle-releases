using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IdleDeck.App.Browser;
using IdleDeck.Core.Twitch;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IdleDeck.App.Lives;

/// <summary>O que muda de um site de lives para outro (Twitch, Kick): endereço, ajustes da página e as leituras.</summary>
public interface ILiveSite
{
    /// <summary>Nome do site (título da janela escondida).</summary>
    string Name { get; }

    int PageWidth { get; }
    int PageHeight { get; }

    string PageUrl(string channel);

    /// <summary>Ajustes da página antes de abrir a live (identificação, bloqueios, scripts). Thread da tela.</summary>
    Task SetupPageAsync(BrowserHost host, CoreWebView2 core, Action<string> log);

    /// <summary>Quais canais da lista estão ao vivo agora.</summary>
    Task<IReadOnlySet<string>> ReadLiveAsync(string token, IReadOnlyList<string> channels, Action<string> log, CancellationToken ct);

    /// <summary>Saldo de pontos da conta nos canais assistidos, para o registro (ex.: "rodil=2380 | axt=260").</summary>
    Task<string> ReadBalancesAsync(string token, IReadOnlyList<string> watching, CancellationToken ct);
}

/// <summary>
/// Assiste lives por uma conta para juntar pontos do canal (o PokeIdle troca pontos por buffs). Twitch e Kick só dão
/// pontos em 2 canais ao mesmo tempo por conta (medido em 2026-11-07/08), então são até 2 páginas do site, numa janela
/// escondida fora da tela, no perfil de navegador da conta (o mesmo login). A cada minuto relê quem está ao vivo e troca a
/// live que encerrou (<see cref="TwitchWatchPlan"/>); sem nenhuma live da lista no ar (ou com as lives desligadas pela
/// pessoa), a janela fecha e a memória volta. Cada página é reaberta do zero a cada 2 h (a memória sobe com as horas).
/// </summary>
public sealed class LiveWatcher : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PageLifetime = TimeSpan.FromHours(2);

    /// <summary>Estado do vídeo; se estiver pausado, tenta dar play (mudo).</summary>
    private const string VideoCheckScript = """
        (() => {
          const v = document.querySelector('video');
          if (!v) return 'sem vídeo';
          if (v.paused) { v.muted = true; v.play().catch(() => {}); return 'pausado (tentando dar play)'; }
          return 'tocando' + (v.videoHeight ? ' ' + v.videoHeight + 'p' : '') + ', página ' + document.visibilityState;
        })()
        """;

    private readonly string _account;
    private readonly ILiveSite _site;
    private readonly Action<string> _log;
    private readonly Dispatcher _ui;
    private readonly Func<BrowserHost?> _hostOf;
    private readonly Func<CancellationToken, Task<string?>> _token;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Page> _pages = [];
    private readonly HashSet<string> _opening = [];
    private readonly SemaphoreSlim _wake = new(0);
    private volatile bool _enabled = true;
    private IReadOnlyList<string> _channels = [];
    private Window? _window;
    private StackPanel? _panel;
    private BrowserHost? _windowHost;
    private bool _started;
    private string? _lastBalances;
    private int _checks;

    private sealed class Page(WebView2 web, CoreWebView2 core)
    {
        public WebView2 Web { get; } = web;
        public CoreWebView2 Core { get; } = core;
        public string? Channel { get; set; }
        public string? LastVideo { get; set; }
        public DateTime OpenedAt { get; } = DateTime.UtcNow;
    }

    /// <param name="log">Registro de diagnóstico desta conta (nunca recebe chaves).</param>
    /// <param name="token">A chave da conta no site (null se ela não está logada nele agora).</param>
    public LiveWatcher(string account, ILiveSite site, Action<string> log, Dispatcher ui, Func<BrowserHost?> hostOf,
        Func<CancellationToken, Task<string?>> token)
    {
        _account = account;
        _site = site;
        _log = log;
        _ui = ui;
        _hostOf = hostOf;
        _token = token;
    }

    /// <summary>As lives sendo assistidas agora (na ordem da lista).</summary>
    public IReadOnlyList<string> Watching { get; private set; } = [];

    /// <summary>Mudou o que está sendo assistido. Vem na thread da tela.</summary>
    public event Action? Changed;

    /// <summary>A lista do perfil (ordem = prioridade). A troca acontece na próxima checagem.</summary>
    public void SetChannels(IReadOnlyList<string> channels) => _channels = channels.ToList();

    /// <summary>Liga/desliga as lives (a pessoa pode desligar para poupar RAM). Vale na hora: fecha ou abre as páginas já.</summary>
    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        Log(enabled ? "lives ligadas" : "lives desligadas pela pessoa");
        _wake.Release();
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _ = RunAsync(_stop.Token);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _ui.BeginInvoke(CloseWindow);
    }

    private void Log(string text) => _log(text);

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(FirstCheck, ct);
            while (!ct.IsCancellationRequested)
            {
                try { await CheckAsync(ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception e) when (e is HttpRequestException or FormatException or TaskCanceledException or IOException
                                              or InvalidOperationException or System.Runtime.InteropServices.COMException)
                {
                    Log("checagem falhou: " + e.Message);
                }
                await _wake.WaitAsync(Interval, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        var channels = _channels;
        var token = channels.Count == 0 || !_enabled ? null : await _token(ct);
        if (token is null)
        {
            await _ui.InvokeAsync(() => Apply([]));
            return;
        }
        var live = await _site.ReadLiveAsync(token, channels, Log, ct);
        var pick = TwitchWatchPlan.Pick(channels, live);
        if (pick.Count > 0)
        {
            var balances = await _site.ReadBalancesAsync(token, pick, ct);
            if (balances.Length > 0 && balances != _lastBalances) Log("saldo: " + balances);
            _lastBalances = balances;
        }
        await await _ui.InvokeAsync(async () =>
        {
            Apply(pick);
            await RecycleAndCheckVideoAsync();
        });
    }

    /// <summary>Deixa as páginas abertas nos canais escolhidos. Thread da tela.</summary>
    private void Apply(List<string> pick)
    {
        var host = _hostOf();
        if (host is null || pick.Count == 0)
        {
            if (_window is not null)
                Log(host is null ? "navegador fechado: páginas fechadas"
                    : !_enabled ? "páginas fechadas (lives desligadas)"
                    : "nenhuma live da lista ao vivo: páginas fechadas");
            CloseWindow();
            SetWatching([]);
            return;
        }
        if (_window is not null && _windowHost != host) CloseWindow(); // o navegador foi recriado

        // a página de um canal que saiu fica livre para um que entrou
        foreach (var page in _pages.Where(p => p.Channel is not null && !pick.Contains(p.Channel)))
        {
            Log($"{page.Channel} saiu (encerrou a live, saiu da lista ou entrou um canal mais acima na lista)");
            page.Channel = null;
        }
        var newcomers = pick.Where(c => !_opening.Contains(c) && _pages.All(p => p.Channel != c)).ToList();
        foreach (var channel in newcomers)
        {
            var page = _pages.FirstOrDefault(p => p.Channel is null);
            if (page is null)
            {
                _ = OpenPageAsync(host, channel);
                continue;
            }
            Navigate(page, channel);
        }
        foreach (var page in _pages.Where(p => p.Channel is null).ToList()) ClosePage(page);
        SetWatching(pick);
    }

    private async Task OpenPageAsync(BrowserHost host, string channel)
    {
        EnsureWindow(host);
        var web = new WebView2 { Width = _site.PageWidth, Height = _site.PageHeight, DefaultBackgroundColor = System.Drawing.Color.Black };
        _panel!.Children.Add(web);
        _opening.Add(channel);
        try
        {
            var options = host.Environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = _account;
            await web.EnsureCoreWebView2Async(host.Environment, options);
            if (_window is null || _windowHost != host || !Watching.Contains(channel))
            {
                _panel?.Children.Remove(web);
                web.Dispose(); // fechou/trocou enquanto abria
                return;
            }
            var core = web.CoreWebView2;
            core.IsMuted = true;
            core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.NewWindowRequested += (_, e) => e.Handled = true; // nada de janelas novas (anúncios, links)
            await _site.SetupPageAsync(host, core, Log);
            var page = new Page(web, core);
            _pages.Add(page);
            Navigate(page, channel);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            Log($"não abriu a página de {channel}: {e.Message}");
            _panel?.Children.Remove(web);
            web.Dispose();
        }
        finally { _opening.Remove(channel); }
    }

    private void Navigate(Page page, string channel)
    {
        page.Channel = channel;
        page.LastVideo = null;
        page.Core.Navigate(_site.PageUrl(channel));
        Log("assistindo " + channel);
    }

    /// <summary>Reabre do zero a página mais velha (se passou do tempo) e confere o vídeo de cada uma.</summary>
    private async Task RecycleAndCheckVideoAsync()
    {
        // uma página por vez (a mais velha), para a conta nunca ficar sem live aberta
        if (_pages.Where(p => p.Channel is not null && DateTime.UtcNow - p.OpenedAt > PageLifetime)
                .MinBy(p => p.OpenedAt) is { Channel: { } old } stale && _windowHost is { } recycleHost)
        {
            Log($"{old}: página reaberta do zero (aberta há mais de {PageLifetime.TotalHours:0} h, para soltar memória)");
            ClosePage(stale);
            await OpenPageAsync(recycleHost, old);
        }

        foreach (var page in _pages.ToList())
        {
            if (page.Channel is not { } channel) continue;
            string video;
            try { video = (await page.Core.ExecuteScriptAsync(VideoCheckScript)).Trim('"'); }
            catch (InvalidOperationException) { continue; }
            if (video != page.LastVideo) Log($"{channel}: {video}");
            page.LastVideo = video;
        }
        if (++_checks % 5 == 0 && _pages.Count > 0) await LogMemoryAsync();
    }

    /// <summary>Memória de cada página (o processo que desenha ela), a cada 5 checagens.</summary>
    private async Task LogMemoryAsync()
    {
        try
        {
            var infos = await _pages[0].Core.Environment.GetProcessExtendedInfosAsync();
            var parts = new List<string>();
            foreach (var page in _pages)
            {
                var frame = (ulong)page.Core.FrameId;
                var info = infos.FirstOrDefault(i => i.ProcessInfo.Kind == CoreWebView2ProcessKind.Renderer
                                                     && i.AssociatedFrameInfos.Any(f => f.FrameId == frame));
                if (info is null) { parts.Add($"{page.Channel}=?"); continue; }
                using var process = System.Diagnostics.Process.GetProcessById(info.ProcessInfo.ProcessId);
                parts.Add($"{page.Channel}={process.PrivateMemorySize64 / (1024 * 1024)} MB");
            }
            Log("memória: " + string.Join(" | ", parts));
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            Log("memória: " + e.Message);
        }
    }

    private void SetWatching(List<string> pick)
    {
        if (Watching.SequenceEqual(pick)) return;
        Watching = pick;
        Changed?.Invoke();
    }

    private void EnsureWindow(BrowserHost host)
    {
        if (_window is not null) return;
        _panel = new StackPanel();
        // fora da tela, sem barra de tarefas nem Alt+Tab; não é dona de nada (minimizar o app não pausa as lives)
        _window = new Window
        {
            Title = $"BookIdle · {_site.Name} · {_account}",
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            Width = _site.PageWidth + 16,
            Height = _site.PageHeight * TwitchWatchPlan.Slots + 40,
            Content = _panel,
        };
        _windowHost = host;
        _window.Show();
    }

    private void ClosePage(Page page)
    {
        _pages.Remove(page);
        _panel?.Children.Remove(page.Web);
        page.Web.Dispose();
    }

    private void CloseWindow()
    {
        foreach (var page in _pages.ToList()) ClosePage(page);
        _window?.Close();
        _window = null;
        _panel = null;
        _windowHost = null;
    }
}
