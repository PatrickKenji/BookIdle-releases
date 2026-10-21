namespace IdleDeck.Core.Profiles;

/// <summary>Iniciais e cor do avatar de uma conta.</summary>
public static class AccountAvatar
{
    /// <summary>Uma palavra → 2 primeiras letras ("Main" → "MA"); várias → iniciais das 2 primeiras ("Conta 2" → "C2").</summary>
    public static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var initials = words.Length switch
        {
            0 => "?",
            1 => words[0].Length >= 2 ? words[0][..2] : words[0],
            _ => $"{words[0][0]}{words[1][0]}",
        };
        return initials.ToUpperInvariant();
    }

    /// <summary>Índice de cor estável entre execuções (FNV-1a 32 bits do perfil de navegador).</summary>
    public static int ColorIndex(string profileName, int paletteSize)
    {
        var hash = 2166136261u;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(profileName))
        {
            hash ^= b;
            hash *= 16777619u;
        }
        return (int)(hash % (uint)paletteSize);
    }
}
