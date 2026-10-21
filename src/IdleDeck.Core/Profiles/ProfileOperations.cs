using System.Security.Cryptography;
using IdleDeck.Core.Settings;

namespace IdleDeck.Core.Profiles;

/// <summary>Criar, remover e renomear perfis de jogo e contas, mantendo as configurações válidas.</summary>
public static class ProfileOperations
{
    public const int MaxNameLength = 24;

    /// <summary>Cria a próxima conta do perfil ("Conta n"), ou null se o perfil já está no limite.</summary>
    public static AccountConfig? AddAccount(AppSettings settings, GameProfile profile)
    {
        if (profile.Accounts.Count >= profile.MaxAccounts) return null;

        var used = profile.Accounts.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var n = 1;
        while (used.Contains($"Conta {n}")) n++;

        var account = new AccountConfig($"Conta {n}", NewId(settings, "acc-"));
        profile.Accounts.Add(account);
        return account;
    }

    public static bool RemoveAccount(GameProfile profile, string profileName)
    {
        var index = profile.Accounts.FindIndex(a => a.ProfileName == profileName);
        if (index < 0) return false;

        profile.Accounts.RemoveAt(index);
        if (index < profile.FocusedAccount) profile.FocusedAccount--;
        profile.FocusedAccount = Math.Clamp(profile.FocusedAccount, 0, Math.Max(0, profile.Accounts.Count - 1));
        return true;
    }

    /// <summary>Cria um perfil vazio e o seleciona.</summary>
    public static GameProfile AddProfile(AppSettings settings, string name, string url, int maxAccounts)
    {
        var profile = new GameProfile
        {
            Id = NewId(settings, "game-"),
            Name = name.Trim(),
            Url = url.Trim(),
            MaxAccounts = Math.Clamp(maxAccounts, 1, GameProfile.MaxAccountsLimit),
        };
        settings.Profiles.Add(profile);
        settings.SelectedProfileId = profile.Id;
        return profile;
    }

    /// <summary>Remove um perfil. O último perfil não pode ser removido.</summary>
    public static bool RemoveProfile(AppSettings settings, string profileId)
    {
        if (settings.Profiles.Count <= 1) return false;
        var removed = settings.Profiles.RemoveAll(p => p.Id == profileId) > 0;
        if (removed && settings.Profiles.All(p => p.Id != settings.SelectedProfileId))
            settings.SelectedProfileId = settings.Profiles[0].Id;
        return removed;
    }

    /// <summary>
    /// "Expandir" uma conta da Grade ou do 1+N: o perfil vai para as Abas com ela selecionada.
    /// Conta que não existe no perfil: devolve false e não muda nada.
    /// </summary>
    public static bool ExpandToTabs(GameProfile profile, string accountProfileName)
    {
        var index = profile.Accounts.FindIndex(a => a.ProfileName == accountProfileName);
        if (index < 0) return false;
        profile.Layout = LayoutMode.Tabs;
        profile.FocusedAccount = index;
        return true;
    }

    /// <summary>
    /// Troca a posição de duas contas (reordenar arrastando). O foco continua na mesma conta, não no mesmo índice.
    /// Nome desconhecido ou o mesmo nome duas vezes: devolve false e não muda nada.
    /// </summary>
    public static bool SwapAccounts(GameProfile profile, string a, string b)
    {
        var i = profile.Accounts.FindIndex(x => x.ProfileName == a);
        var j = profile.Accounts.FindIndex(x => x.ProfileName == b);
        if (i < 0 || j < 0 || i == j) return false;
        (profile.Accounts[i], profile.Accounts[j]) = (profile.Accounts[j], profile.Accounts[i]);
        if (profile.FocusedAccount == i) profile.FocusedAccount = j;
        else if (profile.FocusedAccount == j) profile.FocusedAccount = i;
        return true;
    }

    /// <summary>
    /// Tira <paramref name="account"/> de onde está e a coloca antes (ou depois, com <paramref name="after"/>) de
    /// <paramref name="target"/> — arrastar na lista da lateral. O foco continua na mesma conta, não no mesmo índice.
    /// Nome desconhecido, a mesma conta, ou um movimento que não muda a ordem: devolve false e não muda nada.
    /// </summary>
    public static bool MoveAccount(GameProfile profile, string account, string target, bool after)
    {
        var from = profile.Accounts.FindIndex(a => a.ProfileName == account);
        var to = profile.Accounts.FindIndex(a => a.ProfileName == target);
        if (from < 0 || to < 0 || from == to) return false;

        var moving = profile.Accounts[from];
        var focusedName = profile.Accounts.ElementAtOrDefault(profile.FocusedAccount)?.ProfileName;
        profile.Accounts.RemoveAt(from);
        var at = profile.Accounts.FindIndex(a => a.ProfileName == target) + (after ? 1 : 0);
        if (at == from) // caiu onde já estava: desfaz
        {
            profile.Accounts.Insert(from, moving);
            return false;
        }
        profile.Accounts.Insert(at, moving);
        if (focusedName is not null)
            profile.FocusedAccount = Math.Max(0, profile.Accounts.FindIndex(a => a.ProfileName == focusedName));
        return true;
    }

    /// <summary>Renomeia (aparado, até <see cref="MaxNameLength"/> caracteres). Nome vazio é recusado: devolve null.</summary>
    public static string? RenameAccount(GameProfile profile, string profileName, string newName)
    {
        var name = newName.Trim();
        if (name.Length == 0) return null;
        if (name.Length > MaxNameLength) name = name[..MaxNameLength].TrimEnd();

        var index = profile.Accounts.FindIndex(a => a.ProfileName == profileName);
        if (index < 0) return null;
        profile.Accounts[index] = profile.Accounts[index] with { Name = name };
        return name;
    }

    private static string NewId(AppSettings settings, string prefix)
    {
        var used = settings.Profiles.Select(p => p.Id)
            .Concat(settings.Profiles.SelectMany(p => p.Accounts).Select(a => a.ProfileName))
            .ToHashSet();
        string id;
        do id = prefix + RandomNumberGenerator.GetHexString(8, lowercase: true);
        while (used.Contains(id));
        return id;
    }
}
