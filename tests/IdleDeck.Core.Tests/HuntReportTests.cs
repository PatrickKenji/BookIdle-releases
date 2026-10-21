using System.Text.Json;
using IdleDeck.Core.Hunt;

namespace IdleDeck.Core.Tests;

public class HuntReportTests
{
    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();

    private const string Relatorio = """
        {
          "tipo": "relatorio", "versaoJogo": "1.337.0", "geradoEm": 1792540000000,
          "treinador": { "nick": "ash", "level": 512 },
          "pokemon": { "nome": "Shiny Charizard", "level": 612, "tipos": ["FIRE","FLYING"], "hpMax": 9000,
                       "zMove": "FIRE", "aoe": true, "aoeAlvos": 2 },
          "bonusXp": { "pokemon": 2.25, "treinador": 1.5 },
          "bike": { "raridade": "lendaria", "fator": 2 },
          "pocao": "Golden Potion", "limiar": 0.3, "huntAtual": "shedinja",
          "porXp": [
            { "slug": "shedinja", "nome": "Shedinja", "nivel": 500, "regiao": "hoenn", "liberada": true, "atual": true,
              "inviavel": false, "killsH": 1000, "lutaS": 1.5, "xpPokemonH": 900000, "xpTreinadorH": 600000,
              "ouroH": 50000, "liquidoH": 42000, "custoPocoesH": 8000, "custoRevivesH": 0, "custoBolasH": 0,
              "pocoesH": 1.6, "mortesH": 0, "risco": "seguro", "maiorGolpePct": 0.12, "danoPorAbatePct": 0.05,
              "golpe": "Z-Move FIRE" }
          ],
          "porOuro": []
        }
        """;

    [Fact]
    public void Parses_full_report()
    {
        var msg = HuntMessage.Parse(Json(Relatorio));

        var r = Assert.IsType<HuntReport>(msg);
        Assert.Equal("1.337.0", r.VersaoJogo);
        Assert.Equal(512, r.TreinadorLevel);
        Assert.Equal("Shiny Charizard", r.Pokemon!.Nome);
        Assert.Equal(612, r.Pokemon.Level);
        Assert.Equal(["FIRE", "FLYING"], r.Pokemon.Tipos);
        Assert.True(r.Pokemon.Aoe);
        Assert.Equal(2, r.Pokemon.AoeAlvos);
        Assert.Equal("FIRE", r.Pokemon.ZMove);
        Assert.Equal(2.25, r.BonusXpPokemon);
        Assert.Equal("lendaria", r.BikeRaridade);
        Assert.Equal("Golden Potion", r.Pocao);
        Assert.Equal("shedinja", r.HuntAtual);
        var l = Assert.Single(r.PorXp);
        Assert.Equal("Shedinja", l.Nome);
        Assert.True(l.Atual);
        Assert.Equal(900000, l.XpPokemonH);
        Assert.Equal(42000, l.LiquidoH);
        Assert.Equal(8000, l.CustoPocoesH);
        Assert.Equal(HuntRisk.Seguro, l.Risco);
        Assert.Equal("Z-Move FIRE", l.Golpe);
        Assert.Empty(r.PorOuro);
        Assert.Equal(new DateTime(2026, 10, 20, 23, 46, 40, DateTimeKind.Utc), r.GeradoEm);
    }

    [Fact]
    public void Parses_status()
    {
        var msg = HuntMessage.Parse(Json("""{ "tipo": "status", "texto": "Coloque um pokémon em campo" }"""));

        Assert.Equal("Coloque um pokémon em campo", Assert.IsType<HuntStatus>(msg).Texto);
    }

    [Fact]
    public void Missing_fields_use_safe_defaults()
    {
        var msg = HuntMessage.Parse(Json("""{ "tipo": "relatorio", "porXp": [ { "slug": "x", "nome": "X", "risco": "umHit" } ] }"""));

        var r = Assert.IsType<HuntReport>(msg);
        Assert.Null(r.Pokemon);
        Assert.Equal(0, r.TreinadorLevel);
        Assert.Equal(HuntRisk.UmHit, r.PorXp[0].Risco);
        Assert.Empty(r.PorOuro);
    }

    [Theory]
    [InlineData("""{ "tipo": "outro" }""")]
    [InlineData("""[1,2,3]""")]
    [InlineData("""{ "tipo": "relatorio", "porXp": "nao-e-lista" }""")]
    public void Unknown_or_malformed_returns_null(string json) =>
        Assert.Null(HuntMessage.Parse(Json(json)));

    [Fact]
    public void Reads_the_seasonal_day_type_and_whether_the_pokemon_is_of_that_type()
    {
        var comDia = Relatorio.Replace("\"huntAtual\": \"shedinja\",", "\"huntAtual\": \"shedinja\", \"diaSazonal\": \"FIRE\", \"heroiDoDia\": true,");
        var r = Assert.IsType<HuntReport>(HuntMessage.Parse(Json(comDia)));
        Assert.Equal("FIRE", r.DiaSazonal);
        Assert.True(r.HeroiDoDia);
    }

    [Fact]
    public void A_report_without_the_seasonal_day_has_no_type()
    {
        var r = Assert.IsType<HuntReport>(HuntMessage.Parse(Json(Relatorio)));
        Assert.Null(r.DiaSazonal);
        Assert.False(r.HeroiDoDia);
    }

    [Theory]
    [InlineData("FIRE", "Fogo")]
    [InlineData("WATER", "Água")]
    [InlineData("PSYCHIC", "Psíquico")]
    [InlineData("DARK", "Sombrio")]
    [InlineData("FAIRY", "Fada")]
    [InlineData("OUTRO", "OUTRO")]
    public void Type_names_are_shown_in_portuguese(string tipo, string nome)
    {
        Assert.Equal(nome, HuntReport.TipoEmPortugues(tipo));
    }
}
