using System.Text.Json;
using IdleDeck.App.Modules;
using IdleDeck.Core.Market;

namespace IdleDeck.App.Market;

/// <summary>
/// O canal de mercado de uma conta, por cima da ponte (<c>market-bridge.js</c>) que vive na página do jogo:
/// os pedidos saem pelo WebSocket que o jogo já abriu e o que a ponte repassa chega em <see cref="Deliver"/>.
/// </summary>
public sealed class PageMarketChannel : IMarketChannel
{
    private readonly IModuleHostServices _host;
    private readonly int _accountId;
    private volatile bool _open;

    public PageMarketChannel(IModuleHostServices host, int accountId)
    {
        _host = host;
        _accountId = accountId;
    }

    /// <summary>Verdadeiro depois que a página mandou algo (o jogo conectou) e até um pedido não sair.</summary>
    public bool IsOpen => _open;

    public event Action<MarketMessage>? Received;

    public async Task<bool> SendAsync(string json, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var script = $"window.__biSniper ? window.__biSniper.send({JsonSerializer.Serialize(json)}) : false";
        string? result;
        try { result = await _host.ExecutarNaContaAsync(_accountId, script); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            result = null; // navegador sendo recriado
        }
        var sent = result == "true";
        if (!sent) _open = false;
        return sent;
    }

    /// <summary>A página mandou <c>{ frame: "…" }</c>; vira as mensagens do mercado.</summary>
    public void Deliver(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("frame", out var frame) || frame.ValueKind != JsonValueKind.String)
            return;
        _open = true;
        foreach (var message in MarketProtocol.ParseAll(frame.GetString() ?? ""))
            Received?.Invoke(message);
    }

    /// <summary>A conta recarregou ou saiu: até o jogo reconectar não há o que mandar.</summary>
    public void MarkClosed() => _open = false;
}
