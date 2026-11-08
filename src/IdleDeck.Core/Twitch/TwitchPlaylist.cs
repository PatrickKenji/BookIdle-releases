namespace IdleDeck.Core.Twitch;

/// <summary>
/// A lista de qualidades de uma live (HLS "master playlist" da Twitch). Deixar só a menor qualidade de vídeo trava o
/// player nela: ele não tem como subir para 720p sozinho, o que poupa memória e CPU de quem só assiste para os pontos.
/// </summary>
public static class TwitchPlaylist
{
    /// <summary>
    /// A mesma lista só com a variante de vídeo de menor banda (nunca "só áudio", que não conta como assistir).
    /// Se não reconhecer o formato, devolve o texto como veio.
    /// </summary>
    public static string KeepLowest(string playlist)
    {
        var lines = playlist.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "#EXTM3U") return playlist;

        // variantes: linha #EXT-X-STREAM-INF + a linha seguinte (endereço)
        var variants = new List<(int Index, long Bandwidth, string? Group)>();
        for (var i = 0; i < lines.Length - 1; i++)
        {
            if (!lines[i].StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal)) continue;
            var group = Attribute(lines[i], "VIDEO");
            if (group == "audio_only") continue;
            if (!long.TryParse(Attribute(lines[i], "BANDWIDTH"), out var bw)) continue;
            variants.Add((i, bw, group));
        }
        if (variants.Count < 2) return playlist;
        var keep = variants.MinBy(v => v.Bandwidth);

        var result = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal))
            {
                if (i == keep.Index) { result.Add(line); result.Add(lines[i + 1]); }
                i++; // pula o endereço
                continue;
            }
            if (line.StartsWith("#EXT-X-MEDIA:", StringComparison.Ordinal) && Attribute(line, "TYPE") == "VIDEO"
                && Attribute(line, "GROUP-ID") != keep.Group)
                continue;
            result.Add(line);
        }
        return string.Join("\n", result);
    }

    /// <summary>Valor de um atributo (<c>NOME=valor</c> ou <c>NOME="valor"</c>) de uma linha de tag.</summary>
    private static string? Attribute(string line, string name)
    {
        var colon = line.IndexOf(':');
        var i = colon + 1;
        while (i > 0 && i < line.Length)
        {
            var eq = line.IndexOf('=', i);
            if (eq < 0) return null;
            var key = line[i..eq];
            string value;
            int next;
            if (eq + 1 < line.Length && line[eq + 1] == '"')
            {
                var close = line.IndexOf('"', eq + 2);
                if (close < 0) return null;
                value = line[(eq + 2)..close];
                next = close + 1;
            }
            else
            {
                var comma = line.IndexOf(',', eq);
                next = comma < 0 ? line.Length : comma;
                value = line[(eq + 1)..next];
            }
            if (key == name) return value;
            i = next + 1; // pula a vírgula
        }
        return null;
    }
}
