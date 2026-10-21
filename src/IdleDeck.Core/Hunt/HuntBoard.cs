namespace IdleDeck.Core.Hunt;

/// <summary>O que o painel mostra de uma conta: a última mensagem, quando chegou e um aviso solto (se houver).</summary>
public sealed record HuntEntry(HuntMessage Mensagem, DateTime Recebido, string? Aviso);

/// <summary>
/// O último estado do /hunt de cada conta. Um aviso (ex.: "Esperando o jogo conectar…" quando a conta recarrega) não apaga
/// o último relatório bom, mas fica só até o próximo relatório: o relatório novo limpa o aviso.
/// </summary>
public sealed class HuntBoard
{
    private readonly Dictionary<int, HuntEntry> _entries = [];

    public IReadOnlyDictionary<int, HuntEntry> Entries => _entries;

    public bool HasReport(int accountId) => _entries.TryGetValue(accountId, out var e) && e.Mensagem is HuntReport;

    public void Apply(int accountId, HuntMessage message, DateTime now)
    {
        if (message is HuntStatus status && _entries.TryGetValue(accountId, out var previous) && previous.Mensagem is HuntReport)
            _entries[accountId] = previous with { Aviso = status.Texto }; // mantém o relatório (e a hora dele), mostra o aviso
        else
            _entries[accountId] = new HuntEntry(message, now, null);      // relatório novo (ou 1º status): nada de aviso preso
    }
}
