using System.Net.Http;
using System.Text.Json;

namespace IdleDeck.Core.Kick;

/// <summary>
/// Leitura (só leitura) da Kick pela mesma API que o site usa: quem está ao vivo (público), o saldo de pontos da conta num
/// canal e o nome de usuário (com a sessão da conta).
/// </summary>
public static class KickApi
{
    private const string Agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly TimeSpan LiveCacheFor = TimeSpan.FromSeconds(45);
    private static readonly Dictionary<string, (DateTime At, bool Live)> LiveCache = [];
    private static readonly SemaphoreSlim LiveGate = new(1, 1);

    /// <summary>Resposta de <c>/api/v2/channels/&lt;canal&gt;</c>: ao vivo quando <c>livestream</c> é um objeto.</summary>
    public static bool ParseLive(string json)
    {
        using var doc = Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("slug", out _))
            throw new FormatException("Resposta da Kick sem o canal: " + Short(json));
        return root.TryGetProperty("livestream", out var ls) && ls.ValueKind == JsonValueKind.Object;
    }

    /// <summary>Resposta de <c>/api/v2/channels/&lt;canal&gt;/points</c>; null sem sessão/sem dados.</summary>
    public static int? ParsePoints(string json)
    {
        using var doc = Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Object
               && doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
               && data.TryGetProperty("points", out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetInt32() : null;
    }

    /// <summary>Resposta de <c>/api/v1/user</c>; null sem sessão. (A resposta traz o e-mail: só o nome sai daqui.)</summary>
    public static string? ParseUsername(string json)
    {
        using var doc = Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Object
               && doc.RootElement.TryGetProperty("username", out var u) && u.ValueKind == JsonValueKind.String
            ? u.GetString() : null;
    }

    /// <summary>
    /// Quais canais estão ao vivo. A resposta de cada canal vale por 45 s para todas as contas (a pergunta é pública).
    /// Erro em um canal: ele conta como fora do ar nesta rodada e o erro vai para <paramref name="errors"/>.
    /// </summary>
    public static async Task<HashSet<string>> ReadLiveAsync(IReadOnlyList<string> channels, List<string> errors, CancellationToken ct = default)
    {
        var live = new HashSet<string>();
        await LiveGate.WaitAsync(ct);
        try
        {
            foreach (var channel in channels)
            {
                if (LiveCache.TryGetValue(channel, out var cached) && DateTime.UtcNow - cached.At < LiveCacheFor)
                {
                    if (cached.Live) live.Add(channel);
                    continue;
                }
                try
                {
                    var isLive = ParseLive(await GetAsync($"https://kick.com/api/v2/channels/{channel}", null, ct));
                    LiveCache[channel] = (DateTime.UtcNow, isLive);
                    if (isLive) live.Add(channel);
                }
                catch (Exception e) when (e is HttpRequestException or FormatException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    errors.Add($"{channel}: {e.Message}");
                }
            }
        }
        finally { LiveGate.Release(); }
        return live;
    }

    public static async Task<int?> ReadPointsAsync(string token, string channel, CancellationToken ct = default) =>
        ParsePoints(await GetAsync($"https://kick.com/api/v2/channels/{channel}/points", token, ct));

    public static async Task<string?> ReadUsernameAsync(string token, CancellationToken ct = default) =>
        ParseUsername(await GetAsync("https://kick.com/api/v1/user", token, ct));

    private static async Task<string> GetAsync(string url, string? token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", Agent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (token is not null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        using var response = await Http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if ((int)response.StatusCode is 401 or 403 && token is not null && !text.TrimStart().StartsWith('<'))
            return text; // sem sessão: o chamador vê "sem dados"
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Kick respondeu {(int)response.StatusCode}");
        return text;
    }

    private static JsonDocument Parse(string json)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException e) { throw new FormatException("Resposta da Kick inválida: " + Short(json), e); }
    }

    private static string Short(string s) => s.Length > 120 ? s[..120] : s;
}
