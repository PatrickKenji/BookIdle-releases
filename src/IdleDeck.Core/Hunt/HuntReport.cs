using System.Text.Json;

namespace IdleDeck.Core.Hunt;

public enum HuntRisk { Seguro, Arriscado, UmHit }

/// <summary>Uma hunt no ranking de uma conta (valores por hora).</summary>
public record HuntLine(
    string Slug, string Nome, int Nivel, string? Regiao, bool Liberada, bool Atual, bool Inviavel,
    double KillsH, double LutaS, double XpPokemonH, double XpTreinadorH, double OuroH, double LiquidoH,
    double CustoPocoesH, double CustoRevivesH, double CustoBolasH, double PocoesH, double MortesH,
    HuntRisk Risco, double MaiorGolpePct, double DanoPorAbatePct, string? Golpe);

public record HuntPokemon(string? Nome, int Level, string[] Tipos, double HpMax, string? ZMove, bool Aoe, int AoeAlvos);

/// <summary>O que o script do /hunt manda de uma conta: um relatório ou um aviso.</summary>
public abstract record HuntMessage
{
    /// <summary>Lê o <c>data</c> de <c>{ module: 'hunt', data }</c>. Devolve null se não for reconhecível.</summary>
    public static HuntMessage? Parse(JsonElement data)
    {
        try
        {
            if (data.ValueKind != JsonValueKind.Object) return null;
            return Str(data, "tipo") switch
            {
                "status" => new HuntStatus(Str(data, "texto") ?? ""),
                "relatorio" => HuntReport.From(data),
                _ => null,
            };
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or KeyNotFoundException)
        {
            return null;
        }
    }

    internal static string? Str(JsonElement o, string nome) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static double Num(JsonElement o, string nome) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    internal static bool Bool(JsonElement o, string nome) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.True;

    internal static bool Obj(JsonElement o, string nome, out JsonElement v) =>
        o.TryGetProperty(nome, out v) && v.ValueKind == JsonValueKind.Object;
}

public record HuntStatus(string Texto) : HuntMessage;

public record HuntReport(
    DateTime GeradoEm, string? VersaoJogo, int TreinadorLevel, HuntPokemon? Pokemon,
    double BonusXpPokemon, double BonusXpTreinador, string? BikeRaridade, string? Pocao, double Limiar,
    string? HuntAtual, IReadOnlyList<HuntLine> PorXp, IReadOnlyList<HuntLine> PorOuro,
    string? DiaSazonal = null, bool HeroiDoDia = false) : HuntMessage
{
    /// <summary>O nome do tipo em português (o jogo manda em inglês: FIRE, WATER…).</summary>
    public static string TipoEmPortugues(string tipo) => tipo switch
    {
        "NORMAL" => "Normal", "FIRE" => "Fogo", "WATER" => "Água", "GRASS" => "Planta", "ELECTRIC" => "Elétrico",
        "ICE" => "Gelo", "FIGHTING" => "Lutador", "POISON" => "Veneno", "GROUND" => "Terra", "FLYING" => "Voador",
        "PSYCHIC" => "Psíquico", "BUG" => "Inseto", "ROCK" => "Pedra", "GHOST" => "Fantasma", "DRAGON" => "Dragão",
        "DARK" => "Sombrio", "STEEL" => "Aço", "FAIRY" => "Fada", _ => tipo,
    };

    internal static HuntReport? From(JsonElement d)
    {
        var porXp = Lines(d, "porXp");
        var porOuro = Lines(d, "porOuro");
        if (porXp is null || porOuro is null) return null;

        HuntPokemon? pokemon = null;
        if (Obj(d, "pokemon", out var p))
        {
            var tipos = p.TryGetProperty("tipos", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
                : [];
            pokemon = new HuntPokemon(Str(p, "nome"), (int)Num(p, "level"), tipos, Num(p, "hpMax"), Str(p, "zMove"),
                Bool(p, "aoe"), Math.Max(1, (int)Num(p, "aoeAlvos")));
        }

        var gerado = Num(d, "geradoEm");
        Obj(d, "treinador", out var tr);
        Obj(d, "bonusXp", out var bonus);
        Obj(d, "bike", out var bike);
        return new HuntReport(
            gerado > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)gerado).UtcDateTime : DateTime.UtcNow,
            Str(d, "versaoJogo"),
            (int)Num(tr, "level"),
            pokemon,
            Num(bonus, "pokemon"), Num(bonus, "treinador"),
            Str(bike, "raridade"),
            Str(d, "pocao"),
            Num(d, "limiar"),
            Str(d, "huntAtual"),
            porXp, porOuro,
            Str(d, "diaSazonal"), Bool(d, "heroiDoDia"));
    }

    /// <summary>null quando o campo existe mas não é lista (mensagem inválida); vazio quando falta.</summary>
    private static List<HuntLine>? Lines(JsonElement d, string nome)
    {
        if (!d.TryGetProperty(nome, out var arr)) return [];
        if (arr.ValueKind != JsonValueKind.Array) return null;
        return arr.EnumerateArray().Where(l => l.ValueKind == JsonValueKind.Object).Select(l => new HuntLine(
            Str(l, "slug") ?? "", Str(l, "nome") ?? "", (int)Num(l, "nivel"), Str(l, "regiao"),
            Bool(l, "liberada"), Bool(l, "atual"), Bool(l, "inviavel"),
            Num(l, "killsH"), Num(l, "lutaS"), Num(l, "xpPokemonH"), Num(l, "xpTreinadorH"), Num(l, "ouroH"), Num(l, "liquidoH"),
            Num(l, "custoPocoesH"), Num(l, "custoRevivesH"), Num(l, "custoBolasH"), Num(l, "pocoesH"), Num(l, "mortesH"),
            Str(l, "risco") switch { "umHit" => HuntRisk.UmHit, "arriscado" => HuntRisk.Arriscado, _ => HuntRisk.Seguro },
            Num(l, "maiorGolpePct"), Num(l, "danoPorAbatePct"), Str(l, "golpe"))).ToList();
    }
}
