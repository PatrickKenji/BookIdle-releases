using IdleDeck.Core.Market;
using IdleDeck.Core.Settings;

namespace IdleDeck.Core.Tests;

public class SniperRulesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("idledeck-sniper-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static SniperRule Item(long gold = 1000, long orb = 0, int itemId = 23) =>
        new() { Id = Guid.NewGuid().ToString("N")[..8], Kind = RuleKind.Item, ItemId = itemId, Label = "Cocoon Stone", MaxGold = gold, MaxOrb = orb, Enabled = true };

    private static SniperRule Shiny(long orb = 5000, long gold = 0) =>
        new() { Id = Guid.NewGuid().ToString("N")[..8], Kind = RuleKind.AnyShiny, Label = "Qualquer shiny", MaxOrb = orb, MaxGold = gold, Enabled = true };

    [Fact]
    public void A_valid_item_rule_passes()
    {
        Assert.Null(SniperRules.Validate(Item()));
        Assert.Null(SniperRules.Validate(Item(gold: 0, orb: 50)));
    }

    [Fact]
    public void Item_rule_needs_an_item_and_a_ceiling()
    {
        Assert.NotNull(SniperRules.Validate(new SniperRule { Kind = RuleKind.Item, MaxGold = 100 }));
        Assert.NotNull(SniperRules.Validate(Item(gold: 0, orb: 0)));
    }

    [Fact]
    public void Pokemon_rule_needs_a_species_and_a_ceiling()
    {
        Assert.NotNull(SniperRules.Validate(new SniperRule { Kind = RuleKind.Pokemon, MaxOrb = 100 }));
        Assert.Null(SniperRules.Validate(new SniperRule { Kind = RuleKind.Pokemon, SpeciesId = 6, MaxOrb = 100 }));
    }

    [Fact]
    public void Shiny_only_in_gems()
    {
        Assert.Null(SniperRules.Validate(Shiny(orb: 5000)));
        Assert.NotNull(SniperRules.Validate(Shiny(orb: 5000, gold: 100)));
        Assert.NotNull(SniperRules.Validate(Shiny(orb: 0)));
    }

    [Fact]
    public void Nota_rule_needs_a_cutoff_above_zero_and_a_ceiling()
    {
        Assert.NotNull(SniperRules.Validate(new SniperRule { Kind = RuleKind.MinNota, MinNota = 0, MaxOrb = 10 }));
        Assert.NotNull(SniperRules.Validate(new SniperRule { Kind = RuleKind.MinNota, MinNota = 7 }));
        Assert.Null(SniperRules.Validate(new SniperRule { Kind = RuleKind.MinNota, MinNota = 7, MaxGold = 250000 }));
    }

    [Fact]
    public void Negative_numbers_are_rejected()
    {
        Assert.NotNull(SniperRules.Validate(Item(gold: -1, orb: 5)));
        var rule = Item();
        rule.MaxQty = -1;
        Assert.NotNull(SniperRules.Validate(rule));
    }

    [Fact]
    public void An_item_or_pokemon_rule_over_the_limit_is_refused_but_radars_have_their_own_room()
    {
        var cfg = new SniperAccountConfig();
        for (var i = 0; i < SniperRules.MaxRules; i++) cfg.Rules.Add(Item(itemId: 100 + i));

        Assert.NotNull(SniperRules.CanAdd(cfg, Item(itemId: 999)));
        Assert.Null(SniperRules.CanAdd(cfg, Shiny()));
    }

    [Fact]
    public void Second_radar_of_the_same_kind_is_refused()
    {
        var cfg = new SniperAccountConfig { Rules = [Shiny()] };
        Assert.NotNull(SniperRules.CanAdd(cfg, Shiny()));
        Assert.Null(SniperRules.CanAdd(cfg, new SniperRule { Kind = RuleKind.AnyP5, MaxGold = 10 }));
    }

    [Fact]
    public void Remaining_counts_down_and_never_goes_negative()
    {
        var rule = Item();
        Assert.Equal(long.MaxValue, SniperRules.Remaining(rule));
        rule.MaxQty = 3;
        rule.Bought = 1;
        Assert.Equal(2, SniperRules.Remaining(rule));
        rule.Bought = 9;
        Assert.Equal(0, SniperRules.Remaining(rule));
    }

    [Fact]
    public void Normalize_drops_nulls_invalid_rules_repeats_and_excess()
    {
        var items = Enumerable.Range(1, SniperRules.MaxRules + 2).Select(i => Item(itemId: i)).ToList();
        var cfg = new SniperAccountConfig
        {
            ReserveGold = -5,
            ReserveOrb = -1,
            Rules = [null!, Shiny(orb: 5000, gold: 100), Shiny(), Shiny(orb: 1), .. items], // nula, shiny com coins, radar repetido, excesso
        };

        SniperRules.Normalize(cfg);

        Assert.Equal(0, cfg.ReserveGold);
        Assert.Equal(0, cfg.ReserveOrb);
        Assert.Equal(1 + SniperRules.MaxRules, cfg.Rules.Count);                // 1 radar + o máximo de regras
        Assert.Single(cfg.Rules, r => r.Kind == RuleKind.AnyShiny);
        Assert.Equal(Enumerable.Range(1, SniperRules.MaxRules), cfg.Rules.Where(r => r.Kind == RuleKind.Item).Select(r => r.ItemId!.Value));
    }

    [Fact]
    public void Fifteen_rules_fit_and_the_sixteenth_does_not()
    {
        Assert.Equal(15, SniperRules.MaxRules);
        var cfg = new SniperAccountConfig();
        for (var i = 1; i <= 15; i++)
        {
            var rule = Item(itemId: i);
            Assert.Null(SniperRules.CanAdd(cfg, rule));
            cfg.Rules.Add(rule);
        }
        Assert.NotNull(SniperRules.CanAdd(cfg, Item(itemId: 99)));
    }

    [Fact]
    public void Normalize_gives_distinct_ids_and_handles_a_null_list()
    {
        var a = Item(itemId: 1);
        var b = Item(itemId: 2);
        a.Id = "x";
        b.Id = "x";
        var cfg = new SniperAccountConfig { Rules = [a, b] };
        SniperRules.Normalize(cfg);
        Assert.Equal(2, cfg.Rules.Select(r => r.Id).Distinct().Count());
        Assert.All(cfg.Rules, r => Assert.False(string.IsNullOrWhiteSpace(r.Id)));

        var broken = new SniperAccountConfig { Rules = null! };
        SniperRules.Normalize(broken);
        Assert.Empty(broken.Rules);
    }

    [Fact]
    public void Settings_without_sniper_load_empty()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """
            { "Version": 2, "Profiles": [ { "Id": "p1", "Name": "PokeIdle", "Url": "https://pokeidle.io", "MaxAccounts": 4,
              "Accounts": [ { "Name": "Conta 1", "ProfileName": "conta1" } ] } ], "SelectedProfileId": "p1" }
            """);

        var settings = new SettingsStore(path).Load();

        Assert.Empty(settings.Profiles[0].Sniper);
    }

    [Fact]
    public void Sniper_settings_roundtrip_per_account_and_forget_removed_accounts()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(path);
        var settings = AppSettings.CreateDefault();
        var profile = settings.Profiles[0];
        profile.Sniper["conta1"] = new SniperAccountConfig { Enabled = true, ReserveGold = 1000, ReserveOrb = 5, Rules = [Item(), Shiny()] };
        profile.Sniper["fantasma"] = new SniperAccountConfig { Enabled = true };

        store.Save(settings);
        var loaded = store.Load().Profiles[0];

        var cfg = Assert.Single(loaded.Sniper).Value;
        Assert.True(cfg.Enabled);
        Assert.Equal(1000, cfg.ReserveGold);
        Assert.Equal(5, cfg.ReserveOrb);
        Assert.Equal([RuleKind.Item, RuleKind.AnyShiny], cfg.Rules.Select(r => r.Kind));
        Assert.Equal("conta1", loaded.Sniper.Keys.Single());
        Assert.Contains("\"Item\"", File.ReadAllText(path)); // enum como texto
    }

    [Fact]
    public void Hand_edited_file_with_null_sniper_entries_still_loads()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """
            { "Version": 2, "Profiles": [ { "Id": "p1", "Name": "PokeIdle", "Url": "https://pokeidle.io", "MaxAccounts": 4,
              "Accounts": [ { "Name": "Conta 1", "ProfileName": "conta1" } ],
              "Sniper": { "conta1": { "Enabled": true, "Rules": [ null, { "Kind": "AnyShiny", "MaxGold": 10, "MaxOrb": 5 } ] }, "x": null } } ],
              "SelectedProfileId": "p1" }
            """);

        var loaded = new SettingsStore(path).Load().Profiles[0];

        Assert.Empty(loaded.Sniper["conta1"].Rules); // regra de shiny com coins foi descartada
        Assert.False(loaded.Sniper.ContainsKey("x"));
    }
}
