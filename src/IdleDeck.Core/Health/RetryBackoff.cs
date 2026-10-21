namespace IdleDeck.Core.Health;

/// <summary>Espera crescente entre tentativas: 5 s, dobrando, até 60 s.</summary>
public class RetryBackoff
{
    private static readonly TimeSpan First = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Max = TimeSpan.FromSeconds(60);

    private TimeSpan _next = First;

    public TimeSpan NextDelay()
    {
        var current = _next;
        _next = TimeSpan.FromTicks(Math.Min(_next.Ticks * 2, Max.Ticks));
        return current;
    }

    public void Reset() => _next = First;
}
