using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using IdleDeck.Core.Twitch;

namespace IdleDeck.Core.Tests;

/// <summary>Servidor IRC falso (WebSocket em localhost): responde como o chat da Twitch responderia.</summary>
internal sealed class FakeIrcServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly List<string> _received = [];
    private WebSocket? _socket;
    private int _connections;

    public FakeIrcServer(bool rejectLogin = false)
    {
        RejectLogin = rejectLogin;
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        Endpoint = new Uri($"ws://localhost:{port}/");
        _ = Task.Run(AcceptLoopAsync);
    }

    public Uri Endpoint { get; }
    public bool RejectLogin { get; }
    public int Connections => Volatile.Read(ref _connections);
    public IReadOnlyList<string> Received { get { lock (_gate) return [.. _received]; } }

    public async Task SendAsync(string line)
    {
        var socket = _socket ?? throw new InvalidOperationException("ninguém conectado");
        await socket.SendAsync(Encoding.UTF8.GetBytes(line + "\r\n"), WebSocketMessageType.Text, true, _cts.Token);
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();
                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }
                var ws = (await context.AcceptWebSocketAsync(null)).WebSocket;
                Interlocked.Increment(ref _connections);
                _socket = ws;
                _ = Task.Run(() => SessionAsync(ws));
            }
        }
        catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or OperationCanceledException) { }
    }

    private async Task SessionAsync(WebSocket ws)
    {
        var buffer = new byte[4096];
        var nick = "";
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                var line = Encoding.UTF8.GetString(buffer, 0, result.Count).Trim();
                lock (_gate) _received.Add(line);

                if (line.StartsWith("NICK "))
                {
                    nick = line[5..];
                    await SendAsync(RejectLogin
                        ? ":tmi.twitch.tv NOTICE * :Login authentication failed"
                        : $":tmi.twitch.tv 001 {nick} :Welcome, GLHF!");
                }
                else if (line.StartsWith("JOIN #"))
                    await SendAsync($":{nick}!{nick}@{nick}.tmi.twitch.tv JOIN {line[5..]}");
                else if (line.StartsWith("PART #"))
                    await SendAsync($":{nick}!{nick}@{nick}.tmi.twitch.tv PART {line[5..]}");
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Close();
        return ValueTask.CompletedTask;
    }
}

public class TwitchChatConnectionTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, string what, int timeoutMs = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Esperando: {what}");
            await Task.Delay(20);
        }
    }

    private static TwitchChatConnection Make(FakeIrcServer server, Func<CancellationToken, Task<string?>>? token = null, TimeSpan? retryDelay = null) =>
        new("patrick", token ?? (_ => Task.FromResult<string?>("tok123")), server.Endpoint, retryDelay is { } d ? () => d : null);

    [Fact]
    public async Task Connects_sends_credentials_and_joins_the_wanted_channels()
    {
        await using var server = new FakeIrcServer();
        await using var chat = Make(server);
        chat.SetChannels(["Canal1", "#canal2", "canal1"]);

        chat.Start();
        await WaitUntilAsync(() => chat.Status == TwitchStatus.Connected && chat.JoinedChannels.Count == 2, "conectar e entrar nos 2 canais");

        Assert.Equal(["canal1", "canal2"], chat.JoinedChannels.Order());
        var sent = server.Received;
        Assert.Contains("PASS oauth:tok123", sent);
        Assert.Contains("NICK patrick", sent);
        Assert.Contains("JOIN #canal1", sent);
        Assert.Contains("JOIN #canal2", sent);
        Assert.True(sent.ToList().IndexOf("PASS oauth:tok123") < sent.ToList().IndexOf("NICK patrick"));
    }

    [Fact]
    public async Task Replies_to_ping_with_pong()
    {
        await using var server = new FakeIrcServer();
        await using var chat = Make(server);
        chat.Start();
        await WaitUntilAsync(() => chat.Status == TwitchStatus.Connected, "conectar");

        await server.SendAsync("PING :tmi.twitch.tv");

        await WaitUntilAsync(() => server.Received.Contains("PONG :tmi.twitch.tv"), "receber o PONG");
    }

    [Fact]
    public async Task Auth_failure_sets_AuthFailed_and_does_not_reconnect()
    {
        await using var server = new FakeIrcServer(rejectLogin: true);
        await using var chat = Make(server);

        chat.Start();
        await WaitUntilAsync(() => chat.Status == TwitchStatus.AuthFailed, "ficar em AuthFailed");
        await Task.Delay(400);

        Assert.Equal(TwitchStatus.AuthFailed, chat.Status);
        Assert.Equal(1, server.Connections);
        Assert.Empty(chat.JoinedChannels);
    }

    [Fact]
    public async Task Missing_token_sets_AuthFailed_without_connecting()
    {
        await using var server = new FakeIrcServer();
        await using var chat = Make(server, _ => Task.FromResult<string?>(null));

        chat.Start();
        await WaitUntilAsync(() => chat.Status == TwitchStatus.AuthFailed, "ficar em AuthFailed");

        Assert.Equal(0, server.Connections);
    }

    [Fact]
    public async Task SetChannels_parts_removed_and_joins_new_without_reconnecting()
    {
        await using var server = new FakeIrcServer();
        await using var chat = Make(server);
        chat.SetChannels(["a", "b"]);
        chat.Start();
        await WaitUntilAsync(() => chat.JoinedChannels.Count == 2, "entrar em a e b");

        chat.SetChannels(["b", "c"]);

        await WaitUntilAsync(() => chat.JoinedChannels.Order().SequenceEqual(["b", "c"]), "trocar a por c");
        Assert.Contains("PART #a", server.Received);
        Assert.Contains("JOIN #c", server.Received);
        Assert.Equal(1, server.Connections);
    }

    [Fact]
    public async Task An_unexpected_error_is_reported_and_the_connection_keeps_retrying()
    {
        await using var server = new FakeIrcServer();
        var calls = 0;
        await using var chat = Make(server, _ =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new KeyNotFoundException("resposta estranha da Twitch");
            return Task.FromResult<string?>("tok123");
        }, retryDelay: TimeSpan.FromMilliseconds(30));
        var errors = new List<Exception>();
        chat.Error += (_, e) => { lock (errors) errors.Add(e); };

        chat.Start();
        await WaitUntilAsync(() => chat.Status == TwitchStatus.Connected, "reconectar depois do erro");

        Assert.Single(errors);
        Assert.IsType<KeyNotFoundException>(errors[0]);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Stop_closes_the_connection_and_resets_the_state()
    {
        await using var server = new FakeIrcServer();
        await using var chat = Make(server);
        chat.SetChannels(["a"]);
        chat.Start();
        await WaitUntilAsync(() => chat.JoinedChannels.Count == 1, "entrar em a");

        await chat.StopAsync();

        Assert.Equal(TwitchStatus.Disconnected, chat.Status);
        Assert.Empty(chat.JoinedChannels);
    }
}
