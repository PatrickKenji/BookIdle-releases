using System.Text.RegularExpressions;
using IdleDeck.Core.Profiles;
using IdleDeck.Core.Settings;

namespace IdleDeck.Core.Tests;

public class ProfileOperationsTests
{
    private static AppSettings Settings() => AppSettings.CreateDefault();

    [Fact]
    public void AddAccount_respects_limit()
    {
        var s = Settings();
        var p = s.Profiles[0];

        Assert.Null(ProfileOperations.AddAccount(s, p));

        p.MaxAccounts = 5;
        var added = ProfileOperations.AddAccount(s, p);
        Assert.NotNull(added);
        Assert.Equal(5, p.Accounts.Count);
        Assert.Equal(LoginMethod.None, added.Login);
    }

    [Fact]
    public void AddAccount_names_fill_gaps()
    {
        var s = Settings();
        var p = s.Profiles[0];
        p.Accounts = [new AccountConfig("Conta 1", "conta1"), new AccountConfig("Conta 3", "conta3")];

        Assert.Equal("Conta 2", ProfileOperations.AddAccount(s, p)!.Name);
        Assert.Equal("Conta 4", ProfileOperations.AddAccount(s, p)!.Name);
    }

    [Fact]
    public void AddAccount_profile_name_unique_across_profiles()
    {
        var s = Settings();
        var a = s.Profiles[0];
        a.MaxAccounts = 50;
        var b = ProfileOperations.AddProfile(s, "Outro", "https://example.com/", 50);

        for (var i = 0; i < 50; i++)
        {
            ProfileOperations.AddAccount(s, a);
            ProfileOperations.AddAccount(s, b);
        }

        var names = s.Profiles.SelectMany(p => p.Accounts).Select(x => x.ProfileName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(names.Where(n => !n.StartsWith("conta")), n => Assert.Matches(new Regex("^acc-[0-9a-f]{8}$"), n));
    }

    [Fact]
    public void RemoveAccount_keeps_focus_valid()
    {
        var s = Settings();
        var p = s.Profiles[0];
        p.FocusedAccount = 3;

        Assert.True(ProfileOperations.RemoveAccount(p, "conta4"));
        Assert.Equal(2, p.FocusedAccount);

        ProfileOperations.RemoveAccount(p, "conta1");
        ProfileOperations.RemoveAccount(p, "conta2");
        ProfileOperations.RemoveAccount(p, "conta3");
        Assert.Empty(p.Accounts);
        Assert.Equal(0, p.FocusedAccount);

        Assert.False(ProfileOperations.RemoveAccount(p, "nao-existe"));
    }

    [Fact]
    public void RemoveAccount_before_focus_keeps_same_account_focused()
    {
        var p = Settings().Profiles[0];
        p.FocusedAccount = 2; // conta3

        ProfileOperations.RemoveAccount(p, "conta1");

        Assert.Equal("conta3", p.Accounts[p.FocusedAccount].ProfileName);
    }

    [Fact]
    public void AddProfile_becomes_selected_with_game_id()
    {
        var s = Settings();

        var p = ProfileOperations.AddProfile(s, "  Outro Jogo ", "https://example.com/", 3);

        Assert.Matches(new Regex("^game-[0-9a-f]{8}$"), p.Id);
        Assert.Equal("Outro Jogo", p.Name);
        Assert.Equal(3, p.MaxAccounts);
        Assert.Empty(p.Accounts);
        Assert.Same(p, s.SelectedProfile);
    }

    [Fact]
    public void RemoveProfile_refuses_last()
    {
        var s = Settings();

        Assert.False(ProfileOperations.RemoveProfile(s, "pokeidle"));
        Assert.Single(s.Profiles);
    }

    [Fact]
    public void RemoveProfile_selected_moves_selection()
    {
        var s = Settings();
        var other = ProfileOperations.AddProfile(s, "Outro", "https://example.com/", 4);

        Assert.True(ProfileOperations.RemoveProfile(s, other.Id));

        Assert.Equal("pokeidle", s.SelectedProfileId);
        Assert.Single(s.Profiles);
    }

    [Fact]
    public void RenameAccount_trims_and_limits()
    {
        var p = Settings().Profiles[0];

        Assert.Equal("Main Gengar", ProfileOperations.RenameAccount(p, "conta1", "  Main Gengar  "));
        Assert.Equal("Main Gengar", p.Accounts[0].Name);

        var renamed = ProfileOperations.RenameAccount(p, "conta2", new string('x', 30));
        Assert.Equal(ProfileOperations.MaxNameLength, renamed!.Length);
    }

    [Fact]
    public void RenameAccount_rejects_blank()
    {
        var p = Settings().Profiles[0];

        Assert.Null(ProfileOperations.RenameAccount(p, "conta1", "   "));
        Assert.Equal("Conta 1", p.Accounts[0].Name);
    }
}

public class AccountAvatarTests
{
    [Theory]
    [InlineData("Main", "MA")]
    [InlineData("Conta 2", "C2")]
    [InlineData("farm gold", "FG")]
    [InlineData("x", "X")]
    [InlineData("  ", "?")]
    [InlineData("Ébano", "ÉB")]
    public void Initials(string name, string expected) =>
        Assert.Equal(expected, AccountAvatar.Initials(name));

    [Fact]
    public void ColorIndex_is_stable()
    {
        // FNV-1a 32 bits de "conta1" = 1858213263; 1858213263 % 6 = 3
        Assert.Equal(3, AccountAvatar.ColorIndex("conta1", 6));
        Assert.Equal(3, AccountAvatar.ColorIndex("conta1", 6));
    }

    [Fact]
    public void ExpandToTabs_sets_tabs_and_focus()
    {
        var profile = AppSettings.CreateDefault().Profiles[0];
        profile.Layout = LayoutMode.Grid2x2;

        var ok = ProfileOperations.ExpandToTabs(profile, "conta3");

        Assert.True(ok);
        Assert.Equal(LayoutMode.Tabs, profile.Layout);
        Assert.Equal(2, profile.FocusedAccount);
    }

    [Fact]
    public void ExpandToTabs_unknown_account_changes_nothing()
    {
        var profile = AppSettings.CreateDefault().Profiles[0];
        profile.Layout = LayoutMode.MainPlus3;
        profile.FocusedAccount = 1;

        var ok = ProfileOperations.ExpandToTabs(profile, "nao-existe");

        Assert.False(ok);
        Assert.Equal(LayoutMode.MainPlus3, profile.Layout);
        Assert.Equal(1, profile.FocusedAccount);
    }

    [Fact]
    public void SwapAccounts_exchanges_positions()
    {
        var profile = AppSettings.CreateDefault().Profiles[0];

        var ok = ProfileOperations.SwapAccounts(profile, "conta1", "conta3");

        Assert.True(ok);
        Assert.Equal(["conta3", "conta2", "conta1", "conta4"], profile.Accounts.Select(a => a.ProfileName));
    }

    [Fact]
    public void SwapAccounts_keeps_focus_on_same_account()
    {
        var profile = AppSettings.CreateDefault().Profiles[0];
        profile.FocusedAccount = 0; // conta1

        ProfileOperations.SwapAccounts(profile, "conta1", "conta3");

        Assert.Equal(2, profile.FocusedAccount); // conta1 agora é a terceira
        Assert.Equal("conta1", profile.Accounts[profile.FocusedAccount].ProfileName);
    }

    [Theory]
    [InlineData("conta1", "nao-existe")]
    [InlineData("nao-existe", "conta1")]
    [InlineData("conta2", "conta2")]
    public void SwapAccounts_invalid_pair_returns_false_and_changes_nothing(string a, string b)
    {
        var profile = AppSettings.CreateDefault().Profiles[0];
        profile.FocusedAccount = 1;

        var ok = ProfileOperations.SwapAccounts(profile, a, b);

        Assert.False(ok);
        Assert.Equal(["conta1", "conta2", "conta3", "conta4"], profile.Accounts.Select(x => x.ProfileName));
        Assert.Equal(1, profile.FocusedAccount);
    }

    private static string[] Order(GameProfile p) => p.Accounts.Select(a => a.ProfileName).ToArray();

    [Fact]
    public void MoveAccount_puts_the_account_before_the_target()
    {
        var profile = AppSettings.CreateDefault().Profiles[0];

        var ok = ProfileOperations.MoveAccount(profile, "conta4", "conta2", after: false);

        Assert.True(ok);
        Assert.Equal(["conta1", "conta4", "conta2", "conta3"], Order(profile));
    }

    [Fact]
    public void MoveAccount_puts_the_account_after_the_target()
    {
        var profile = AppSettings.CreateDefault().Profiles[0];

        var ok = ProfileOperations.MoveAccount(profile, "conta1", "conta3", after: true);

        Assert.True(ok);
        Assert.Equal(["conta2", "conta3", "conta1", "conta4"], Order(profile));
    }

    [Fact]
    public void MoveAccount_keeps_focus_on_the_same_account()
    {
        var profile = AppSettings.CreateDefault().Profiles[0];
        profile.FocusedAccount = 1; // conta2

        ProfileOperations.MoveAccount(profile, "conta4", "conta1", after: false); // conta4 vira a primeira

        Assert.Equal("conta2", profile.Accounts[profile.FocusedAccount].ProfileName);
        Assert.Equal(2, profile.FocusedAccount);
    }

    [Fact]
    public void MoveAccount_keeps_focus_when_the_focused_account_is_the_one_moved()
    {
        var profile = AppSettings.CreateDefault().Profiles[0];
        profile.FocusedAccount = 0; // conta1

        ProfileOperations.MoveAccount(profile, "conta1", "conta4", after: true);

        Assert.Equal("conta1", profile.Accounts[profile.FocusedAccount].ProfileName);
        Assert.Equal(3, profile.FocusedAccount);
    }

    [Theory]
    [InlineData("conta2", "conta1", true)]   // já está logo depois da conta1
    [InlineData("conta1", "conta2", false)]  // já está logo antes da conta2
    [InlineData("conta3", "conta3", false)]  // sobre si mesma
    [InlineData("conta1", "nao-existe", false)]
    [InlineData("nao-existe", "conta1", false)]
    public void MoveAccount_that_changes_nothing_returns_false_and_leaves_the_order(string account, string target, bool after)
    {
        var profile = AppSettings.CreateDefault().Profiles[0];
        profile.FocusedAccount = 2;

        var ok = ProfileOperations.MoveAccount(profile, account, target, after);

        Assert.False(ok);
        Assert.Equal(["conta1", "conta2", "conta3", "conta4"], Order(profile));
        Assert.Equal(2, profile.FocusedAccount);
    }
}
