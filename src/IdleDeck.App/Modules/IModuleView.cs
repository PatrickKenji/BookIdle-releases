using System.Windows;
using IdleDeck.Core.Modules;

namespace IdleDeck.App.Modules;

/// <summary>Módulo com painel próprio, que o app mostra numa janela (botão da lateral).</summary>
public interface IModuleView : IModule
{
    /// <summary>Chamado uma vez, quando o painel do módulo é aberto pela primeira vez.</summary>
    FrameworkElement CreatePanel();

    /// <summary>Subtítulo da janela do módulo (null = sem subtítulo).</summary>
    string? Description { get; }

    /// <summary>Linha curta abaixo do botão da lateral (null = sem resumo).</summary>
    string? Summary { get; }

    /// <summary>O resumo mudou (chegou dado novo das contas): a lateral se atualiza.</summary>
    event EventHandler? SummaryChanged;
}
