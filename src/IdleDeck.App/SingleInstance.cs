namespace IdleDeck.App;

/// <summary>
/// Garante uma única instância: abrir o .exe de novo só traz a janela existente para frente,
/// em vez de abrir outro navegador sobre os mesmos perfis.
/// </summary>
public static class SingleInstance
{
    // Em instância de teste (BOOKIDLE_HOME definido) o sufixo deixa abrir ao lado da instância real.
    private static readonly string Suffix = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(IdleDeck.Core.AppPaths.HomeVariable)) ? "" : ".test";
    private static readonly string MutexName = @"Local\BookIdle" + Suffix;
    private static readonly string ShowEventName = @"Local\BookIdle.Show" + Suffix;

    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            return false;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        return true;
    }

    /// <summary>Libera a instância (antes de abrir o app de novo ao reiniciar).</summary>
    public static void Release()
    {
        _showEvent?.Dispose();
        _showEvent = null;
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mutex = null;
    }

    /// <summary>Chama <paramref name="onShowRequested"/> (fora da thread de UI) quando outra execução pede para mostrar a janela.</summary>
    public static void ListenForShowRequests(Action onShowRequested)
    {
        if (_showEvent is null) return;
        ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
    }
}
