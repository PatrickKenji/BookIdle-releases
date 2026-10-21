using System.IO;
using System.Text.Json;
using System.Windows;
using IdleDeck.Core.Hunt;

namespace IdleDeck.App.Modules.Hunt;

/// <summary>
/// /hunt: em cada conta do PokeIdle, um script lê o jogo e calcula as melhores hunts para o pokémon
/// em campo (com as regras do jogo); aqui guardamos o último resultado de cada conta e desenhamos o painel.
/// </summary>
public sealed class HuntModule : IModuleView
{
    private readonly IModuleHostServices _host;
    private readonly HuntBoard _quadro = new();
    private HuntPanel? _painel;

    public HuntModule(IModuleHostServices host)
    {
        _host = host;
        _host.ContasMudaram += (_, _) =>
        {
            _painel?.Atualizar();
            SummaryChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public string Id => "hunt";
    public string Title => "/hunt";
    public string UrlPattern => "pokeidle.io";
    /// <summary>O /hunt só aparece quando o perfil aponta para o pokeidle.io (raiz) ou pokeidle.io/app.</summary>
    public IReadOnlyList<string> AllowedPaths => ["/", "/app"];
    public string? InjectedScript { get; } = Carregar("Modules.Hunt.hunt-engine.js") + "\n" + Carregar("Modules.Hunt.hunt-page.js");

    public string? Description => "Hunts de todas as contas deste perfil, lidas do jogo ao vivo. Clique numa conta para ver o ranking completo.";

    public event EventHandler? SummaryChanged;

    /// <summary>"3 de 4 contas com hunts" — a lateral mostra isso embaixo do botão do módulo.</summary>
    public string? Summary
    {
        get
        {
            var contas = _host.ContasDoPerfilSelecionado();
            if (contas.Count == 0) return null;
            var comHunt = contas.Count(_quadro.HasReport);
            if (comHunt == contas.Count) return $"{contas.Count} de {contas.Count} contas com hunts";
            return comHunt == 0 ? $"esperando o jogo em {contas.Count} conta{(contas.Count == 1 ? "" : "s")}…"
                : $"{comHunt} de {contas.Count} contas com hunts";
        }
    }

    public void OnMessage(int contaId, JsonElement data)
    {
        var msg = HuntMessage.Parse(data);
        Registrar(contaId, data, msg);
        if (msg is null) return;
        // Um aviso não apaga o último relatório bom; o relatório novo limpa o aviso (regra no HuntBoard).
        _quadro.Apply(contaId, msg, DateTime.Now);
        _painel?.Atualizar();
        SummaryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Marcos e erros do /hunt vão para o hunt.log (uma linha cada); o relatório entra só como resumo.</summary>
    private void Registrar(int contaId, JsonElement data, HuntMessage? msg)
    {
        var conta = _host.NomeDaConta(contaId) ?? contaId.ToString();
        var tipo = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("tipo", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        switch (msg)
        {
            case HuntStatus status: HuntLog.Write(conta, $"status: {status.Texto}"); break;
            case HuntReport r:
                HuntLog.Write(conta, $"relatório: {r.PorXp.Count} hunts por XP, pokémon {r.Pokemon?.Nome ?? "—"}, jogo {r.VersaoJogo ?? "?"}, dia sazonal {r.DiaSazonal ?? "nenhum"}");
                break;
            default:
                if (tipo == "diag" && data.TryGetProperty("texto", out var texto) && texto.ValueKind == JsonValueKind.String) HuntLog.Write(conta, texto.GetString() ?? "");
                else HuntLog.Write(conta, $"mensagem ignorada (formato inesperado): {Resumir(data.GetRawText())}");
                break;
        }
    }

    private static string Resumir(string json) => json.Length <= 240 ? json : json[..240] + "…";

    public FrameworkElement CreatePanel() => _painel = new HuntPanel(this, _host);

    internal IReadOnlyDictionary<int, HuntEntry> Ultimos => _quadro.Entries;

    /// <summary>Ajusta os alvos por golpe do TM AoE na conta (fica guardado nela) e recalcula.</summary>
    internal Task DefinirAoeAsync(int contaId, int alvos) =>
        _host.ExecutarNaContaAsync(contaId, $"window.__idleHunt && window.__idleHunt.definirAoe({alvos})");

    private static string Carregar(string recurso)
    {
        using var stream = typeof(HuntModule).Assembly.GetManifestResourceStream(recurso)
                           ?? throw new InvalidOperationException($"Script {recurso} não embutido.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
