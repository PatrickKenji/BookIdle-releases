using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using IdleDeck.Core.Health;

namespace IdleDeck.Core.Twitch;

public enum TwitchStatus { Disconnected, Connecting, Connected, AuthFailed }

/// <summary>
/// Uma conta na Twitch, só presente no chat: uma conexão (IRC sobre WebSocket) logada com a chave da conta, que entra
/// em todos os canais pedidos. Para a Twitch é o mesmo que ter o chat popup de cada canal aberto — sem navegador, sem
/// vídeo e sem mostrar nada. Reconecta sozinha (espera crescente); chave recusada para de tentar (<see cref="TwitchStatus.AuthFailed"/>).
/// </summary>
public sealed class TwitchChatConnection : IAsyncDisposable
{
    public static readonly Uri DefaultEndpoint = new("wss://irc-ws.chat.twitch.tv:443");

    private enum SessionEnd { Dropped, AuthFailed, Reconnect }

    private readonly string _login;
    private readonly Func<CancellationToken, Task<string?>> _tokenProvider;
    private readonly Uri _endpoint;
    private readonly Func<TimeSpan>? _retryDelay;
    private readonly JoinScheduler _joins = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0);
    private readonly object _gate = new();
    private readonly HashSet<string> _wanted = [];
    private readonly HashSet<string> _joined = [];
    private readonly HashSet<string> _asked = []; // JOIN enviado, esperando a confirmação
    private CancellationTokenSource? _cts;
    private Task? _run;
    private TwitchStatus _status = TwitchStatus.Disconnected;

    /// <param name="login">Nome de usuário da Twitch dono da chave.</param>
    /// <param name="tokenProvider">Entrega a chave de acesso válida (renovando se preciso) ou null se não há mais como.</param>
    /// <param name="endpoint">Servidor do chat (só os testes mudam isto).</param>
    /// <param name="retryDelay">Espera entre tentativas (padrão: 5 s dobrando até 60 s; só os testes mudam isto).</param>
    public TwitchChatConnection(string login, Func<CancellationToken, Task<string?>> tokenProvider, Uri? endpoint = null,
        Func<TimeSpan>? retryDelay = null)
    {
        _retryDelay = retryDelay;
        _login = login.ToLowerInvariant();
        _tokenProvider = tokenProvider;
        _endpoint = endpoint ?? DefaultEndpoint;
    }

    public TwitchStatus Status { get { lock (_gate) return _status; } }

    /// <summary>Canais em que a conexão confirmou a entrada.</summary>
    public IReadOnlyCollection<string> JoinedChannels { get { lock (_gate) return [.. _joined]; } }

    /// <summary>Canais pedidos (normalizados).</summary>
    public IReadOnlyCollection<string> WantedChannels { get { lock (_gate) return [.. _wanted]; } }

    /// <summary>O estado ou os canais confirmados mudaram (pode vir de outra thread).</summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Diagnóstico: cada linha enviada e recebida (a chave nunca aparece: o PASS sai mascarado). Pode vir de outra thread.
    /// </summary>
    public event EventHandler<string>? Trace;

    private void Log(string text) => Trace?.Invoke(this, text);

    /// <summary>Erro inesperado numa tentativa (a conexão tenta de novo sozinha). Pode vir de outra thread.</summary>
    public event EventHandler<Exception>? Error;

    /// <summary>Troca a lista de canais. Com a conexão aberta, entra nos novos e sai dos removidos, sem reconectar.</summary>
    public void SetChannels(IEnumerable<string> channels)
    {
        var normalized = TwitchChannels.Normalize(channels);
        lock (_gate)
        {
            _wanted.Clear();
            foreach (var channel in normalized) _wanted.Add(channel);
        }
        _wake.Release();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Start()
    {
        if (_run is not null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _run = Task.Run(() => RunAsync(ct));
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var run = _run;
        _cts = null;
        _run = null;
        if (cts is null || run is null) return;
        await cts.CancelAsync();
        try { await run; }
        catch (OperationCanceledException) { }
        cts.Dispose();
        lock (_gate)
        {
            _joined.Clear();
            _asked.Clear();
        }
        SetStatus(TwitchStatus.Disconnected);
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    // ---------- Laço principal ----------

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = new RetryBackoff();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                SetStatus(TwitchStatus.Connecting);
                Log("pedindo a chave ao navegador da conta");
                var token = await _tokenProvider(ct);
                if (string.IsNullOrEmpty(token))
                {
                    Log("sem chave (ninguém logado na Twitch com este login no navegador da conta) → AuthFailed");
                    SetStatus(TwitchStatus.AuthFailed);
                    return;
                }
                Log($"chave encontrada ({token.Length} caracteres); conectando em {_endpoint}");
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(_endpoint, ct);
                var end = await SessionAsync(socket, token, ct);
                Log($"sessão terminou: {end}");
                if (end == SessionEnd.AuthFailed)
                {
                    SetStatus(TwitchStatus.AuthFailed);
                    return;
                }
                if (end == SessionEnd.Reconnect) backoff.Reset();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e) when (e is WebSocketException or HttpRequestException or IOException or OperationCanceledException)
            {
                // queda de rede, timeout ou erro do servidor (ex.: renovar a chave sem internet): tenta de novo abaixo
                Log($"queda: {e.GetType().Name}: {e.Message}");
            }
            catch (Exception e)
            {
                // algo que não esperávamos (resposta estranha da Twitch…): avisa e tenta de novo, em vez de travar em "conectando"
                Error?.Invoke(this, e);
            }

            ClearJoined();
            SetStatus(TwitchStatus.Disconnected);
            try { await Task.Delay(_retryDelay?.Invoke() ?? backoff.NextDelay(), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<SessionEnd> SessionAsync(ClientWebSocket socket, string token, CancellationToken ct)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var welcome = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendAsync(socket, $"PASS oauth:{token}", ct);
        await SendAsync(socket, $"NICK {_login}", ct);
        var joiner = Task.Run(() => JoinLoopAsync(socket, welcome.Task, session.Token), session.Token);
        try
        {
            await foreach (var line in ReadLinesAsync(socket, session.Token))
            {
                Log("← " + line);
                if (IrcMessage.Parse(line) is not { } message) continue;
                switch (message.Command)
                {
                    case "PING":
                        await SendAsync(socket, $"PONG :{message.Trailing ?? "tmi.twitch.tv"}", ct);
                        break;
                    case "001":
                        welcome.TrySetResult();
                        SetStatus(TwitchStatus.Connected);
                        break;
                    case "JOIN" when IsMe(message) && message.Params.Count > 0:
                        MarkJoined(message.Params[0]);
                        break;
                    case "366" when message.Params.Count > 1: // fim da lista de presentes do canal: entramos
                        MarkJoined(message.Params[1]);
                        break;
                    case "PART" when IsMe(message) && message.Params.Count > 0:
                        MarkParted(message.Params[0]);
                        break;
                    case "NOTICE" when IsAuthFailure(message):
                        return SessionEnd.AuthFailed;
                    case "RECONNECT":
                        return SessionEnd.Reconnect;
                }
            }
            return SessionEnd.Dropped;
        }
        finally
        {
            await session.CancelAsync();
            try { await joiner; }
            catch (OperationCanceledException) { }
            catch (Exception e) when (e is WebSocketException or IOException or ObjectDisposedException) { }
        }
    }

    /// <summary>Depois do boas-vindas, mantém as entradas iguais à lista pedida (respeitando o limite de JOIN).</summary>
    private async Task JoinLoopAsync(ClientWebSocket socket, Task welcomed, CancellationToken ct)
    {
        await welcomed.WaitAsync(ct);
        while (!ct.IsCancellationRequested)
        {
            List<string> toPart, toJoin;
            lock (_gate)
            {
                toPart = _joined.Concat(_asked).Where(c => !_wanted.Contains(c)).Distinct().ToList();
                toJoin = _wanted.Where(c => !_joined.Contains(c) && !_asked.Contains(c)).ToList();
            }
            foreach (var channel in toPart)
            {
                await SendAsync(socket, $"PART #{channel}", ct);
                lock (_gate)
                {
                    _joined.Remove(channel);
                    _asked.Remove(channel);
                }
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            foreach (var channel in toJoin)
            {
                var wait = _joins.NextDelay(DateTime.UtcNow);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                await SendAsync(socket, $"JOIN #{channel}", ct);
                _joins.Record(DateTime.UtcNow);
                lock (_gate) _asked.Add(channel);
            }
            await _wake.WaitAsync(ct);
        }
    }

    // ---------- Apoio ----------

    private bool IsMe(IrcMessage message) =>
        message.Prefix?.StartsWith(_login + "!", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsAuthFailure(IrcMessage message) =>
        message.Trailing is { } text
        && (text.Contains("authentication failed", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Improperly formatted auth", StringComparison.OrdinalIgnoreCase));

    private void MarkJoined(string channel)
    {
        lock (_gate)
        {
            var name = channel.TrimStart('#').ToLowerInvariant();
            _asked.Remove(name);
            _joined.Add(name);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MarkParted(string channel)
    {
        lock (_gate)
        {
            var name = channel.TrimStart('#').ToLowerInvariant();
            _asked.Remove(name);
            _joined.Remove(name);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearJoined()
    {
        lock (_gate)
        {
            _joined.Clear();
            _asked.Clear();
        }
    }

    private void SetStatus(TwitchStatus status)
    {
        lock (_gate)
        {
            if (_status == status) return;
            _status = status;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>O ClientWebSocket não aceita dois envios ao mesmo tempo (PONG e JOIN podem coincidir).</summary>
    private async Task SendAsync(ClientWebSocket socket, string line, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            Log("→ " + (line.StartsWith("PASS ", StringComparison.Ordinal) ? "PASS oauth:***" : line));
            await socket.SendAsync(Encoding.UTF8.GetBytes(line + "\r\n"), WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(ClientWebSocket socket, [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[8192];
        var text = new StringBuilder();
        while (socket.State == WebSocketState.Open)
        {
            text.Clear();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) yield break;
                text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            foreach (var line in text.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                yield return line;
        }
    }
}
