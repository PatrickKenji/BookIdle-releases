using IdleDeck.Core.Settings;

namespace IdleDeck.Core.Tests;

public class SettingsMigrationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("idledeck-migration-").FullName;
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string V1 = """
        {
          "Accounts": [
            { "Name": "Main",    "Url": "https://pokeidle.io", "ProfileName": "conta1", "AutoLoginEnabled": true },
            { "Name": "Conta 2", "Url": "https://pokeidle.io", "ProfileName": "conta2", "AutoLoginEnabled": true },
            { "Name": "Conta 3", "Url": "https://pokeidle.io", "ProfileName": "conta3", "AutoLoginEnabled": false },
            { "Name": "Conta 4", "Url": "https://pokeidle.io", "ProfileName": "conta4", "AutoLoginEnabled": true }
          ],
          "Layout": "MainPlus3",
          "FocusedAccount": 2,
          "MemoryLimitMb": 450,
          "AutoResetEnabled": true,
          "DisableBackgroundThrottling": true,
          "ExtensionsEnabled": false,
          "StartWithWindows": false,
          "BlockedDomains": [ "doubleclick.net" ]
        }
        """;

    [Fact]
    public void V1_file_becomes_pokeidle_profile()
    {
        File.WriteAllText(FilePath, V1);

        var s = new SettingsStore(FilePath).Load();

        Assert.Equal(2, s.Version);
        var p = Assert.Single(s.Profiles);
        Assert.Equal("pokeidle", p.Id);
        Assert.Equal("PokeIdle", p.Name);
        Assert.Equal("https://pokeidle.io", p.Url);
        Assert.Equal(4, p.MaxAccounts);
        Assert.Equal(
            [
                new AccountConfig("Main", "conta1", LoginMethod.Password),
                new AccountConfig("Conta 2", "conta2", LoginMethod.Password),
                new AccountConfig("Conta 3", "conta3", LoginMethod.None),
                new AccountConfig("Conta 4", "conta4", LoginMethod.Password),
            ],
            p.Accounts);
        Assert.Equal(LayoutMode.MainPlus3, p.Layout);
        Assert.Equal(2, p.FocusedAccount);
        Assert.Equal("pokeidle", s.SelectedProfileId);
        Assert.Equal(450, s.MemoryLimitMb);
        Assert.Equal(["doubleclick.net"], s.BlockedDomains);
        Assert.False(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void V1_profile_url_comes_from_first_account()
    {
        File.WriteAllText(FilePath, V1.Replace("""{ "Name": "Main",    "Url": "https://pokeidle.io",""", """{ "Name": "Main",    "Url": "https://pokeidle.io/play","""));

        Assert.Equal("https://pokeidle.io/play", new SettingsStore(FilePath).Load().Profiles[0].Url);
    }

    [Fact]
    public void V1_migrated_then_saved_is_v2()
    {
        File.WriteAllText(FilePath, V1);
        var store = new SettingsStore(FilePath);

        store.Save(store.Load());
        var json = File.ReadAllText(FilePath);

        Assert.Contains("\"Version\": 2", json);
        Assert.Contains("\"Profiles\"", json);
        Assert.DoesNotContain("AutoLoginEnabled", json);
    }
}
