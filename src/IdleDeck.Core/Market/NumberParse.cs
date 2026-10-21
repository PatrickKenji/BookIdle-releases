namespace IdleDeck.Core.Market;

/// <summary>Lê o que a pessoa digita em campos de preço, sem nunca errar por uma ordem de grandeza.</summary>
public static class NumberParse
{
    /// <summary>
    /// Inteiro com ou sem separadores de milhar (<c>1.000.000</c>, <c>1,000</c>, <c>1 000</c>); uma parte decimal de 1 ou 2
    /// dígitos (<c>1.500,50</c>) é descartada, não vira milhar. Vazio = 0. Texto estranho, negativo ou ambíguo = null.
    /// </summary>
    public static long? Coins(string? text)
    {
        var s = new string((text ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (s.Length == 0) return 0;
        if (s.Any(c => !char.IsAsciiDigit(c) && c is not '.' and not ',')) return null;

        var last = s.LastIndexOfAny(['.', ',']);
        string whole;
        if (last < 0) whole = s;
        else
        {
            var after = s.Length - last - 1;
            if (after is 1 or 2) whole = s[..last];     // parte decimal
            else if (after == 3) whole = s;              // só milhar
            else return null;
        }
        return Grouped(whole) is { } digits && long.TryParse(digits.Length == 0 ? "0" : digits, out var n) ? n : null;
    }

    /// <summary>Dígitos de um número com milhar válido (grupos de 3 depois do primeiro) ou null.</summary>
    private static string? Grouped(string text)
    {
        if (text.Length == 0) return "";
        var groups = text.Split('.', ',');
        if (groups[0].Length is < 1 or > 3 && groups.Length > 1) return null;
        for (var i = 1; i < groups.Length; i++)
            if (groups[i].Length != 3) return null;
        return string.Concat(groups);
    }
}
