namespace IdleDeck.Core.Twitch;

/// <summary>
/// Limite de entradas em canais da Twitch: no máximo <paramref name="limit"/> JOIN por janela
/// (20 em 10 s por usuário). Diz quanto esperar antes do próximo JOIN.
/// </summary>
public sealed class JoinScheduler(int limit = 20, TimeSpan? window = null)
{
    private readonly TimeSpan _window = window ?? TimeSpan.FromSeconds(10);
    private readonly Queue<DateTime> _recent = new();

    /// <summary>Zero se já pode entrar; senão, quanto falta para sair o JOIN mais antigo da janela.</summary>
    public TimeSpan NextDelay(DateTime now)
    {
        Trim(now);
        return _recent.Count < limit ? TimeSpan.Zero : _recent.Peek() + _window - now;
    }

    public void Record(DateTime now)
    {
        Trim(now);
        _recent.Enqueue(now);
    }

    private void Trim(DateTime now)
    {
        while (_recent.Count > 0 && now - _recent.Peek() >= _window) _recent.Dequeue();
    }
}
