namespace IdleDeck.Core.Lives;

/// <summary>Uma conta com a plataforma conectada: o nome que aparece na coluna, o saldo por canal e o que ela assiste agora.</summary>
public sealed record LiveBoardAccount(string Name, IReadOnlyDictionary<string, int?> Balances, IReadOnlyCollection<string> Watching);

/// <summary>Uma linha da tabela: o canal, se está ao vivo, e por conta (na ordem das colunas) o saldo e se ela assiste.</summary>
public sealed record LiveBoardRow(string Channel, bool Live, IReadOnlyList<LiveBoardCell> Cells);

public sealed record LiveBoardCell(int? Balance, bool Watching);

/// <summary>
/// A tabela da janela "Lives" (só leitura): uma linha por canal da lista do perfil, na ordem dela (a prioridade), e uma
/// coluna por conta. O total soma os saldos conhecidos de cada conta.
/// </summary>
public sealed record LiveBoard(IReadOnlyList<string> Accounts, IReadOnlyList<LiveBoardRow> Rows, IReadOnlyList<int> Totals)
{
    public static LiveBoard Build(IReadOnlyList<string> channels, IReadOnlySet<string> live, IReadOnlyList<LiveBoardAccount> accounts)
    {
        var rows = channels.Select(c => new LiveBoardRow(c, live.Contains(c),
            accounts.Select(a => new LiveBoardCell(a.Balances.TryGetValue(c, out var b) ? b : null, a.Watching.Contains(c))).ToList())).ToList();
        var totals = accounts.Select(a => channels.Sum(c => a.Balances.TryGetValue(c, out var b) ? b ?? 0 : 0)).ToList();
        return new LiveBoard(accounts.Select(a => a.Name).ToList(), rows, totals);
    }
}
