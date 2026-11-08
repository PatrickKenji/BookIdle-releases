namespace IdleDeck.Core.Twitch;

/// <summary>
/// Quais lives cada conta assiste para juntar pontos do canal. A Twitch e a Kick só dão pontos em 2 canais ao mesmo
/// tempo por conta (medido em 2026-11-07/08), então são 2 vagas. A ordem da lista do perfil é a prioridade: as vagas são
/// sempre dos 2 primeiros canais ao vivo. Um canal mais acima que entra ao vivo tira na hora o de baixo (a pessoa põe
/// em primeiro os canais em que é sub, que rendem o dobro); um que encerra a live dá a vaga ao próximo ao vivo.
/// </summary>
public static class TwitchWatchPlan
{
    public const int Slots = 2;

    public static List<string> Pick(IReadOnlyList<string> priority, IReadOnlySet<string> live, int slots = Slots) =>
        priority.Where(live.Contains).Take(slots).ToList();
}
