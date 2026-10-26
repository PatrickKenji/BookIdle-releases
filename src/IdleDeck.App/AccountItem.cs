using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using IdleDeck.App.Browser;
using IdleDeck.Core.Profiles;
using IdleDeck.Core.Settings;
using IdleDeck.Core.Vote;

namespace IdleDeck.App;

/// <summary>Linha de uma conta dentro do card de perfil na lateral.</summary>
public sealed class AccountItem : INotifyPropertyChanged
{
    private string _name;
    private AccountStatus _status = AccountStatus.Loading;
    private long _memoryBytes;
    private double _ramFraction;
    private bool _isFocused;
    private bool _isRenaming;
    private LoginMethod _login;
    private bool _isMuted;
    private bool _isAutoCatch;

    public AccountItem(AccountView view)
    {
        View = view;
        _isMuted = view.Config.Muted;
        _isAutoCatch = view.Config.AutoCatch;
        _name = view.Config.Name;
        _login = view.Config.Login;
        var palette = (Array)Application.Current.Resources["AvatarPalette"];
        AvatarBrush = (Brush)palette.GetValue(AccountAvatar.ColorIndex(view.Config.ProfileName, palette.Length))!;
        view.StatusChanged += (_, _) => Status = view.Status;
    }

    public AccountView View { get; }
    public Brush AvatarBrush { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (Set(ref _name, value)) OnPropertyChanged(nameof(Initials));
        }
    }

    public string Initials => AccountAvatar.Initials(Name);

    public LoginMethod Login
    {
        get => _login;
        set
        {
            if (Set(ref _login, value)) OnPropertyChanged(nameof(Subtitle));
        }
    }

    public AccountStatus Status
    {
        get => _status;
        set
        {
            if (!Set(ref _status, value)) return;
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(Subtitle));
        }
    }

    /// <summary>"Google · Online", "Senha · Precisa de você"…</summary>
    public string Subtitle => Login switch
    {
        LoginMethod.Password => Loc.T("Senha · {0}", StatusText),
        LoginMethod.Google => Loc.T("Google · {0}", StatusText),
        _ => StatusText,
    };

    public string StatusText => Status switch
    {
        AccountStatus.Loading => Loc.T("Carregando"),
        AccountStatus.Online => Loc.T("Online"),
        AccountStatus.Offline => Loc.T("Sem conexão"),
        AccountStatus.Reloading => Loc.T("Recarregando"),
        AccountStatus.LoginError => Loc.T("Erro de login"),
        AccountStatus.NeedsUser => Loc.T("Precisa de você"),
        _ => Loc.T("Erro"),
    };

    public Brush StatusBrush => (Brush)Application.Current.Resources[Status switch
    {
        AccountStatus.Online => "Green",
        AccountStatus.Loading or AccountStatus.Reloading or AccountStatus.NeedsUser => "Yellow",
        _ => "Err",
    }];

    public long MemoryBytes
    {
        get => _memoryBytes;
        set
        {
            if (Set(ref _memoryBytes, value)) OnPropertyChanged(nameof(Memory));
        }
    }

    public string Memory => MemoryBytes > 0 ? Format.Bytes(MemoryBytes) : "";

    /// <summary>Uso de RAM em relação ao limite de reset automático (0–1), para a barrinha.</summary>
    public double RamFraction { get => _ramFraction; set => Set(ref _ramFraction, value); }

    public bool IsFocused { get => _isFocused; set => Set(ref _isFocused, value); }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (Set(ref _isMuted, value)) OnPropertyChanged(nameof(MuteLabel));
        }
    }

    // ---------- Captura automática ----------

    /// <summary>Clica sozinho nos pokémons caídos desta conta (o que o VIP faz no jogo).</summary>
    public bool IsAutoCatch
    {
        get => _isAutoCatch;
        set
        {
            if (!Set(ref _isAutoCatch, value)) return;
            OnPropertyChanged(nameof(AutoCatchLabel));
            OnPropertyChanged(nameof(AutoCatchButtonLabel));
        }
    }

    public string AutoCatchLabel => IsAutoCatch ? Loc.T("🎯  Captura automática: ligada") : Loc.T("🎯  Captura automática: desligada");

    public string AutoCatchButtonLabel => IsAutoCatch ? Loc.T("Captura auto: ligada") : Loc.T("Captura auto: desligada");

    // ---------- Twitch ----------

    private TwitchIcon _twitchIcon = TwitchIcon.Hidden;
    private string _twitchTooltip = "";

    /// <summary>Login da Twitch ligada a esta conta (null = nenhuma).</summary>
    public string? TwitchLogin => View.Config.TwitchLogin;

    public Visibility TwitchVisibility => _twitchIcon == TwitchIcon.Hidden ? Visibility.Collapsed : Visibility.Visible;

    public Brush TwitchBrush => (Brush)Application.Current.Resources[_twitchIcon switch
    {
        TwitchIcon.Green => "Green",
        TwitchIcon.Yellow => "Yellow",
        _ => "Muted",
    }];

    public string TwitchTooltip => _twitchTooltip;

    /// <summary>Texto do botão da Twitch na linha da conta: chama para conectar, ou mostra com qual login está ligada.</summary>
    public string TwitchButtonLabel => TwitchLogin is null ? Loc.T("Conectar Twitch") : Loc.T("Twitch: {0}", TwitchLogin);

    public bool TwitchConnected => TwitchLogin is not null;

    public string TwitchConnectLabel => TwitchLogin is null ? Loc.T("Conectar Twitch…") : Loc.T("Reconectar Twitch…");

    public string TwitchDisconnectLabel => Loc.T("Desconectar Twitch ({0})", TwitchLogin);

    public Visibility TwitchDisconnectVisibility => TwitchLogin is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Ícone da Twitch na linha da conta: verde (em todos os canais), amarelo (parcial), cinza (sem Twitch/sem chave).</summary>
    public void SetTwitch(TwitchIcon icon, string tooltip)
    {
        _twitchIcon = icon;
        _twitchTooltip = tooltip;
        OnPropertyChanged(nameof(TwitchVisibility));
        OnPropertyChanged(nameof(TwitchBrush));
        OnPropertyChanged(nameof(TwitchTooltip));
        OnPropertyChanged(nameof(TwitchConnectLabel));
        OnPropertyChanged(nameof(TwitchButtonLabel));
        OnPropertyChanged(nameof(TwitchConnected));
        OnPropertyChanged(nameof(TwitchDisconnectLabel));
        OnPropertyChanged(nameof(TwitchDisconnectVisibility));
    }

    // ---------- Voto no TopIdle ----------

    /// <summary>O voto só existe para o PokeIdle: o host do perfil casa com pokeidle.io.</summary>
    public bool CanVote => View.IsPokeIdle;

    public Visibility VoteVisibility => CanVote ? Visibility.Visible : Visibility.Collapsed;

    public string VoteLabel => VoteCooldown.Label(View.Config.LastVoteUtc, DateTime.UtcNow);

    /// <summary>Texto do botão de voto na linha da conta (curto, para caber ao lado do botão da Twitch).</summary>
    public string VoteButtonLabel => VoteCooldown.ShortLabel(View.Config.LastVoteUtc, DateTime.UtcNow);

    /// <summary>Liberado para votar de novo (selo no quadrado da conta no menu recolhido).</summary>
    public bool VoteAvailable =>
        CanVote && VoteCooldown.Remaining(View.Config.LastVoteUtc, DateTime.UtcNow) == TimeSpan.Zero;

    public Visibility VoteDotVisibility => VoteAvailable ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Chamado depois de votar e a cada minuto: a contagem do menu anda sozinha.</summary>
    public void RefreshVote()
    {
        OnPropertyChanged(nameof(VoteLabel));
        OnPropertyChanged(nameof(VoteButtonLabel));
        OnPropertyChanged(nameof(VoteVisibility));
        OnPropertyChanged(nameof(VoteAvailable));
        OnPropertyChanged(nameof(VoteDotVisibility));
    }

    private bool _isProbing;

    /// <summary>Diagnóstico do mercado ligado nesta conta (só nesta execução do app).</summary>
    public bool IsProbing
    {
        get => _isProbing;
        set { if (Set(ref _isProbing, value)) OnPropertyChanged(nameof(ProbeLabel)); }
    }

    public string ProbeLabel => IsProbing ? Loc.T("◉  Diagnóstico do mercado: ligado (clique para silenciar)") : Loc.T("◎  Diagnóstico do mercado");

    public string MuteLabel => IsMuted ? Loc.T("🔊  Tirar o mudo") : Loc.T("🔇  Mutar");

    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (!Set(ref _isRenaming, value)) return;
            OnPropertyChanged(nameof(NameVisibility));
            OnPropertyChanged(nameof(RenameVisibility));
        }
    }

    public Visibility NameVisibility => IsRenaming ? Visibility.Collapsed : Visibility.Visible;
    public Visibility RenameVisibility => IsRenaming ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum TwitchIcon { Hidden, Gray, Yellow, Green }

public static class Format
{
    public static string Bytes(long bytes) =>
        bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024.0 * 1024 * 1024):0.0} GB" : $"{bytes / (1024 * 1024)} MB";
}
