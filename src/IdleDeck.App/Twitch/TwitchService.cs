using System.IO;
using System.Windows.Threading;
using IdleDeck.Core.Twitch;
using Microsoft.Web.WebView2.Core;

namespace IdleDeck.App.Twitch;

/// <summary>
/// Mantém uma conexão de chat por conta com a Twitch ligada (<see cref="TwitchChatConnection"/>). A chave vem do próprio
/// navegador da conta: quem entrou na Twitch ali deixa o login e a chave nos cookies, e a conexão leve (poucos MB, sem
/// navegador por canal) usa essa mesma chave — como um navegador só com o chat de todos os canais.
/// </summary>
public sealed class TwitchService : IDisposable
{
    /// <summary>Uma conta que deve estar no chat: o login da Twitch dela e os canais do perfil.</summary>
    public sealed record Desired(string Account, string Login, IReadOnlyList<string> Channels);

    public sealed record State(TwitchStatus Status, int Joined, int Wanted);

    private static readonly TimeSpan BrowserWait = TimeSpan.FromMinutes(2);

    private readonly Dispatcher _ui;
    private readonly Func<string, CoreWebView2?> _coreOf;
    private readonly Dictionary<string, (TwitchChatConnection Chat, string Login)> _chats = [];

    /// <param name="ui">Thread da tela: os cookies só são lidos nela.</param>
    /// <param name="coreOf">O navegador de uma conta (pelo nome do perfil dela), ou null enquanto ainda não subiu.</param>
    public TwitchService(Dispatcher ui, Func<string, CoreWebView2?> coreOf)
    {
        _ui = ui;
        _coreOf = coreOf;
    }

    /// <summary>Algo mudou (conectou, entrou num canal, caiu…). Pode vir de qualquer thread.</summary>
    public event Action? Changed;

    public State? StateOf(string account) =>
        _chats.TryGetValue(account, out var entry)
            ? new State(entry.Chat.Status, entry.Chat.JoinedChannels.Count, entry.Chat.WantedChannels.Count)
            : null;

    /// <summary>
    /// Deixa as conexões iguais ao pedido: abre as que faltam, fecha as que sobram (ou trocaram de login)
    /// e atualiza os canais das que continuam, sem derrubar nada.
    /// </summary>
    public void Sync(IReadOnlyList<Desired> desired)
    {
        var wanted = desired.ToDictionary(d => d.Account);
        foreach (var account in _chats.Keys.ToList())
            if (!wanted.TryGetValue(account, out var d) || d.Login != _chats[account].Login) Stop(account);

        foreach (var d in desired)
        {
            if (_chats.TryGetValue(d.Account, out var entry))
            {
                entry.Chat.SetChannels(d.Channels);
                continue;
            }
            var chat = new TwitchChatConnection(d.Login, ct => ProvideTokenAsync(d.Account, d.Login, ct));
            chat.StateChanged += (_, _) => Changed?.Invoke();
            chat.Error += (_, e) => App.Log(e);
            chat.Trace += (_, line) => TwitchLog.Write(d.Account, line);
            _chats[d.Account] = (chat, d.Login);
            chat.SetChannels(d.Channels);
            chat.Start();
        }
        Changed?.Invoke();
    }

    public void Stop(string account)
    {
        if (!_chats.Remove(account, out var entry)) return;
        _ = entry.Chat.DisposeAsync().AsTask();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        foreach (var account in _chats.Keys.ToList()) Stop(account);
    }

    /// <summary>
    /// A chave da Twitch do navegador da conta. Espera o navegador subir (no início do app). Null se ninguém está logado
    /// na Twitch ali — ou se quem está agora NÃO é o login ligado a esta conta (nunca usa a chave de outra pessoa): a
    /// conexão fica em "reconectar". Navegador sendo recriado: erro de E/S, e a conexão tenta de novo.
    /// </summary>
    private async Task<string?> ProvideTokenAsync(string account, string login, CancellationToken ct)
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
            var session = await await _ui.InvokeAsync(() => TwitchSession.ReadAsync(core));
            if (session is null)
            {
                var names = await await _ui.InvokeAsync(() => TwitchSession.CookieNamesAsync(core));
                TwitchLog.Write(account, $"nenhum login da Twitch nos cookies do navegador desta conta. Cookies de twitch.tv encontrados: {(names.Count == 0 ? "(nenhum)" : string.Join(", ", names))}");
            }
            else if (session.Login != login)
            {
                TwitchLog.Write(account, $"o navegador desta conta está logado na Twitch como '{session.Login}', mas a conta está ligada a '{login}'");
            }
            return session is not null && session.Login == login ? session.Token : null;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            throw new IOException("O navegador da conta não está disponível agora.", e);
        }
    }
}
