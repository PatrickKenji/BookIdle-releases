using System.Text.Json;
using System.Windows;
using IdleDeck.App.Browser;
using IdleDeck.Core.Modules;

namespace IdleDeck.App.Modules;

/// <summary>
/// Liga os módulos às contas: injeta os scripts (só rodam em páginas do host do módulo),
/// entrega as mensagens das páginas e guarda o painel de cada módulo.
/// Para adicionar um módulo: implementar <see cref="IModuleView"/> e incluir em <see cref="All"/>.
/// </summary>
public sealed class ModuleHost
{
    private readonly ModuleRouter _router;
    private readonly Dictionary<string, FrameworkElement> _panels = [];

    public ModuleHost(IReadOnlyList<IModuleView> modules)
    {
        Modules = modules;
        _router = new ModuleRouter(modules);
    }

    /// <summary>Módulos disponíveis no app.</summary>
    public static IReadOnlyList<IModuleView> All(IModuleHostServices host) => [new Hunt.HuntModule(host), new Sniper.SniperModule(host)];

    public IReadOnlyList<IModuleView> Modules { get; }

    public async Task RegisterScriptsAsync(AccountView view)
    {
        foreach (var module in Modules)
            if (module.InjectedScript is { } script)
                await view.AddStartupScriptAsync(WrapForHost(module.UrlPattern, script));
    }

    public bool Route(int accountIndex, string message) => _router.Route(accountIndex, message);

    public FrameworkElement PanelFor(IModuleView module)
    {
        if (!_panels.TryGetValue(module.Id, out var panel))
            _panels[module.Id] = panel = module.CreatePanel();
        return panel;
    }

    /// <summary>O script só roda se o site da página casar com o host do módulo (segue trocas de URL da conta).</summary>
    private static string WrapForHost(string host, string script)
    {
        var h = JsonSerializer.Serialize(host.ToLowerInvariant());
        return $$"""
            (() => {
              const h = location.hostname.toLowerCase();
              if (h !== {{h}} && !h.endsWith('.' + {{h}})) return;
            {{script}}
            })();
            """;
    }
}
