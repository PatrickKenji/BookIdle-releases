using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using IdleDeck.Core.Market;

namespace IdleDeck.App.Modules.Sniper;

/// <summary>
/// Painel do Sniper. No topo, uma aba por conta (com a chave liga/desliga dela); embaixo, três colunas numa tela só:
/// nova regra + radar de Pokémon, regras ativas + proteção de saldo, e o feed. Tudo vale para a conta da aba escolhida.
/// </summary>
public sealed class SniperPanel : DockPanel
{
    private static CultureInfo Br => Loc.Culture;
    private const int MaxSuggestions = 60;

    private readonly SniperModule _module;
    private readonly IModuleHostServices _host;
    private readonly WrapPanel _tabs = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly DockPanel _header = new() { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };
    private readonly Grid _body = new();
    private readonly StackPanel _form = new();
    private readonly StackPanel _rules = new();
    private readonly StackPanel _ruleList = new();
    private readonly TextBlock _rulesTitle;
    private readonly StackPanel _feed = new();
    private readonly ScrollViewer _feedScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    // nova regra
    private readonly WrapPanel _chips = new();
    private readonly TextBox _search = Campo();
    private readonly Popup _popup = new();
    private readonly StackPanel _suggestions = new();
    private readonly Border _preview = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8, 6, 8, 6) };
    private readonly TextBlock _previewName = new() { FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _previewLabel = new();
    private readonly TextBox _goldCeiling = Campo("0");
    private readonly TextBox _orbCeiling = Campo("0");
    private readonly TextBox _quantity = Campo("1");
    private readonly CheckBox _infinite = new() { Content = Loc.T("∞ Sem limite"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _formError = new() { TextWrapping = TextWrapping.Wrap };

    // radar
    private readonly Dictionary<RuleKind, RadarUi> _radars = [];
    private readonly TextBlock _radarError = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _radarBadge = new();

    // proteção de saldo
    private readonly TextBox _reserveGold = Campo("0");
    private readonly TextBox _reserveOrb = Campo("0");

    private int? _selected;
    private int? _formAccount;      // de que conta o formulário foi carregado
    private string? _group;         // chip escolhido (null = Todas)
    private SearchEntry? _picked;
    private bool _loadingForm;

    private sealed record RadarUi(RuleKind Kind, CheckBox Box, TextBox Gold, TextBox Orb, TextBox Nota);

    public SniperPanel(SniperModule module, IModuleHostServices host)
    {
        _module = module;
        _host = host;
        _host.ContasMudaram += (_, _) => Refresh();

        SetDock(_tabs, Dock.Top);
        SetDock(_header, Dock.Top);
        Children.Add(_tabs);
        Children.Add(_header);

        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });
        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        _rulesTitle = Titulo(Loc.T("REGRAS DE COMPRA ATIVAS"));
        _rules.Children.Add(_rulesTitle);
        _rules.Children.Add(_ruleList);
        _rules.Children.Add(BuildReserve());
        _body.Children.Add(Coluna(Card(_form, scroll: true), 0));
        _body.Children.Add(Coluna(Card(_rules, scroll: true), 1));
        _body.Children.Add(Coluna(Card(FeedColumn()), 2));
        Children.Add(_body);

        BuildForm();
        Refresh();
    }

    // ---------- Peças ----------

    private static FrameworkElement Coluna(FrameworkElement card, int column)
    {
        Grid.SetColumn(card, column);
        card.Margin = new Thickness(column == 0 ? 0 : 8, 0, 0, 0);
        return card;
    }

    private static Border Card(UIElement content, bool scroll = false) => new()
    {
        Style = (Style)Application.Current.Resources["ArcadeCard"],
        Padding = new Thickness(10),
        Child = scroll ? new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content } : content,
    };

    private UIElement FeedColumn()
    {
        var dock = new DockPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var clear = new Button { Content = Loc.T("LIMPAR"), Padding = new Thickness(8, 2, 8, 2) };
        clear.Click += (_, _) => { if (_selected is { } id) _module.ClearFeed(id); };
        SetDock(clear, Dock.Right);
        top.Children.Add(clear);
        top.Children.Add(Titulo(Loc.T("FEED DE EVENTOS")));
        SetDock(top, Dock.Top);
        dock.Children.Add(top);
        _feedScroll.Content = _feed;
        dock.Children.Add(_feedScroll);
        return dock;
    }

    private static TextBox Campo(string text = "") => new() { Text = text, Margin = new Thickness(0, 2, 0, 6) };

    private static TextBlock Texto(string text, bool muted = false, bool bold = false, string? color = null, Thickness? margin = null)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0, 0, 0, 2) };
        block.SetResourceReference(TextBlock.ForegroundProperty, color ?? (muted ? "Muted" : "Text"));
        if (bold) block.FontWeight = FontWeights.Bold;
        return block;
    }

    private static TextBlock Titulo(string text, Thickness? margin = null)
    {
        var block = new TextBlock { Text = text, Margin = margin ?? new Thickness(0, 0, 0, 6) };
        block.SetResourceReference(FrameworkElement.StyleProperty, "SectionTitle");
        return block;
    }

    private static StackPanel Mini(string label, UIElement box, double width = 0)
    {
        if (width > 0 && box is FrameworkElement fe) fe.Width = width;
        var panel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        panel.Children.Add(Texto(label, muted: true));
        panel.Children.Add(box);
        return panel;
    }

    private static void ApplyStyle(FrameworkElement element, string key) => element.SetResourceReference(FrameworkElement.StyleProperty, key);

    // ---------- Formulário (montado uma vez) ----------

    private void BuildForm()
    {
        _form.Children.Add(Titulo(Loc.T("NOVA REGRA DE COMPRA")));
        _form.Children.Add(_chips);
        _form.Children.Add(Texto(Loc.T("BUSCAR ITEM / POKÉMON"), muted: true, margin: new Thickness(0, 6, 0, 0)));
        _search.ToolTip = Loc.T("Digite para filtrar; clique numa sugestão para escolher.");
        _form.Children.Add(_search);
        BuildPopup();

        _preview.SetResourceReference(Border.BackgroundProperty, "Field");
        _preview.SetResourceReference(Border.BorderBrushProperty, "Yellow");
        _preview.BorderThickness = new Thickness(2);
        _preview.CornerRadius = new CornerRadius(6);
        var previewRow = new DockPanel();
        var clear = new Button { Content = Loc.T("Limpar"), Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center };
        clear.Click += (_, _) => Pick(null);
        SetDock(clear, Dock.Right);
        previewRow.Children.Add(clear);
        var previewText = new StackPanel();
        _previewName.SetResourceReference(TextBlock.ForegroundProperty, "Yellow");
        _previewLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        previewText.Children.Add(_previewName);
        previewText.Children.Add(_previewLabel);
        previewRow.Children.Add(previewText);
        _preview.Child = previewRow;
        _form.Children.Add(_preview);

        var ceilings = new Grid();
        ceilings.ColumnDefinitions.Add(new ColumnDefinition());
        ceilings.ColumnDefinitions.Add(new ColumnDefinition());
        var coins = Mini(Loc.T("Teto em coins (GP)"), _goldCeiling);
        var gems = Mini(Loc.T("Teto em gemas"), _orbCeiling);
        Grid.SetColumn(gems, 1);
        gems.Margin = new Thickness(0);
        ceilings.Children.Add(coins);
        ceilings.Children.Add(gems);
        _form.Children.Add(ceilings);

        var qtyHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        SetDock(_infinite, Dock.Right);
        qtyHeader.Children.Add(_infinite);
        qtyHeader.Children.Add(Texto(Loc.T("QUANTIDADE MÁXIMA"), muted: true, margin: new Thickness(0)));
        _form.Children.Add(qtyHeader);
        _infinite.Checked += (_, _) => _quantity.IsEnabled = false;
        _infinite.Unchecked += (_, _) => _quantity.IsEnabled = true;
        _form.Children.Add(_quantity);

        _formError.SetResourceReference(TextBlock.ForegroundProperty, "Err");
        _form.Children.Add(_formError);
        var add = new Button { Content = Loc.T("CADASTRAR REGRA DE COMPRA"), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 2, 0, 0) };
        ApplyStyle(add, "ArcadePrimaryButton");
        add.Click += (_, _) => AddRule();
        _form.Children.Add(add);

        var radarHeader = new DockPanel { Margin = new Thickness(0, 14, 0, 4) };
        _radarBadge.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        SetDock(_radarBadge, Dock.Right);
        radarHeader.Children.Add(_radarBadge);
        radarHeader.Children.Add(Titulo(Loc.T("RADAR GLOBAL DE POKÉMON"), new Thickness(0)));
        _form.Children.Add(radarHeader);
        AddRadar(RuleKind.AnyShiny, Loc.T("Auto-snipe qualquer shiny (só gemas)"), gold: false, nota: false);
        AddRadar(RuleKind.AnyP5, Loc.T("Auto-snipe qualquer P5 (potência 5)"), gold: true, nota: false);
        AddRadar(RuleKind.MinNota, Loc.T("Auto-snipe por nota mínima"), gold: true, nota: true);
        _radarError.SetResourceReference(TextBlock.ForegroundProperty, "Err");
        _form.Children.Add(_radarError);

        _search.TextChanged += (_, _) => OpenSuggestions();
        _search.GotKeyboardFocus += (_, _) => OpenSuggestions();
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _popup.IsOpen) { _popup.IsOpen = false; e.Handled = true; }
        };
        RebuildChips();
    }

    private void RebuildChips()
    {
        _chips.Children.Clear();
        AddChip(Loc.T("Todas"), null);
        foreach (var group in ItemCategories.Chips) AddChip(ItemCategories.Label(group), group);
    }

    private void AddChip(string label, string? group)
    {
        var chip = new Button { Content = label, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 2, 8, 2) };
        ApplyStyle(chip, _group == group ? "ArcadeSelectedButton" : "ArcadeButton");
        chip.Click += (_, _) =>
        {
            _group = group;
            RebuildChips();
            OpenSuggestions();
        };
        _chips.Children.Add(chip);
    }

    // ---------- Busca com sugestões ----------

    private void BuildPopup()
    {
        var box = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(6), Padding = new Thickness(4), MaxHeight = 300 };
        box.SetResourceReference(Border.BackgroundProperty, "Card");
        box.SetResourceReference(Border.BorderBrushProperty, "Purple");
        box.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _suggestions };
        _popup.Child = box;
        _popup.PlacementTarget = _search;
        _popup.Placement = PlacementMode.Bottom;
        _popup.StaysOpen = false;
        _popup.AllowsTransparency = true;
    }

    private void OpenSuggestions()
    {
        _suggestions.Children.Clear();
        var found = SniperSearch.Find(_module.Catalog, _module.Species, _search.Text, _group, MaxSuggestions);
        foreach (var entry in found) _suggestions.Children.Add(SuggestionRow(entry));

        if (found.Count == 0)
        {
            var empty = _group == ItemCategories.Pokemon && _module.Species.All.Count == 0
                ? Loc.T("A lista de Pokémon é aprendida do mercado do jogo: abra o jogo nesta conta e espere alguns segundos.")
                : _module.Catalog is null ? Loc.T("Carregando a lista de itens do jogo…") : Loc.T("Nada encontrado com esse nome.");
            _suggestions.Children.Add(Texto(empty, muted: true, margin: new Thickness(6)));
        }

        _popup.Width = Math.Max(260, _search.ActualWidth);
        _popup.IsOpen = _search.IsKeyboardFocusWithin || _chips.IsKeyboardFocusWithin || _popup.IsOpen;
    }

    private Button SuggestionRow(SearchEntry entry)
    {
        var row = new DockPanel();
        var label = Texto(entry.Label, muted: true, margin: new Thickness(10, 0, 0, 0));
        SetDock(label, Dock.Right);
        row.Children.Add(label);
        row.Children.Add(Texto(entry.Name, margin: new Thickness(0)));
        var button = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 0, 0, 2) };
        button.Click += (_, _) => Pick(entry);
        return button;
    }

    private void Pick(SearchEntry? entry)
    {
        _picked = entry;
        _popup.IsOpen = false;
        _preview.Visibility = entry is null ? Visibility.Collapsed : Visibility.Visible;
        _previewName.Text = entry?.Name ?? "";
        _previewLabel.Text = entry is null ? "" : entry.Kind == SearchKind.Pokemon ? "Pokémon" : Loc.T("Item · {0}", entry.Label);
        _search.Text = "";
        _formError.Text = "";
    }

    // ---------- Radar ----------

    private void AddRadar(RuleKind kind, string title, bool gold, bool nota)
    {
        var box = new CheckBox { Content = title, Margin = new Thickness(0, 4, 0, 2) };
        var ui = new RadarUi(kind, box, Campo("0"), Campo("0"), Campo("7"));
        _radars[kind] = ui;

        _form.Children.Add(box);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (nota) row.Children.Add(Mini(Loc.T("Nota"), ui.Nota, 70));
        if (gold) row.Children.Add(Mini(Loc.T("Coins"), ui.Gold, 100));
        row.Children.Add(Mini(Loc.T("Gemas"), ui.Orb, 100));
        _form.Children.Add(row);

        box.Click += (_, _) => ApplyRadar(ui);
        foreach (var field in new[] { ui.Gold, ui.Orb, ui.Nota })
            field.LostFocus += (_, _) => { if (box.IsChecked == true) ApplyRadar(ui); };
    }

    // ---------- Atualização ----------

    public void Refresh()
    {
        var accounts = _module.Accounts;
        if (_selected is null || !accounts.Contains(_selected.Value)) _selected = accounts.FirstOrDefault();
        BuildTabs(accounts);
        BuildHeader();
        BuildRules();
        BuildFeed();
        if (_selected is { } id && _formAccount != id) LoadForm(id);
        if (_popup.IsOpen) OpenSuggestions(); // chegou item/Pokémon novo na lista enquanto a busca está aberta
    }

    /// <summary>Só o cabeçalho (saldo e chave da conta), para o saldo andar sem refazer o resto da tela.</summary>
    public void RefreshHeader() => BuildHeader();

    private void BuildTabs(IReadOnlyList<int> accounts)
    {
        _tabs.Children.Clear();
        foreach (var id in accounts)
        {
            var (dot, color) = _module.StateOf(id) switch
            {
                SniperState.Watching => ("●", "Green"),
                SniperState.BoughtRecently => ("★", "Yellow"),
                SniperState.Error => ("!", "Err"),
                _ => ("○", "Muted"),
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(Texto(dot, color: color, margin: new Thickness(0, 0, 6, 0)));
            content.Children.Add(Texto(_module.AccountName(id), margin: new Thickness(0)));
            var tab = new Button { Content = content, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(10, 4, 10, 4) };
            ApplyStyle(tab, id == _selected ? "ArcadeTabSelected" : "ArcadeTab");
            var captured = id;
            tab.Click += (_, _) => { _selected = captured; Refresh(); };
            _tabs.Children.Add(tab);
        }
        if (accounts.Count == 0) _tabs.Children.Add(Texto(Loc.T("Nenhuma conta do PokeIdle."), muted: true));
    }

    private void BuildHeader()
    {
        _header.Children.Clear();
        if (_selected is not { } id) return;
        var cfg = _module.Config(id);

        var on = new CheckBox { Content = Loc.T("AUTO-SNIPE NESTA CONTA"), IsChecked = cfg.Enabled, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold };
        on.Click += (_, _) =>
        {
            cfg.Enabled = on.IsChecked == true;
            _module.ConfigChanged(id);
        };
        SetDock(on, Dock.Right);
        _header.Children.Add(on);

        var balance = _module.BalanceOf(id);
        var slots = cfg.Rules.Count(r => r.Kind is RuleKind.Item or RuleKind.Pokemon);
        var text = (balance is null ? Loc.T("Saldo: esperando o jogo") : Loc.T("Saldo: {0} coins · {1} gemas", balance.Gold.ToString("N0", Br), balance.Orb.ToString("N0", Br)))
            + Loc.T("   ·   {0} / {1} regras", slots, SniperRules.MaxRules);
        var info = Texto(text, muted: true, margin: new Thickness(0));
        info.VerticalAlignment = VerticalAlignment.Center;
        _header.Children.Add(info);
    }

    private StackPanel BuildReserve()
    {
        var section = new StackPanel();
        section.Children.Add(Titulo(Loc.T("PROTEÇÃO DE SALDO"), new Thickness(0, 14, 0, 6)));
        section.Children.Add(Texto(Loc.T("Reserva que o sniper nunca gasta. Ele só compra se sobrar pelo menos isso depois da compra."), muted: true, margin: new Thickness(0, 0, 0, 6)));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Mini(Loc.T("Reserva em coins"), _reserveGold, 140));
        row.Children.Add(Mini(Loc.T("Reserva em gemas"), _reserveOrb, 140));
        _reserveGold.LostFocus += ReserveChanged;
        _reserveOrb.LostFocus += ReserveChanged;
        section.Children.Add(row);
        return section;
    }

    private void BuildRules()
    {
        _ruleList.Children.Clear();
        if (_selected is not { } id) return;
        var cfg = _module.Config(id);
        var itemRules = cfg.Rules.Count(r => r.Kind is RuleKind.Item or RuleKind.Pokemon);
        _rulesTitle.Text = Loc.T("REGRAS DE COMPRA ATIVAS ({0} de {1})", itemRules, SniperRules.MaxRules);
        if (cfg.Rules.Count == 0)
            _ruleList.Children.Add(Texto(Loc.T("Nenhuma regra cadastrada. Cadastre até {0} regras ao lado ou ligue um radar.", SniperRules.MaxRules), muted: true));
        foreach (var rule in cfg.Rules) _ruleList.Children.Add(RuleCard(id, cfg, rule));

        // não pisa no que o usuário está digitando
        if (!_reserveGold.IsKeyboardFocusWithin) _reserveGold.Text = cfg.ReserveGold.ToString("N0", Br);
        if (!_reserveOrb.IsKeyboardFocusWithin) _reserveOrb.Text = cfg.ReserveOrb.ToString("N0", Br);

        var radarsOn = cfg.Rules.Count(r => r.Kind is RuleKind.AnyShiny or RuleKind.AnyP5 or RuleKind.MinNota && r.Enabled);
        _radarBadge.Text = Loc.T("{0} / 3 ativos", radarsOn);
    }

    private void ReserveChanged(object? sender, RoutedEventArgs e)
    {
        if (_selected is not { } id) return;
        var cfg = _module.Config(id);
        if (NumberParse.Coins(_reserveGold.Text) is not { } gold || NumberParse.Coins(_reserveOrb.Text) is not { } orb)
        {
            // texto que não é número: não zera a proteção em silêncio, volta ao valor que valia
            _reserveGold.Text = cfg.ReserveGold.ToString("N0", Br);
            _reserveOrb.Text = cfg.ReserveOrb.ToString("N0", Br);
            return;
        }
        cfg.ReserveGold = gold;
        cfg.ReserveOrb = orb;
        _module.ConfigChanged(id);
    }

    private Border RuleCard(int account, SniperAccountConfig cfg, SniperRule rule)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var on = new CheckBox { IsChecked = rule.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), ToolTip = Loc.T("Liga/desliga esta regra") };
        on.Click += (_, _) => { rule.Enabled = on.IsChecked == true; _module.ConfigChanged(account); };
        var remove = new Button { Content = "✕", Padding = new Thickness(8, 2, 8, 2), ToolTip = Loc.T("Apagar regra") };
        remove.Click += (_, _) => { cfg.Rules.Remove(rule); _module.ConfigChanged(account); };

        var info = new StackPanel();
        info.Children.Add(Texto(RuleTitle(rule), bold: true, margin: new Thickness(0)));
        info.Children.Add(Texto(RuleDetail(rule), muted: true, margin: new Thickness(0)));

        Grid.SetColumn(on, 0);
        Grid.SetColumn(info, 1);
        Grid.SetColumn(remove, 2);
        grid.Children.Add(on);
        grid.Children.Add(info);
        grid.Children.Add(remove);
        return new Border
        {
            Style = (Style)Application.Current.Resources["ArcadeCard"],
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 6),
            Child = grid,
        };
    }

    private string RuleTitle(SniperRule r) => r.Kind switch
    {
        RuleKind.AnyShiny => Loc.T("Qualquer shiny"),
        RuleKind.AnyP5 => Loc.T("Qualquer P5"),
        RuleKind.MinNota => Loc.T("Nota ≥ {0}", r.MinNota.ToString("0.##", Br)),
        RuleKind.Pokemon => $"{(string.IsNullOrWhiteSpace(r.Label) ? _module.Species.NameOf(r.SpeciesId ?? 0) ?? Loc.T("Pokémon {0}", r.SpeciesId) : r.Label)}  ·  Pokémon",
        _ => string.IsNullOrWhiteSpace(r.Label) ? _module.Catalog?.ById(r.ItemId ?? 0)?.Name ?? Loc.T("Item {0}", r.ItemId) : r.Label,
    };

    private static string RuleDetail(SniperRule r)
    {
        var parts = new List<string>();
        if (r.MaxGold > 0) parts.Add(Loc.T("até {0} coins", r.MaxGold.ToString("N0", Br)));
        if (r.MaxOrb > 0) parts.Add(Loc.T("até {0} gemas", r.MaxOrb.ToString("N0", Br)));
        parts.Add(r.MaxQty > 0 ? Loc.T("comprou {0} de {1}", r.Bought, r.MaxQty) : Loc.T("comprou {0} (sem limite)", r.Bought));
        return string.Join(" · ", parts);
    }

    private void BuildFeed()
    {
        _feed.Children.Clear();
        if (_selected is not { } id) return;
        foreach (var item in _module.Feed(id).Reverse().Take(120))
        {
            var color = item.Kind switch { FeedKind.Bought => "Green", FeedKind.Error => "Err", FeedKind.Lost => "Orange", FeedKind.Offer => "Yellow", _ => "Muted" };
            _feed.Children.Add(Texto($"{item.At:HH:mm:ss}  {item.Text}", color: color));
        }
        if (_feed.Children.Count == 0) _feed.Children.Add(Texto(Loc.T("Aguardando anúncios do mercado…"), muted: true));
    }

    // ---------- Ações ----------

    private void LoadForm(int account)
    {
        _formAccount = account;
        _loadingForm = true;
        var cfg = _module.Config(account);
        foreach (var ui in _radars.Values)
        {
            var rule = cfg.Rules.FirstOrDefault(r => r.Kind == ui.Kind);
            ui.Box.IsChecked = rule?.Enabled == true;
            ui.Gold.Text = (rule?.MaxGold ?? 0).ToString(Br);
            ui.Orb.Text = (rule?.MaxOrb ?? 0).ToString(Br);
            ui.Nota.Text = (rule?.MinNota > 0 ? rule.MinNota : 7).ToString("0.##", Br);
        }
        _radarError.Text = "";
        _loadingForm = false;
    }

    private void AddRule()
    {
        _formError.Text = "";
        if (_selected is not { } id) return;
        if (_picked is not { } picked) { _formError.Text = Loc.T("Escolha um item ou Pokémon na busca."); return; }
        if (NumberParse.Coins(_goldCeiling.Text) is not { } gold || NumberParse.Coins(_orbCeiling.Text) is not { } orb)
        {
            _formError.Text = Loc.T("Use só números nos tetos.");
            return;
        }
        var qty = _infinite.IsChecked == true ? 0 : NumberParse.Coins(_quantity.Text);
        if (qty is null) { _formError.Text = Loc.T("Use só números na quantidade."); return; }

        var rule = new SniperRule
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Kind = picked.Kind == SearchKind.Pokemon ? RuleKind.Pokemon : RuleKind.Item,
            ItemId = picked.Kind == SearchKind.Item ? picked.Id : null,
            SpeciesId = picked.Kind == SearchKind.Pokemon ? picked.Id : null,
            Label = picked.Name,
            MaxGold = gold,
            MaxOrb = orb,
            MaxQty = (int)Math.Clamp(qty.Value, 0, int.MaxValue),
            Enabled = true,
        };
        var cfg = _module.Config(id);
        if (SniperRules.CanAdd(cfg, rule) is { } error) { _formError.Text = error; return; }
        cfg.Rules.Add(rule);
        _module.ConfigChanged(id);
        Pick(null);
    }

    private void ApplyRadar(RadarUi ui)
    {
        if (_loadingForm || _selected is not { } id) return;
        _radarError.Text = "";
        var cfg = _module.Config(id);
        var existing = cfg.Rules.FirstOrDefault(r => r.Kind == ui.Kind);

        if (ui.Box.IsChecked != true)
        {
            if (existing is not null) { existing.Enabled = false; _module.ConfigChanged(id); }
            return;
        }

        var gold = NumberParse.Coins(ui.Gold.Text);
        var nota = ParseDecimal(ui.Nota.Text);
        if (NumberParse.Coins(ui.Orb.Text) is not { } orb || gold is not { } coins || (ui.Kind == RuleKind.MinNota && nota is null))
        {
            _radarError.Text = Loc.T("Use só números nos tetos e na nota.");
            ui.Box.IsChecked = false;
            return;
        }

        var rule = new SniperRule
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString("N")[..8],
            Kind = ui.Kind,
            Label = ui.Kind switch { RuleKind.AnyShiny => Loc.T("Qualquer shiny"), RuleKind.AnyP5 => Loc.T("Qualquer P5"), _ => Loc.T("Nota ≥ {0}", (nota ?? 0).ToString("0.##", Br)) },
            MaxGold = ui.Kind == RuleKind.AnyShiny ? 0 : coins,
            MaxOrb = orb,
            MinNota = ui.Kind == RuleKind.MinNota ? nota ?? 0 : 0,
            Bought = existing?.Bought ?? 0,
            Enabled = true,
        };
        if (SniperRules.Validate(rule) is { } invalid)
        {
            _radarError.Text = invalid;
            ui.Box.IsChecked = false;
            return;
        }
        if (existing is null) cfg.Rules.Add(rule);
        else cfg.Rules[cfg.Rules.IndexOf(existing)] = rule;
        _module.ConfigChanged(id);
    }

    private static double? ParseDecimal(string? text)
    {
        var normalized = (text ?? "").Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n >= 0 ? n : null;
    }
}
