using IdleDeck.Core.Hunt;

namespace IdleDeck.Core.Tests;

public class HuntBoardTests
{
    private static readonly DateTime T0 = new(2026, 10, 21, 14, 0, 0);

    private static HuntReport Report(string? dia = null) =>
        new(T0, "1.345.4", 400, null, 1, 1, null, null, 0.3, null, [], [], dia);

    [Fact]
    public void A_status_after_a_report_keeps_the_report_and_shows_the_notice()
    {
        var board = new HuntBoard();
        var report = Report();
        board.Apply(1, report, T0);
        board.Apply(1, new HuntStatus("Esperando o jogo conectar…"), T0.AddMinutes(5));

        var entry = board.Entries[1];
        Assert.Same(report, entry.Mensagem);
        Assert.Equal(T0, entry.Recebido);                      // o relatório continua com a hora dele
        Assert.Equal("Esperando o jogo conectar…", entry.Aviso);
    }

    [Fact]
    public void A_new_report_clears_the_old_notice()
    {
        var board = new HuntBoard();
        board.Apply(1, Report(), T0);
        board.Apply(1, new HuntStatus("Esperando o jogo conectar…"), T0.AddMinutes(1));
        var novo = Report("FIRE");
        board.Apply(1, novo, T0.AddMinutes(2));

        var entry = board.Entries[1];
        Assert.Same(novo, entry.Mensagem);
        Assert.Equal(T0.AddMinutes(2), entry.Recebido);
        Assert.Null(entry.Aviso);                              // era o defeito: o aviso ficava preso para sempre
    }

    [Fact]
    public void A_status_with_no_report_yet_is_just_the_status()
    {
        var board = new HuntBoard();
        board.Apply(1, new HuntStatus("Coloque um pokémon em campo"), T0);

        var entry = board.Entries[1];
        Assert.Equal("Coloque um pokémon em campo", Assert.IsType<HuntStatus>(entry.Mensagem).Texto);
        Assert.Null(entry.Aviso);
    }

    [Fact]
    public void A_report_replaces_a_status()
    {
        var board = new HuntBoard();
        board.Apply(1, new HuntStatus("Esperando o jogo conectar…"), T0);
        board.Apply(1, Report(), T0.AddSeconds(3));

        Assert.IsType<HuntReport>(board.Entries[1].Mensagem);
        Assert.Null(board.Entries[1].Aviso);
    }

    [Fact]
    public void Accounts_do_not_interfere_and_HasReport_tells_who_has_one()
    {
        var board = new HuntBoard();
        board.Apply(1, Report(), T0);
        board.Apply(2, new HuntStatus("Esperando o jogo conectar…"), T0);

        Assert.True(board.HasReport(1));
        Assert.False(board.HasReport(2));
        Assert.False(board.HasReport(3));
        Assert.Null(board.Entries[1].Aviso);
    }

    [Fact]
    public void A_second_status_replaces_the_notice()
    {
        var board = new HuntBoard();
        board.Apply(1, Report(), T0);
        board.Apply(1, new HuntStatus("Esperando o jogo conectar…"), T0.AddMinutes(1));
        board.Apply(1, new HuntStatus("Coloque um pokémon em campo"), T0.AddMinutes(2));

        Assert.Equal("Coloque um pokémon em campo", board.Entries[1].Aviso);
        Assert.IsType<HuntReport>(board.Entries[1].Mensagem);
    }
}
