using IdleDeck.Core.Settings;

namespace IdleDeck.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("idledeck-settings-").FullName;
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void CreateDefault_has_pokeidle_with_four_accounts()
    {
        var s = AppSettings.CreateDefault();

        var p = Assert.Single(s.Profiles);
        Assert.Equal("pokeidle", p.Id);
        Assert.Equal("PokeIdle", p.Name);
        Assert.Equal("https://pokeidle.io", p.Url);
        Assert.Equal(4, p.MaxAccounts);
        Assert.Equal(4, p.Accounts.Count);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal($"Conta {i + 1}", p.Accounts[i].Name);
            Assert.Equal($"conta{i + 1}", p.Accounts[i].ProfileName);
            Assert.Equal(LoginMethod.None, p.Accounts[i].Login);
        }
        Assert.Equal(LayoutMode.Tabs, p.Layout);
        Assert.Same(p, s.SelectedProfile);
        Assert.Equal(AppSettings.CurrentVersion, s.Version);
        Assert.Equal(600, s.MemoryLimitMb);
        Assert.True(s.AutoResetEnabled);
        Assert.True(s.DisableBackgroundThrottling);
        Assert.False(s.ExtensionsEnabled);
        Assert.False(s.StartWithWindows);
    }

    [Fact]
    public void Save_then_Load_roundtrips()
    {
        var store = new SettingsStore(FilePath);
        var s = AppSettings.CreateDefault();
        s.Profiles[0].Layout = LayoutMode.MainPlus3;
        s.Profiles[0].FocusedAccount = 2;
        s.Profiles[0].Collapsed = true;
        s.Profiles[0].Accounts[1] = s.Profiles[0].Accounts[1] with { Name = "Principal", Login = LoginMethod.Google };
        s.Profiles.Add(new GameProfile
        {
            Id = "game-0a1b2c3d", Name = "Outro", Url = "https://example.com/", MaxAccounts = 2,
            Accounts = [new AccountConfig("X", "acc-0000aaaa", LoginMethod.Password)],
        });
        s.SelectedProfileId = "game-0a1b2c3d";
        s.MemoryLimitMb = 450;

        store.Save(s);
        var loaded = store.Load();

        Assert.Equal(2, loaded.Profiles.Count);
        Assert.Equal(LayoutMode.MainPlus3, loaded.Profiles[0].Layout);
        Assert.Equal(2, loaded.Profiles[0].FocusedAccount);
        Assert.True(loaded.Profiles[0].Collapsed);
        Assert.Equal(new AccountConfig("Principal", "conta2", LoginMethod.Google), loaded.Profiles[0].Accounts[1]);
        Assert.Equal("Outro", loaded.SelectedProfile.Name);
        Assert.Equal(450, loaded.MemoryLimitMb);
        Assert.Equal(s.BlockedDomains, loaded.BlockedDomains);
    }

    [Fact]
    public void Load_missing_file_returns_defaults()
    {
        var loaded = new SettingsStore(FilePath).Load();

        Assert.Equal(4, loaded.SelectedProfile.Accounts.Count);
    }

    [Fact]
    public void Load_corrupt_file_returns_defaults_and_renames_to_bad()
    {
        File.WriteAllText(FilePath, "{lixo");

        var loaded = new SettingsStore(FilePath).Load();

        Assert.Equal(4, loaded.SelectedProfile.Accounts.Count);
        Assert.True(File.Exists(FilePath + ".bad"));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Load_file_missing_fields_uses_defaults()
    {
        File.WriteAllText(FilePath, """{ "MemoryLimitMb": 700 }""");

        var loaded = new SettingsStore(FilePath).Load();

        Assert.Equal(700, loaded.MemoryLimitMb);
        Assert.Equal(AppSettings.CreateDefault().BlockedDomains, loaded.BlockedDomains);
        Assert.Equal(4, loaded.SelectedProfile.Accounts.Count);
        Assert.False(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void Load_keeps_user_emptied_block_list()
    {
        File.WriteAllText(FilePath, """{ "Version": 2, "BlockedDomains": [] }""");

        Assert.Empty(new SettingsStore(FilePath).Load().BlockedDomains);
    }

    [Fact]
    public void Profile_without_accounts_is_valid()
    {
        var store = new SettingsStore(FilePath);
        var s = AppSettings.CreateDefault();
        s.Profiles.Add(new GameProfile { Id = "game-11112222", Name = "Vazio", Url = "https://example.com/" });
        store.Save(s);

        var loaded = store.Load();

        Assert.Empty(loaded.Profiles[1].Accounts);
        Assert.False(File.Exists(FilePath + ".bad"));
    }

    private const string ValidProfile = """{ "Id": "pokeidle", "Name": "P", "Url": "https://pokeidle.io", "MaxAccounts": 4, "Accounts": [ { "Name": "a", "ProfileName": "conta1" } ] }""";

    [Theory]
    [InlineData("""{ "Version": 2, "Profiles": [null] }""")]
    [InlineData("""{ "Version": 2, "Profiles": [ { "Id": "pokeidle", "Name": "P", "Url": "nao-e-url", "Accounts": [] } ] }""")]
    [InlineData("""{ "Version": 2, "Profiles": [ { "Id": "pokeidle", "Name": "P", "Url": "https://pokeidle.io", "MaxAccounts": 0, "Accounts": [] } ] }""")]
    [InlineData("""{ "Version": 2, "Profiles": [ { "Id": "pokeidle", "Name": "P", "Url": "https://pokeidle.io", "MaxAccounts": 21, "Accounts": [] } ] }""")]
    [InlineData("""{ "Version": 2, "Profiles": [ { "Id": "pokeidle", "Name": "P", "Url": "https://pokeidle.io", "Accounts": [ null ] } ] }""")]
    [InlineData("""{ "Version": 2, "Profiles": [ { "Id": "pokeidle", "Name": "P", "Url": "https://pokeidle.io", "Accounts": [ { "Name": "a", "ProfileName": "../fora" } ] } ] }""")]
    [InlineData("""{ "Version": 2, "Profiles": [ { "Id": "pokeidle", "Name": "", "Url": "https://pokeidle.io", "Accounts": [] } ] }""")]
    [InlineData("{ \"Version\": 2, \"Profiles\": [ " + ValidProfile + ", { \"Id\": \"game-1\", \"Name\": \"Q\", \"Url\": \"https://x.io\", \"Accounts\": [ { \"Name\": \"b\", \"ProfileName\": \"conta1\" } ] } ] }")]
    [InlineData("{ \"Version\": 2, \"Profiles\": [ " + ValidProfile + ", " + ValidProfile + " ] }")]
    public void Load_invalid_profiles_returns_defaults_and_renames_to_bad(string json)
    {
        File.WriteAllText(FilePath, json);

        var loaded = new SettingsStore(FilePath).Load();

        Assert.Equal(AppSettings.CreateDefault().Profiles[0].Accounts, loaded.Profiles[0].Accounts);
        Assert.True(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void Load_unknown_selected_profile_selects_first()
    {
        File.WriteAllText(FilePath, "{ \"Version\": 2, \"SelectedProfileId\": \"nada\", \"Profiles\": [ " + ValidProfile + " ] }");

        Assert.Equal("pokeidle", new SettingsStore(FilePath).Load().SelectedProfileId);
    }

    [Fact]
    public void Load_clamps_focused_account()
    {
        var store = new SettingsStore(FilePath);
        var s = AppSettings.CreateDefault();
        s.Profiles[0].FocusedAccount = 9;
        store.Save(s);

        Assert.Equal(0, store.Load().Profiles[0].FocusedAccount);
    }

    [Fact]
    public void Load_locked_file_returns_defaults_without_throwing()
    {
        File.WriteAllText(FilePath, "{}");
        using var locked = new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var loaded = new SettingsStore(FilePath).Load();

        Assert.Equal(4, loaded.SelectedProfile.Accounts.Count);
    }

    [Fact]
    public void Zoom_defaults_to_100_and_roundtrips()
    {
        var store = new SettingsStore(FilePath);
        var s = AppSettings.CreateDefault();
        Assert.Equal(1.0, s.Profiles[0].Zoom);

        s.Profiles[0].Zoom = 0.8;
        store.Save(s);

        Assert.Equal(0.8, store.Load().Profiles[0].Zoom);
    }

    [Theory]
    [InlineData(0.1, 0.5)]
    [InlineData(9.0, 1.5)]
    [InlineData(0.0, 1.0)]
    public void Zoom_out_of_range_is_clamped(double saved, double expected)
    {
        var store = new SettingsStore(FilePath);
        var s = AppSettings.CreateDefault();
        s.Profiles[0].Zoom = saved;
        store.Save(s);

        var loaded = store.Load();

        Assert.Equal(expected, loaded.Profiles[0].Zoom);
        Assert.False(File.Exists(FilePath + ".bad"));
    }
}
