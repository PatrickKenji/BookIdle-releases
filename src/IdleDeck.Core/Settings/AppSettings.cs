using IdleDeck.Core.Market;
using System.Text.Json.Serialization;
using IdleDeck.Core.Layout;
using IdleDeck.Core.Twitch;

namespace IdleDeck.Core.Settings;

/// <summary>
/// Layouts da área principal. Os nomes ficaram da versão 1, mas valem para qualquer número de contas:
/// <see cref="Grid2x2"/> é a grade automática e <see cref="MainPlus3"/> é "1 grande + as outras".
/// </summary>
public enum LayoutMode { Tabs, Grid2x2, MainPlus3, Free }

public enum LoginMethod { None, Password, Google }

/// <summary>Arcade é o roxo de sempre; Dark é preto e cinza neutros.</summary>
public enum ThemeKind { Arcade, Dark }

/// <summary>Uma conta (sessão) dentro de um perfil de jogo. <see cref="ProfileName"/> é o perfil do navegador e nunca muda.</summary>
public record AccountConfig(
    string Name,
    string ProfileName,
    LoginMethod Login = LoginMethod.None,
    bool Muted = false,
    DateTime? LastVoteUtc = null,
    string? TwitchLogin = null);

/// <summary>Um jogo: URL, limite de contas e as contas dele.</summary>
public class GameProfile
{
    public const int MaxAccountsLimit = 20;

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public int MaxAccounts { get; set; } = 4;
    public List<AccountConfig> Accounts { get; set; } = [];
    public LayoutMode Layout { get; set; } = LayoutMode.Tabs;
    public int FocusedAccount { get; set; }
    public bool Collapsed { get; set; }

    /// <summary>Arranjo do modo Livre (linhas e colunas com tamanhos proporcionais). Nulo até a primeira vez no Livre.</summary>
    public FreeLayout? Free { get; set; }

    /// <summary>Canais da Twitch que dão bônus neste jogo. Todas as contas do perfil entram em todos eles.</summary>
    public List<string> TwitchChannels { get; set; } = [];

    /// <summary>Sniper do mercado de cada conta deste perfil (chave = <see cref="AccountConfig.ProfileName"/>).</summary>
    public Dictionary<string, SniperAccountConfig> Sniper { get; set; } = [];

    /// <summary>Zoom das páginas das contas deste jogo (1.0 = 100%).</summary>
    public double Zoom { get; set; } = 1.0;

    public const double MinZoom = 0.5, MaxZoom = 1.5;
}

public class AppSettings
{
    public const int CurrentVersion = 2;
    public const string DefaultProfileId = "pokeidle";
    public const string DefaultUrl = "https://pokeidle.io";

    public int Version { get; set; } = CurrentVersion;
    public List<GameProfile> Profiles { get; set; } = [];
    public string SelectedProfileId { get; set; } = DefaultProfileId;
    public int MemoryLimitMb { get; set; } = 600;
    public bool AutoResetEnabled { get; set; } = true;
    public bool DisableBackgroundThrottling { get; set; } = true;
    public bool ExtensionsEnabled { get; set; }
    public bool StartWithWindows { get; set; }

    public ThemeKind Theme { get; set; } = ThemeKind.Arcade;

    /// <summary>Menu lateral recolhido (vira uma barra estreita com o logo e os quadrados das contas).</summary>
    public bool SidebarCollapsed { get; set; }

    /// <summary>Começa com a lista padrão; um settings.json sem o campo mantém o padrão.</summary>
    public List<string> BlockedDomains { get; set; } =
    [
        "doubleclick.net", "googlesyndication.com", "google-analytics.com", "googletagmanager.com",
        "adservice.google.com", "facebook.net", "hotjar.com", "clarity.ms",
    ];

    [JsonIgnore]
    public GameProfile SelectedProfile =>
        Profiles.FirstOrDefault(p => p.Id == SelectedProfileId) ?? Profiles[0];

    public static AppSettings CreateDefault() => new()
    {
        Profiles = [CreateDefaultProfile()],
    };

    public static GameProfile CreateDefaultProfile() => new()
    {
        Id = DefaultProfileId,
        Name = "PokeIdle",
        Url = DefaultUrl,
        MaxAccounts = 4,
        Accounts = Enumerable.Range(1, 4).Select(i => new AccountConfig($"Conta {i}", $"conta{i}")).ToList(),
    };

    /// <summary>
    /// Corrige valores fora do permitido depois de carregar do disco.
    /// Devolve false se algum perfil ou conta for inválido (o arquivo inteiro deve ser descartado).
    /// </summary>
    internal bool Normalize()
    {
        Version = CurrentVersion;
        Profiles ??= [];
        if (Profiles.Count == 0) Profiles = [CreateDefaultProfile()];
        if (!Profiles.All(IsValid)) return false;
        if (Profiles.Select(p => p.Id).Distinct().Count() != Profiles.Count) return false;

        var profileNames = Profiles.SelectMany(p => p.Accounts).Select(a => a.ProfileName).ToList();
        if (profileNames.Distinct().Count() != profileNames.Count) return false;

        foreach (var p in Profiles)
        {
            if (p.FocusedAccount < 0 || p.FocusedAccount >= Math.Max(1, p.Accounts.Count)) p.FocusedAccount = 0;
            if (!Enum.IsDefined(p.Layout)) p.Layout = LayoutMode.Tabs;
            p.TwitchChannels = TwitchChannels.Normalize(p.TwitchChannels ?? []);
            NormalizeSniper(p);
            p.Zoom = p.Zoom <= 0 || double.IsNaN(p.Zoom) ? 1.0 : Math.Clamp(p.Zoom, GameProfile.MinZoom, GameProfile.MaxZoom);
        }
        if (Profiles.All(p => p.Id != SelectedProfileId)) SelectedProfileId = Profiles[0].Id;
        if (MemoryLimitMb < 100) MemoryLimitMb = 600;
        if (!Enum.IsDefined(Theme)) Theme = ThemeKind.Arcade;
        BlockedDomains ??= [];
        return true;
    }

    /// <summary>Esquece o sniper de contas que não existem mais e arruma as regras que vieram do arquivo.</summary>
    private static void NormalizeSniper(GameProfile p)
    {
        var names = p.Accounts.Select(a => a.ProfileName).ToHashSet();
        var kept = new Dictionary<string, SniperAccountConfig>();
        foreach (var (name, config) in p.Sniper ?? [])
        {
            if (config is null || !names.Contains(name)) continue;
            SniperRules.Normalize(config);
            kept[name] = config;
        }
        p.Sniper = kept;
    }

    private static bool IsValid(GameProfile? p) =>
        p is not null
        && !string.IsNullOrWhiteSpace(p.Id)
        && !string.IsNullOrWhiteSpace(p.Name)
        && IsHttpUrl(p.Url)
        && p.MaxAccounts is >= 1 and <= GameProfile.MaxAccountsLimit
        && p.Accounts is not null
        && p.Accounts.All(IsValid);

    private static bool IsValid(AccountConfig? a) =>
        a is not null
        && !string.IsNullOrWhiteSpace(a.Name)
        && a.ProfileName is { Length: > 0 and <= 32 }
        && a.ProfileName.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
        && Enum.IsDefined(a.Login);

    public static bool IsHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http");
}
