using IdleDeck.Core.Settings;

namespace IdleDeck.Core.Tests;

public class TwitchSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("idledeck-twitch-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Old_settings_without_twitch_fields_load_with_defaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """
            { "Version": 2, "MemoryLimitMb": 640, "Profiles": [ { "Id": "p1", "Name": "PokeIdle", "Url": "https://pokeidle.io", "MaxAccounts": 4,
              "Accounts": [ { "Name": "Conta 1", "ProfileName": "conta1" } ] } ], "SelectedProfileId": "p1" }
            """);

        var settings = new SettingsStore(path).Load();

        Assert.Equal(640, settings.MemoryLimitMb); // aproveitou o arquivo antigo
        Assert.Empty(settings.Profiles[0].TwitchChannels);
        Assert.Null(settings.Profiles[0].Accounts[0].TwitchLogin);
    }

    [Fact]
    public void Twitch_settings_roundtrip_and_channels_are_normalized_on_load()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(path);
        var settings = AppSettings.CreateDefault();
        settings.Profiles[0].TwitchChannels = ["  #Canal ", "https://twitch.tv/Outro", "canal"];
        settings.Profiles[0].Accounts[0] = settings.Profiles[0].Accounts[0] with { TwitchLogin = "treinador_tv" };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(["canal", "outro"], loaded.Profiles[0].TwitchChannels);
        Assert.Equal("treinador_tv", loaded.Profiles[0].Accounts[0].TwitchLogin);
    }

    [Fact]
    public void Null_channels_in_the_file_become_an_empty_list()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """
            { "Version": 2, "Profiles": [ { "Id": "p1", "Name": "PokeIdle", "Url": "https://pokeidle.io", "MaxAccounts": 4,
              "TwitchChannels": null, "Accounts": [] } ], "SelectedProfileId": "p1" }
            """);

        var settings = new SettingsStore(path).Load();

        Assert.Empty(settings.Profiles[0].TwitchChannels);
    }
}
