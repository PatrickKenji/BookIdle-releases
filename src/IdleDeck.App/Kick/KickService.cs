using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using IdleDeck.App.Browser;
using IdleDeck.App.Lives;
using IdleDeck.Core;
using IdleDeck.Core.Kick;
using Microsoft.Web.WebView2.Core;

namespace IdleDeck.App.Kick;

/// <summary>Registro de diagnóstico da Kick (<c>kick.log</c> na pasta de dados). Nunca recebe chaves.</summary>
public static class KickLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.Root, "kick.log");

    public static void Write(string account, string text)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Root);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"[{DateTime.Now:HH:mm:ss}] {account}: {text}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>Quem entrou na Kick no navegador de uma conta (a sessão fica no cookie do perfil dela).</summary>
public static class KickSession
{
    /// <summary>A chave da sessão da Kick do perfil de <paramref name="core"/>, ou null. Thread da tela.</summary>
    public static async Task<string?> ReadTokenAsync(CoreWebView2 core)
    {
        var cookies = await core.CookieManager.GetCookiesAsync("https://kick.com");
        return KickWebCookies.TryReadToken(cookies.Select(c => new KeyValuePair<string, string>(c.Name, c.Value)), out var token)
            ? token : null;
    }

    /// <summary>O nome de usuário da sessão, ou null (sem sessão, ou a Kick não respondeu).</summary>
    public static async Task<string?> ReadUsernameAsync(string token)
    {
        try { return await KickApi.ReadUsernameAsync(token); }
        catch (Exception e) when (e is HttpRequestException or FormatException or TaskCanceledException) { return null; }
    }
}

/// <summary>
/// Lives da Kick por conta: na Kick, ficar no chat não rende pontos (testado pelo Patrick em 2026-10-22), então cada conta
/// com Kick só assiste até 2 lives da lista do perfil (<see cref="LiveWatcher"/> + <see cref="KickLiveSite"/>).
/// </summary>
public sealed class KickService : IDisposable
{
    /// <summary>Uma conta que deve assistir: a lista do perfil (ordem = prioridade) e se as lives estão ligadas.</summary>
    public sealed record Desired(string Account, IReadOnlyList<string> Channels, bool WatchLives);

    private static readonly TimeSpan BrowserWait = TimeSpan.FromMinutes(2);

    private readonly Dispatcher _ui;
    private readonly Func<string, CoreWebView2?> _coreOf;
    private readonly Func<BrowserHost?> _hostOf;
    private readonly Dictionary<string, LiveWatcher> _watchers = [];

    public KickService(Dispatcher ui, Func<string, CoreWebView2?> coreOf, Func<BrowserHost?> hostOf)
    {
        _ui = ui;
        _coreOf = coreOf;
        _hostOf = hostOf;
    }

    /// <summary>Mudou o que alguma conta está assistindo. Pode vir de qualquer thread.</summary>
    public event Action? Changed;

    /// <summary>As lives que a conta assiste agora, ou null se a Kick não está ligada nela.</summary>
    public IReadOnlyList<string>? WatchingOf(string account) => _watchers.TryGetValue(account, out var w) ? w.Watching : null;

    public void Sync(IReadOnlyList<Desired> desired)
    {
        var wanted = desired.ToDictionary(d => d.Account);
        foreach (var account in _watchers.Keys.ToList())
            if (!wanted.ContainsKey(account)) Stop(account);

        foreach (var d in desired)
        {
            if (!_watchers.TryGetValue(d.Account, out var watcher))
            {
                var account = d.Account;
                watcher = new LiveWatcher(account, new KickLiveSite(), text => KickLog.Write(account, text), _ui, _hostOf,
                    ct => ProvideTokenAsync(account, ct));
                watcher.Changed += () => Changed?.Invoke();
                _watchers[account] = watcher;
                watcher.SetChannels(d.Channels);
                watcher.SetEnabled(d.WatchLives);
                watcher.Start();
                continue;
            }
            watcher.SetChannels(d.Channels);
            watcher.SetEnabled(d.WatchLives);
        }
        Changed?.Invoke();
    }

    public void Stop(string account)
    {
        if (!_watchers.Remove(account, out var watcher)) return;
        watcher.Dispose();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        foreach (var account in _watchers.Keys.ToList()) Stop(account);
    }

    /// <summary>A sessão da Kick do navegador da conta (espera o navegador subir). Null se ninguém está logado ali.</summary>
    private async Task<string?> ProvideTokenAsync(string account, CancellationToken ct)
    {
        var waited = TimeSpan.Zero;
        CoreWebView2? core;
        while ((core = await _ui.InvokeAsync(() => _coreOf(account))) is null)
        {
            if (waited >= BrowserWait) return null;
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            waited += TimeSpan.FromSeconds(1);
        }
        try
        {
            var token = await await _ui.InvokeAsync(() => KickSession.ReadTokenAsync(core));
            if (token is null) KickLog.Write(account, "a conta não está logada na Kick no navegador dela (Conectar Kick de novo)");
            return token;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            throw new IOException("O navegador da conta não está disponível agora.", e);
        }
    }
}
