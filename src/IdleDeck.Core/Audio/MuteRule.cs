namespace IdleDeck.Core.Audio;

public static class MuteRule
{
    /// <summary>
    /// Estado que o botão de mudo geral aplica a todas as contas: se todas já estão mudas, tira o mudo de todas;
    /// senão, muta todas (inclusive sem nenhuma conta).
    /// </summary>
    public static bool TargetState(IEnumerable<bool> mutedStates)
    {
        var states = mutedStates.ToList();
        return states.Count == 0 || states.Any(muted => !muted);
    }
}
