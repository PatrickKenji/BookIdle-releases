namespace IdleDeck.Core.Vote;

/// <summary>Voto no site do jogo (IdleHub, TopIdle…): o intervalo de 24 h e a contagem que o menu mostra.</summary>
public static class VoteCooldown
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static string Plain(string site) => Loc.T("Votar no {0}", site);

    /// <summary>
    /// Quanto falta para poder votar de novo. Zero se nunca votou ou se já passou o intervalo;
    /// nunca passa de 24 h, mesmo que o relógio tenha andado para trás (voto "no futuro").
    /// </summary>
    public static TimeSpan Remaining(DateTime? lastVoteUtc, DateTime nowUtc)
    {
        if (lastVoteUtc is not { } last) return TimeSpan.Zero;
        var remaining = Interval - (nowUtc - last);
        if (remaining <= TimeSpan.Zero) return TimeSpan.Zero;
        return remaining > Interval ? Interval : remaining;
    }

    /// <summary>Texto curto para o botão da conta: "Votar no IdleHub" quando já pode, "Votar em 5 h" / "Votar em 40 min" quando não.</summary>
    public static string ShortLabel(string site, DateTime? lastVoteUtc, DateTime nowUtc)
    {
        var remaining = Remaining(lastVoteUtc, nowUtc);
        if (remaining == TimeSpan.Zero) return Plain(site);
        if (remaining < TimeSpan.FromHours(1)) return Loc.T("Votar em {0:0} min", Math.Ceiling(remaining.TotalMinutes));
        return Loc.T("Votar em {0:0} h", Math.Ceiling(remaining.TotalHours));
    }

    /// <summary>"Votar no IdleHub", "Votar no IdleHub (em 5 h)" (horas para cima) ou "(em 40 min)".</summary>
    public static string Label(string site, DateTime? lastVoteUtc, DateTime nowUtc)
    {
        var remaining = Remaining(lastVoteUtc, nowUtc);
        if (remaining == TimeSpan.Zero) return Plain(site);
        if (remaining < TimeSpan.FromHours(1)) return Loc.T("Votar no {0} (em {1:0} min)", site, Math.Ceiling(remaining.TotalMinutes));
        return Loc.T("Votar no {0} (em {1:0} h)", site, Math.Ceiling(remaining.TotalHours));
    }
}
