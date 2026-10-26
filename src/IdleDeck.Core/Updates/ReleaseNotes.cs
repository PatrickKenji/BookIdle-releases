namespace IdleDeck.Core.Updates;

/// <summary>
/// Notas de uma versão (arquivo <c>release-notes/X.Y.Z.md</c>). Uma seção por idioma (<c># pt</c>, <c># en</c>, <c># es</c>);
/// dentro dela, o primeiro <c>##</c> é o resumo (mostrado antes de atualizar) e o segundo, os detalhes (mostrados depois).
/// O nome dos subtítulos é livre: vale a posição.
/// </summary>
public sealed record ReleaseNotes(string Summary, string Details)
{
    public const string FallbackLanguage = "pt";

    /// <summary>As notas no idioma pedido (ou em português, ou na primeira seção). Texto sem seções vira resumo e detalhes.</summary>
    public static ReleaseNotes? Parse(string? markdown, string language)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        var sections = SplitByLanguage(markdown);
        if (sections.Count == 0)
        {
            var text = ToPlainText(markdown);
            return text.Length == 0 ? null : new ReleaseNotes(text, text);
        }

        var lines = sections.GetValueOrDefault(language) ?? sections.GetValueOrDefault(FallbackLanguage) ?? sections.Values.First();
        var parts = new List<List<string>>();
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal)) parts.Add([]);
            else if (parts.Count > 0) parts[^1].Add(line);
        }
        var summary = parts.Count > 0 ? ToPlainText(string.Join('\n', parts[0])) : ToPlainText(string.Join('\n', lines));
        var details = parts.Count > 1 ? ToPlainText(string.Join('\n', parts[1])) : summary;
        return summary.Length == 0 && details.Length == 0 ? null : new ReleaseNotes(summary, details);
    }

    /// <summary>Linhas de cada idioma, pelas linhas <c># xx</c> (código de duas letras).</summary>
    private static Dictionary<string, List<string>> SplitByLanguage(string markdown)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string>? current = null;
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 4 && line.StartsWith("# ", StringComparison.Ordinal) && char.IsLetter(line[2]) && char.IsLetter(line[3]))
            {
                current = [];
                sections[line[2..].ToLowerInvariant()] = current;
            }
            else current?.Add(line);
        }
        return sections;
    }

    /// <summary>Markdown simples para texto de caixa de diálogo: listas viram "•", negrito e código perdem as marcas.</summary>
    public static string ToPlainText(string markdown)
    {
        var lines = new List<string>();
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd().Replace("**", "").Replace("`", "");
            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                line = new string(' ', indent) + "• " + trimmed[2..];
            else if (trimmed.StartsWith('#'))
                line = trimmed.TrimStart('#').Trim();
            lines.Add(line);
        }
        // sem linhas vazias sobrando no começo/fim, e no máximo uma seguida
        var text = string.Join('\n', lines).Trim('\n', ' ');
        while (text.Contains("\n\n\n")) text = text.Replace("\n\n\n", "\n\n");
        return text;
    }

    /// <summary>
    /// Mostrar "o que mudou" ao abrir: a versão é outra que a da última abertura e há notas para ela.
    /// Primeira instalação (sem configurações antes) não mostra; quem veio de uma versão sem esse registro mostra.
    /// </summary>
    public static bool ShouldShowAfterUpdate(string? lastRunVersion, string currentVersion, bool settingsExisted) =>
        lastRunVersion is null ? settingsExisted : !string.Equals(lastRunVersion, currentVersion, StringComparison.Ordinal);
}
