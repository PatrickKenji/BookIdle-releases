using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using IdleDeck.App.Modules;
using IdleDeck.Core.Settings;

namespace IdleDeck.App.Sidebar;

/// <summary>
/// Botão de um módulo dentro do card do perfil (ex.: <c>/hunt</c> no card do PokeIdle).
/// O resumo só aparece no perfil selecionado, porque é dele que o módulo lê as contas.
/// </summary>
public sealed class ModuleButton(IModuleView module, GameProfile profile) : INotifyPropertyChanged
{
    private string? _summary;

    public IModuleView Module { get; } = module;
    public GameProfile Profile { get; } = profile;

    public string Label => $"📊  {Module.Title}";
    public string Tooltip => $"Abrir {Module.Title} numa janela, com as contas deste perfil";

    public string? Summary => _summary;
    public Visibility SummaryVisibility => string.IsNullOrEmpty(_summary) ? Visibility.Collapsed : Visibility.Visible;

    public void SetSummary(string? texto)
    {
        if (_summary == texto) return;
        _summary = texto;
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(SummaryVisibility));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? nome = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
}
