using System.Diagnostics;
using System.Text.Json;

namespace IdleDeck.Core.Modules;

/// <summary>Entrega as mensagens das páginas ao módulo certo, sem deixar um módulo derrubar o app.</summary>
public class ModuleRouter(IEnumerable<IModule> modules)
{
    private readonly Dictionary<string, IModule> _byId = modules.ToDictionary(m => m.Id);

    public IEnumerable<IModule> Modules => _byId.Values;

    public bool Route(int accountIndex, string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("module", out var id) || id.ValueKind != JsonValueKind.String
                || !_byId.TryGetValue(id.GetString()!, out var module))
                return false;

            var data = root.TryGetProperty("data", out var d) ? d.Clone() : default;
            module.OnMessage(accountIndex, data);
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[ModuleRouter] mensagem ignorada: {e.Message}");
            return false;
        }
    }

    public IEnumerable<IModule> ModulesFor(Uri accountUrl) =>
        _byId.Values.Where(m => UrlMatches(accountUrl, m));

    /// <summary>O módulo vale para esta URL? (host casa e o caminho está na lista permitida)</summary>
    public static bool UrlMatches(Uri url, IModule module) =>
        HostMatches(url.Host, module.UrlPattern) && PathMatches(url.AbsolutePath, module.AllowedPaths);

    /// <summary>Caminho aceito pelo módulo. Lista vazia = qualquer caminho; "/" e "/app/" valem como "/" e "/app".</summary>
    public static bool PathMatches(string path, IReadOnlyList<string> allowed)
    {
        if (allowed.Count == 0) return true;
        var alvo = NormalizarCaminho(path);
        return allowed.Any(a => string.Equals(NormalizarCaminho(a), alvo, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizarCaminho(string? caminho)
    {
        var c = (caminho ?? "").Trim();
        if (c.Length == 0) return "/";
        if (c[0] != '/') c = "/" + c;
        if (c.Length > 1) c = c.TrimEnd('/');
        return c.Length == 0 ? "/" : c;
    }

    public static bool HostMatches(string host, string pattern)
    {
        host = host.ToLowerInvariant();
        pattern = pattern.ToLowerInvariant();
        return host == pattern || host.EndsWith("." + pattern, StringComparison.Ordinal);
    }
}
