using System.IO;
using System.Net.Http;
using IdleDeck.App.Browser;
using IdleDeck.App.Lives;
using IdleDeck.Core.Twitch;
using Microsoft.Web.WebView2.Core;

namespace IdleDeck.App.Twitch;

/// <summary>
/// As lives da Twitch: site de celular (~210 MB por página, contra ~350 do site normal), sem imagens, sem o chat da página
/// (o app já fica no chat pela conexão leve), travado na menor qualidade (<see cref="TwitchPlaylist"/>) e pequeno.
/// Ao vivo e saldo vêm da API do próprio site (<see cref="TwitchGql"/>), numa consulta só.
/// </summary>
public sealed class TwitchLiveSite : ILiveSite
{
    private const string MobileAgent = "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Na página: menor qualidade e sem som (o WebView2 também fica mudo).</summary>
    private const string PageScript = """
        try { localStorage.setItem('video-quality', JSON.stringify({ default: '160p30' })); localStorage.setItem('volume', '0'); } catch {}
        """;

    /// <summary>
    /// O chat da própria página não abre: sem ele a página não acumula mensagens. Só a conexão do chat é recusada; o resto
    /// da Twitch (inclusive a dos pontos) continua.
    /// </summary>
    private const string NoPageChatScript = """
        (() => {
          const Real = window.WebSocket;
          window.WebSocket = function (url, protocols) {
            if (String(url).includes('irc-ws.chat.twitch.tv')) url = 'wss://127.0.0.1:9/bloqueado';
            return protocols === undefined ? new Real(url) : new Real(url, protocols);
          };
          window.WebSocket.prototype = Real.prototype;
          Object.assign(window.WebSocket, { CONNECTING: 0, OPEN: 1, CLOSING: 2, CLOSED: 3 });
        })();
        """;

    private List<TwitchChannelState> _last = [];

    public string Name => "Twitch";
    public int PageWidth => 320;
    public int PageHeight => 180;

    public string PageUrl(string channel) => "https://m.twitch.tv/" + channel;

    public async Task SetupPageAsync(BrowserHost host, CoreWebView2 core, Action<string> log)
    {
        core.Settings.UserAgent = MobileAgent;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(PageScript + NoPageChatScript);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image);
        core.AddWebResourceRequestedFilter("*amazon-adsystem.com*", CoreWebView2WebResourceContext.All);
        // a lista de qualidades da live: o player recebe só a menor (pedida também de dentro do player)
        core.AddWebResourceRequestedFilter("*usher.ttvnw.net/api/*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, e) =>
        {
            if (e.Request.Uri.Contains("usher.ttvnw.net/", StringComparison.Ordinal)) _ = LowestQualityAsync(host, e, log);
            else e.Response = host.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
        };
    }

    public async Task<IReadOnlySet<string>> ReadLiveAsync(string token, IReadOnlyList<string> channels, Action<string> log, CancellationToken ct)
    {
        _last = await TwitchGql.ReadChannelsAsync(token, channels, ct);
        return _last.Where(s => s.Live).Select(s => s.Channel).ToHashSet();
    }

    /// <summary>Da mesma consulta do <see cref="ReadLiveAsync"/> (sem pedir de novo).</summary>
    public Task<string> ReadBalancesAsync(string token, IReadOnlyList<string> watching, CancellationToken ct) =>
        Task.FromResult(string.Join(" | ", _last.Where(s => watching.Contains(s.Channel))
            .Select(s => $"{s.Channel}={s.Balance?.ToString() ?? "0"}{(s.Follows ? "" : " (não segue)")}{(s.PointsEnabled ? "" : " (canal sem pontos)")}")));

    /// <summary>Pede a lista de qualidades da live e entrega ao player só a menor (<see cref="TwitchPlaylist"/>).</summary>
    private static async Task LowestQualityAsync(BrowserHost host, CoreWebView2WebResourceRequestedEventArgs e, Action<string> log)
    {
        using var deferral = e.GetDeferral();
        try
        {
            var text = await Http.GetStringAsync(e.Request.Uri);
            var trimmed = TwitchPlaylist.KeepLowest(text);
            var bytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(trimmed));
            e.Response = host.Environment.CreateWebResourceResponse(bytes, 200, "OK",
                "Content-Type: application/vnd.apple.mpegurl\r\nAccess-Control-Allow-Origin: *");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log("lista de qualidades: " + ex.Message); // sem resposta nossa, o navegador busca sozinho
        }
    }
}
