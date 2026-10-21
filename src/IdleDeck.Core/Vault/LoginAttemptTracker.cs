namespace IdleDeck.Core.Vault;

/// <summary>
/// Conta tentativas de login automático falhas por conta. Uma tentativa falhou se o
/// formulário de login reaparece até 60 s depois dela. Após <c>maxFailures</c> falhas
/// seguidas a conta fica bloqueada até <see cref="RecordLoggedIn"/> ou <see cref="Clear"/>.
/// </summary>
public class LoginAttemptTracker(int maxFailures = 3)
{
    private static readonly TimeSpan FailureWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(15);

    private readonly Dictionary<int, State> _states = [];

    private sealed class State
    {
        public int Failures;
        public DateTime? PendingAttempt;
        public DateTime? LastAttempt;
    }

    private State Get(int account) =>
        _states.TryGetValue(account, out var s) ? s : _states[account] = new State();

    public bool CanAttempt(int account) => CanAttempt(account, DateTime.UtcNow);

    public bool CanAttempt(int account, DateTime nowUtc)
    {
        var s = Get(account);
        if (s.Failures >= maxFailures) return false;
        return s.LastAttempt is not { } last || nowUtc - last >= MinInterval;
    }

    public void RecordAttempt(int account, DateTime nowUtc)
    {
        var s = Get(account);
        s.PendingAttempt = nowUtc;
        s.LastAttempt = nowUtc;
    }

    public void RecordLoginFormSeen(int account, DateTime nowUtc)
    {
        var s = Get(account);
        if (s.PendingAttempt is { } attempt && nowUtc - attempt <= FailureWindow)
            s.Failures++;
        s.PendingAttempt = null;
    }

    public void RecordLoggedIn(int account)
    {
        var s = Get(account);
        s.Failures = 0;
        s.PendingAttempt = null;
    }

    public void Clear(int account) => _states.Remove(account);

    public bool IsLockedOut(int account) => Get(account).Failures >= maxFailures;
}
