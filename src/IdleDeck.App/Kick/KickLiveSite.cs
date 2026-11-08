using System.Net.Http;
using IdleDeck.App.Browser;
using IdleDeck.App.Lives;
using IdleDeck.Core.Kick;
using Microsoft.Web.WebView2.Core;

namespace IdleDeck.App.Kick;

/// <summary>
/// As lives da Kick: a página normal do canal (kick.com/canal), muda, com o vídeo tocando de verdade (~290 MB por página).
/// Medido em 2026-11-08: travar a qualidade como na Twitch deixava o player parado e não poupava memória, então não se usa.
/// Ao vivo vem da API pública da Kick; o saldo, com a sessão da conta (<see cref="KickApi"/>).
/// </summary>
public sealed class KickLiveSite : ILiveSite
{
    public string Name => "Kick";
    public int PageWidth => 480;
    public int PageHeight => 270;

    public string PageUrl(string channel) => "https://kick.com/" + channel;

    public Task SetupPageAsync(BrowserHost host, CoreWebView2 core, Action<string> log) => Task.CompletedTask;

    public async Task<IReadOnlySet<string>> ReadLiveAsync(string token, IReadOnlyList<string> channels, Action<string> log, CancellationToken ct)
    {
        var errors = new List<string>();
        var live = await KickApi.ReadLiveAsync(channels, errors, ct);
        if (errors.Count > 0) log("não deu para ver se estão ao vivo: " + string.Join(" | ", errors));
        return live;
    }

    public async Task<string> ReadBalancesAsync(string token, IReadOnlyList<string> watching, CancellationToken ct)
    {
        var parts = new List<string>();
        foreach (var channel in watching)
        {
            try { parts.Add($"{channel}={(await KickApi.ReadPointsAsync(token, channel, ct))?.ToString() ?? "?"}"); }
            catch (Exception e) when (e is HttpRequestException or FormatException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                parts.Add($"{channel}=? ({e.Message})");
            }
        }
        return string.Join(" | ", parts);
    }
}
