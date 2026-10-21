using System.Text.RegularExpressions;
using IdleDeck.Core.Settings;
using IdleDeck.Core.Theme;

namespace IdleDeck.Core.Tests;

public class ThemePalettesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("idledeck-theme-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Both_palettes_define_the_same_keys()
    {
        var arcade = ThemePalettes.For(ThemeKind.Arcade).Keys.OrderBy(k => k);
        var dark = ThemePalettes.For(ThemeKind.Dark).Keys.OrderBy(k => k);

        Assert.Equal(arcade, dark);
    }

    [Theory]
    [InlineData(ThemeKind.Arcade)]
    [InlineData(ThemeKind.Dark)]
    public void Every_value_is_a_hex_color(ThemeKind kind)
    {
        foreach (var (key, value) in ThemePalettes.For(kind))
            Assert.True(Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}$"), $"{kind}.{key} = {value}");
    }

    [Fact]
    public void Dark_differs_from_Arcade_for_Bg_and_Purple_but_keeps_Yellow()
    {
        var arcade = ThemePalettes.For(ThemeKind.Arcade);
        var dark = ThemePalettes.For(ThemeKind.Dark);

        Assert.Equal("#1B1030", arcade["Bg"]);
        Assert.Equal("#121214", dark["Bg"]);
        Assert.Equal("#9D8CFF", dark["Purple"]);
        Assert.Equal(arcade["Yellow"], dark["Yellow"]);
    }

    [Fact]
    public void Old_settings_without_Theme_load_as_Arcade_and_are_not_discarded()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "Version": 2, "MemoryLimitMb": 777 }""");

        var settings = new SettingsStore(path).Load();

        Assert.Equal(ThemeKind.Arcade, settings.Theme);
        Assert.Equal(777, settings.MemoryLimitMb); // o arquivo antigo foi aproveitado, não trocado pelo padrão
        Assert.False(File.Exists(path + ".bad"));
    }

    [Fact]
    public void Theme_roundtrips_as_text()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(path);
        var settings = AppSettings.CreateDefault();
        settings.Theme = ThemeKind.Dark;

        store.Save(settings);

        Assert.Contains("\"Dark\"", File.ReadAllText(path));
        Assert.Equal(ThemeKind.Dark, store.Load().Theme);
    }
}
