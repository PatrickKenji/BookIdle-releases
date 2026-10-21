namespace IdleDeck.Core.Health;

/// <summary>
/// Decide quando recarregar uma conta por excesso de memória, com intervalo mínimo
/// entre resets da mesma conta para não entrar em loop.
/// </summary>
public class MemoryResetPolicy(TimeSpan cooldown)
{
    private readonly Dictionary<int, DateTime> _lastReset = [];

    public bool ShouldAutoReset(int accountIndex, long privateBytes, int limitMb, bool enabled, DateTime nowUtc)
    {
        if (!enabled || privateBytes <= limitMb * 1024L * 1024L) return false;
        return !_lastReset.TryGetValue(accountIndex, out var last) || nowUtc - last >= cooldown;
    }

    public void RecordReset(int accountIndex, DateTime nowUtc) => _lastReset[accountIndex] = nowUtc;
}
