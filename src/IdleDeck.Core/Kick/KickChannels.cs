using System.Text.RegularExpressions;

namespace IdleDeck.Core.Kick;

public static partial class KickChannels
{
    /// <summary>Canais oficiais do PokeIdle na Kick (lista do jogo, 2026-11-08). Sugestão inicial da lista do perfil.</summary>
    public static readonly IReadOnlyList<string> Partners =
    [
        "axt", "surskity", "denissnider", "guaxilol1", "fnblol", "daniels", "yetz", "patopapao2", "rodil",
        "clovaobengalalol", "biscoitao", "matta", "cogu", "tftoddy", "luccatv", "techzz", "dersin2k", "faturetosl",
        "mecmeno", "yulla", "revolta", "yoda", "kennzy", "ookina8",
    ];

    [GeneratedRegex("^[a-z0-9_-]{1,40}$")]
    private static partial Regex ValidName();

    /// <summary>
    /// Nomes de canal como a Kick usa no endereço: minúsculos, aceitando colar o endereço (<c>kick.com/canal</c>).
    /// Inválidos e repetidos saem; a ordem (prioridade) é mantida.
    /// </summary>
    public static List<string> Normalize(IEnumerable<string> raw)
    {
        var result = new List<string>();
        var seen = new HashSet<string>();
        foreach (var item in raw)
        {
            var name = item.Trim().ToLowerInvariant();
            if (name.Contains("kick.com/"))
                name = name[(name.LastIndexOf('/') + 1)..].Split('?', '#')[0];
            name = name.TrimStart('@');
            if (ValidName().IsMatch(name) && seen.Add(name)) result.Add(name);
        }
        return result;
    }
}
