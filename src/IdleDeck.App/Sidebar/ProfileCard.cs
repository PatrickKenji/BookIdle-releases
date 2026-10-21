using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using IdleDeck.Core.Settings;

namespace IdleDeck.App.Sidebar;

/// <summary>Card de um perfil de jogo na lateral: cabeçalho (nome, n/limite, RAM) + contas.</summary>
public sealed class ProfileCard(GameProfile profile) : INotifyPropertyChanged
{
    private bool _isSelected;

    public GameProfile Profile { get; } = profile;
    public ObservableCollection<AccountItem> Accounts { get; } = [];

    /// <summary>Módulos que valem para este perfil (ex.: /hunt no pokeidle.io).</summary>
    public ObservableCollection<ModuleButton> Modules { get; } = [];

    public string Title => Profile.Name.ToUpperInvariant();

    public string Subtitle
    {
        get
        {
            var memory = Accounts.Sum(a => a.MemoryBytes);
            var text = Loc.T("{0}/{1} por IP", Accounts.Count, Profile.MaxAccounts);
            return memory > 0 ? $"{text} · {Format.Bytes(memory)}" : text;
        }
    }

    public bool CanAddAccount => Profile.Accounts.Count < Profile.MaxAccounts;

    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

    public bool IsCollapsed
    {
        get => Profile.Collapsed;
        set
        {
            Profile.Collapsed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Chevron));
            OnPropertyChanged(nameof(AccountsVisibility));
        }
    }

    public string Chevron => IsCollapsed ? "›" : "⌄";
    public Visibility AccountsVisibility => IsCollapsed ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Atualiza os textos depois de mudar nome, limite, contas ou memória.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CanAddAccount));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
