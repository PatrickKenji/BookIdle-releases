using System.Text.RegularExpressions;

namespace IdleDeck.Core.Twitch;

public static partial class TwitchChannels
{
    [GeneratedRegex("^[a-z0-9_]{1,25}$")]
    private static partial Regex ValidName();

    /// <summary>
    /// Nomes de canal como a Twitch espera: minúsculos, sem <c>#</c>/<c>@</c>, aceitando colar o endereço
    /// (<c>twitch.tv/canal</c>). Inválidos e repetidos saem; a ordem é mantida.
    /// </summary>
    public static List<string> Normalize(IEnumerable<string> raw)
    {
        var result = new List<string>();
        var seen = new HashSet<string>();
        foreach (var item in raw)
        {
            var name = item.Trim().ToLowerInvariant();
            if (name.Contains("twitch.tv/"))
                name = name[(name.LastIndexOf('/') + 1)..].Split('?', '#')[0];
            name = name.TrimStart('#', '@');
            if (ValidName().IsMatch(name) && seen.Add(name)) result.Add(name);
        }
        return result;
    }
}
