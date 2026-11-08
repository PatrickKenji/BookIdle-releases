using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace IdleDeck.Core.Twitch;

/// <summary>Um canal visto pela conta: ao vivo, se ela segue, e o saldo de pontos dela ali (null se nunca juntou).</summary>
public sealed record TwitchChannelState(string Channel, bool Live, bool Follows, int? Balance, bool PointsEnabled);

/// <summary>
/// Leitura (só leitura) do estado dos canais pela API do próprio site da Twitch, com a chave da conta.
/// </summary>
public static class TwitchGql
{
    /// <summary>O cliente do próprio site da Twitch (o mesmo que a página usa).</summary>
    public const string ClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko";
    private const int MaxPerQuery = 50;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static string ChannelsQuery(IReadOnlyList<string> channels) =>
        "query{users(logins:" + JsonSerializer.Serialize(channels)
        + "){login self{follower{followedAt}} stream{id} channel{communityPointsSettings{isEnabled} self{communityPoints{balance}}}}}";

    /// <summary>Canais que não existem (null na resposta) ficam de fora.</summary>
    public static List<TwitchChannelState> ParseChannels(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("users", out var users) || users.ValueKind != JsonValueKind.Array)
            throw new FormatException("Resposta da Twitch sem dados: " + json[..Math.Min(200, json.Length)]);

        var result = new List<TwitchChannelState>();
        foreach (var u in users.EnumerateArray())
        {
            if (u.ValueKind != JsonValueKind.Object || !u.TryGetProperty("login", out var l) || l.GetString() is not { } login) continue;
            var live = u.TryGetProperty("stream", out var s) && s.ValueKind == JsonValueKind.Object;
            var follows = u.TryGetProperty("self", out var self) && self.ValueKind == JsonValueKind.Object
                && self.TryGetProperty("follower", out var f) && f.ValueKind == JsonValueKind.Object;
            int? balance = null;
            var enabled = true;
            if (u.TryGetProperty("channel", out var ch) && ch.ValueKind == JsonValueKind.Object)
            {
                if (ch.TryGetProperty("self", out var cs) && cs.ValueKind == JsonValueKind.Object
                    && cs.TryGetProperty("communityPoints", out var cp) && cp.ValueKind == JsonValueKind.Object
                    && cp.TryGetProperty("balance", out var b) && b.ValueKind == JsonValueKind.Number)
                    balance = b.GetInt32();
                if (ch.TryGetProperty("communityPointsSettings", out var cps) && cps.ValueKind == JsonValueKind.Object
                    && cps.TryGetProperty("isEnabled", out var en) && en.ValueKind == JsonValueKind.False)
                    enabled = false;
            }
            result.Add(new TwitchChannelState(login, live, follows, balance, enabled));
        }
        return result;
    }

    /// <summary>Estado de todos os canais (em lotes). Erro de rede/resposta: <see cref="HttpRequestException"/> ou <see cref="FormatException"/>.</summary>
    public static async Task<List<TwitchChannelState>> ReadChannelsAsync(string token, IReadOnlyList<string> channels, CancellationToken ct = default)
    {
        var result = new List<TwitchChannelState>();
        foreach (var batch in channels.Chunk(MaxPerQuery))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://gql.twitch.tv/gql")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { query = ChannelsQuery(batch) }), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("Client-Id", ClientId);
            request.Headers.Add("Authorization", "OAuth " + token);
            using var response = await Http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Twitch respondeu {(int)response.StatusCode}");
            try { result.AddRange(ParseChannels(text)); }
            catch (JsonException e) { throw new FormatException("Resposta da Twitch inválida.", e); }
        }
        return result;
    }
}
