using IdleDeck.Core.Audio;
using IdleDeck.Core.Settings;

namespace IdleDeck.Core.Tests;

public class MuteRuleTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("idledeck-mute-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void TargetState_mutes_all_when_some_are_unmuted()
    {
        Assert.True(MuteRule.TargetState([true, false]));
        Assert.True(MuteRule.TargetState([false, false]));
    }

    [Fact]
    public void TargetState_unmutes_when_all_are_muted()
    {
        Assert.False(MuteRule.TargetState([true, true]));
    }

    [Fact]
    public void TargetState_with_no_accounts_is_true()
    {
        Assert.True(MuteRule.TargetState([]));
    }

    [Fact]
    public void Old_settings_without_Muted_load_unmuted()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """
            { "Version": 2, "Profiles": [ { "Id": "p1", "Name": "PokeIdle", "Url": "https://pokeidle.io", "MaxAccounts": 4,
              "Accounts": [ { "Name": "Conta 1", "ProfileName": "conta1", "Login": "None" } ] } ], "SelectedProfileId": "p1" }
            """);

        var settings = new SettingsStore(path).Load();

        var account = Assert.Single(settings.Profiles[0].Accounts);
        Assert.False(account.Muted);
        Assert.Null(account.LastVoteUtc);
        Assert.Equal("conta1", account.ProfileName); // o arquivo antigo foi aproveitado
    }

    [Fact]
    public void Muted_roundtrips()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(path);
        var settings = AppSettings.CreateDefault();
        settings.Profiles[0].Accounts[1] = settings.Profiles[0].Accounts[1] with { Muted = true };

        store.Save(settings);

        var loaded = store.Load();
        Assert.True(loaded.Profiles[0].Accounts[1].Muted);
        Assert.False(loaded.Profiles[0].Accounts[0].Muted);
    }
}
