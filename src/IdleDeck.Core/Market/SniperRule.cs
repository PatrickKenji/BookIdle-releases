namespace IdleDeck.Core.Market;

public enum RuleKind { Item, Pokemon, AnyShiny, AnyP5, MinNota }

/// <summary>
/// Uma regra de compra. Teto 0 em uma moeda = não compra naquela moeda. <see cref="MaxQty"/> 0 = sem limite.
/// <see cref="Bought"/> conta as unidades já compradas por esta regra.
/// </summary>
public class SniperRule
{
    public string Id { get; set; } = "";
    public RuleKind Kind { get; set; }
    public int? ItemId { get; set; }
    public int? SpeciesId { get; set; }
    public string Label { get; set; } = "";
    public long MaxGold { get; set; }
    public long MaxOrb { get; set; }
    public double MinNota { get; set; }
    public int MaxQty { get; set; }
    public int Bought { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>O sniper de uma conta: chave geral, reserva de saldo e regras.</summary>
public class SniperAccountConfig
{
    public bool Enabled { get; set; }
    public long ReserveGold { get; set; }
    public long ReserveOrb { get; set; }
    public List<SniperRule> Rules { get; set; } = [];
}

public static class SniperRules
{
    /// <summary>Máximo de regras de Item/Pokémon por conta (os radares têm lugar à parte, um de cada).</summary>
    public const int MaxRules = 15;

    private static bool IsRadar(RuleKind kind) => kind is RuleKind.AnyShiny or RuleKind.AnyP5 or RuleKind.MinNota;

    /// <summary>Null se a regra está certa; senão o motivo, em português.</summary>
    public static string? Validate(SniperRule rule)
    {
        if (rule.MaxGold < 0 || rule.MaxOrb < 0) return Loc.T("Os tetos não podem ser negativos.");
        if (rule.MaxQty < 0) return Loc.T("A quantidade máxima não pode ser negativa.");
        var hasCeiling = rule.MaxGold > 0 || rule.MaxOrb > 0;
        switch (rule.Kind)
        {
            case RuleKind.Item:
                if (rule.ItemId is not > 0) return Loc.T("Escolha um item.");
                return hasCeiling ? null : Loc.T("Defina um teto em coins ou em gemas.");
            case RuleKind.Pokemon:
                if (rule.SpeciesId is not > 0) return Loc.T("Escolha um Pokémon.");
                return hasCeiling ? null : Loc.T("Defina um teto em coins ou em gemas.");
            case RuleKind.AnyShiny:
                if (rule.MaxGold > 0) return Loc.T("Shiny só se compra com gemas.");
                return rule.MaxOrb > 0 ? null : Loc.T("Defina o teto em gemas.");
            case RuleKind.AnyP5:
                return hasCeiling ? null : Loc.T("Defina um teto em coins ou em gemas.");
            case RuleKind.MinNota:
                if (!(rule.MinNota > 0)) return Loc.T("Defina a nota mínima.");
                return hasCeiling ? null : Loc.T("Defina um teto em coins ou em gemas.");
            default:
                return Loc.T("Tipo de regra desconhecido.");
        }
    }

    /// <summary>Null se a regra pode entrar na conta; senão o motivo.</summary>
    public static string? CanAdd(SniperAccountConfig config, SniperRule rule)
    {
        if (Validate(rule) is { } invalid) return invalid;
        if (IsRadar(rule.Kind))
            return config.Rules.Any(r => r.Kind == rule.Kind) ? Loc.T("Esse radar já existe nesta conta.") : null;
        return config.Rules.Count(r => !IsRadar(r.Kind)) >= MaxRules ? Loc.T("No máximo {0} regras por conta.", MaxRules) : null;
    }

    /// <summary>Unidades que a regra ainda pode comprar (<see cref="long.MaxValue"/> se não tem limite).</summary>
    public static long Remaining(SniperRule rule) =>
        rule.MaxQty <= 0 ? long.MaxValue : Math.Max(0, (long)rule.MaxQty - Math.Max(0, rule.Bought));

    /// <summary>Deixa uma conta carregada de arquivo dentro das regras (arquivo editado à mão, versão antiga…).</summary>
    public static void Normalize(SniperAccountConfig config)
    {
        config.ReserveGold = Math.Max(0, config.ReserveGold);
        config.ReserveOrb = Math.Max(0, config.ReserveOrb);

        var kept = new List<SniperRule>();
        var ids = new HashSet<string>();
        foreach (var rule in config.Rules ?? [])
        {
            if (rule is null || Validate(rule) is not null) continue;
            if (IsRadar(rule.Kind) ? kept.Any(r => r.Kind == rule.Kind) : kept.Count(r => !IsRadar(r.Kind)) >= MaxRules) continue;
            rule.Bought = Math.Max(0, rule.Bought);
            if (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id))
            {
                do rule.Id = Guid.NewGuid().ToString("N")[..8];
                while (!ids.Add(rule.Id));
            }
            rule.Label ??= "";
            kept.Add(rule);
        }
        config.Rules = kept;
    }
}
