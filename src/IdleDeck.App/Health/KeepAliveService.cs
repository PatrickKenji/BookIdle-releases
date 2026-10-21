using System.Net.NetworkInformation;
using System.Windows.Threading;
using IdleDeck.App.Browser;
using IdleDeck.Core.Health;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace IdleDeck.App.Health;

/// <summary>
/// Mantém as contas vivas: recarrega quando a página trava, o renderizador cai, a navegação falha
/// (com espera crescente), a rede volta ou o PC sai da suspensão.
/// </summary>
public sealed class KeepAliveService : IDisposable
{
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LongOutage = TimeSpan.FromSeconds(60);

    private readonly List<AccountView> _views = [];
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _heartbeat = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly Dictionary<AccountView, RetryBackoff> _backoffs = [];
    private readonly Dictionary<AccountView, int> _missedBeats = [];
    private readonly Dictionary<AccountView, DispatcherTimer> _retryTimers = [];
    private DateTime? _networkLostAt;

    public KeepAliveService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _heartbeat.Tick += async (_, _) => await HeartbeatAsync();
    }

    /// <summary>Disparado quando o processo principal do navegador morre; o app precisa recriar tudo.</summary>
    public event EventHandler? BrowserProcessExited;

    public void Start()
    {
        _heartbeat.Start();
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    /// <summary>Liga os eventos de uma conta. Chamar depois de cada (re)inicialização do WebView2.</summary>
    public void Attach(AccountView view)
    {
        if (!_views.Contains(view))
        {
            _views.Add(view);
            _backoffs[view] = new RetryBackoff();
            _missedBeats[view] = 0;
        }
        var core = view.Core!;
        core.ProcessFailed += (_, e) =>
        {
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                BrowserProcessExited?.Invoke(this, EventArgs.Empty);
            else if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited
                     or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive
                     or CoreWebView2ProcessFailedKind.FrameRenderProcessExited)
                view.Reload();
        };
        core.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess)
            {
                _backoffs[view].Reset();
                _missedBeats[view] = 0;
            }
            else if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
            {
                ScheduleRetry(view);
            }
        };
    }

    /// <summary>Para de cuidar de uma conta (removida).</summary>
    public void Detach(AccountView view)
    {
        CancelRetry(view);
        _views.Remove(view);
        _backoffs.Remove(view);
        _missedBeats.Remove(view);
    }

    private void CancelRetry(AccountView view)
    {
        if (_retryTimers.Remove(view, out var timer)) timer.Stop();
    }

    private void ScheduleRetry(AccountView view)
    {
        CancelRetry(view);
        var timer = new DispatcherTimer { Interval = _backoffs[view].NextDelay() };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _retryTimers.Remove(view);
            if (_views.Contains(view)) view.Reload();
        };
        _retryTimers[view] = timer;
        timer.Start();
    }

    private async Task HeartbeatAsync()
    {
        foreach (var view in _views.ToList())
        {
            if (!_views.Contains(view) || view.Core is not { } core || view.Status is not AccountStatus.Online) continue;

            var script = core.ExecuteScriptAsync("1");
            var finished = await Task.WhenAny(script, Task.Delay(HeartbeatTimeout));
            var alive = finished == script && script.IsCompletedSuccessfully;

            if (!_missedBeats.TryGetValue(view, out var missed)) continue; // removida enquanto esperava
            _missedBeats[view] = missed = alive ? 0 : missed + 1;
            if (missed >= 2)
            {
                _missedBeats[view] = 0;
                view.Reload();
            }
        }
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (!e.IsAvailable)
        {
            _networkLostAt ??= DateTime.UtcNow;
            return;
        }
        var longOutage = _networkLostAt is { } lost && DateTime.UtcNow - lost >= LongOutage;
        _networkLostAt = null;
        _dispatcher.BeginInvoke(() =>
        {
            // depois de muito tempo sem rede, o jogo pode ter perdido a conexão mesmo com a página "Online"
            foreach (var view in _views.Where(v => longOutage || v.Status == AccountStatus.Offline).ToList())
            {
                CancelRetry(view);
                _backoffs[view].Reset();
                view.Reload();
            }
        });
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        _dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            foreach (var view in _views.ToList()) view.Reload();
        });
    }

    public void Dispose()
    {
        _heartbeat.Stop();
        foreach (var t in _retryTimers.Values) t.Stop();
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
