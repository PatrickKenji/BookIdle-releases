using System.Text.Json;

namespace IdleDeck.Core.Vote;

/// <summary>
/// O que o PRÓPRIO jogo diz sobre o voto da conta (resposta do <c>POST /topidle/me</c>): quando foi o último voto e quando
/// libera de novo. É a verdade da conta, vale mesmo para votos feitos fora do app, e o jogo libera um pouco antes de 24 h.
/// </summary>
public sealed record TopIdleStatus(DateTime? LastVoteUtc, DateTime? ReleaseUtc)
{
    /// <summary>
    /// A data de "último voto" que faz o <see cref="VoteCooldown"/> do app dar o mesmo resultado do jogo:
    /// o horário de liberação menos o intervalo (ou o próprio último voto, se o jogo não disse a liberação).
    /// </summary>
    public DateTime EffectiveLastVoteUtc => ReleaseUtc is { } release ? release - VoteCooldown.Interval : LastVoteUtc!.Value;

    /// <summary>Null se a resposta não traz nada útil (nunca votou, formato estranho).</summary>
    public static TopIdleStatus? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var last = Time(doc.RootElement, "ultimoVotoEm");
            var release = Time(doc.RootElement, "liberaEm");
            return last is null && release is null ? null : new TopIdleStatus(last, release);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A data de "último voto" que o app deve guardar, dado o que já tinha. Vale a do jogo, exceto quando o app anotou
    /// um voto MAIS NOVO que o jogo ainda não mostrou (o status que chegou é velho).
    /// </summary>
    public DateTime Reconcile(DateTime? stored)
    {
        var effective = EffectiveLastVoteUtc;
        if (stored is not { } local) return effective;
        var seenByGame = LastVoteUtc ?? effective;
        return seenByGame >= local - TimeSpan.FromMinutes(10) ? effective : local;
    }

    private static DateTime? Time(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var ms) && ms > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
            : null;
}
