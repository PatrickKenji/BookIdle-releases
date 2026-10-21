using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using IdleDeck.Core.Hunt;

namespace IdleDeck.App.Modules.Hunt;

/// <summary>
/// Painel do /hunt (aberto numa <see cref="ModuleWindow"/>): em cima, um card por conta do perfil selecionado
/// (pokémon em campo, melhor hunt por XP/h e por gold/h líquido); embaixo, o ranking completo da conta escolhida.
/// </summary>
public sealed class HuntPanel : DockPanel
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Largura da barra de rolagem do tema: o cabeçalho desconta o mesmo espaço que as linhas.</summary>
    private const double LarguraBarra = 8;

    private enum TipoColuna { Texto, Numero }

    private readonly record struct Coluna(string Titulo, GridLength Largura, TipoColuna Tipo);

    private static readonly Coluna[] Colunas =
    [
        new("Hunt", new GridLength(2.2, GridUnitType.Star), TipoColuna.Texto),
        new("Nv", new GridLength(52), TipoColuna.Numero),
        new("XP/h", new GridLength(1, GridUnitType.Star), TipoColuna.Numero),
        new("Gold líq./h", new GridLength(1, GridUnitType.Star), TipoColuna.Numero),
        new("Abates/h", new GridLength(84), TipoColuna.Numero),
        new("s/abate", new GridLength(68), TipoColuna.Numero),
        new("Poções/h", new GridLength(76), TipoColuna.Numero),
        new("Risco", new GridLength(98), TipoColuna.Texto),
        new("Golpe", new GridLength(1.4, GridUnitType.Star), TipoColuna.Texto),
    ];

    private readonly HuntModule _modulo;
    private readonly IModuleHostServices _host;
    private readonly UniformGrid _cards = new() { Columns = 2, Margin = new Thickness(0, 0, 0, 4) };
    private readonly StackPanel _barra = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
    private readonly StackPanel _tituloCards = Titulo("melhor hunt por conta");
    private readonly StackPanel _tituloRanking = Titulo("ranking da conta escolhida");
    private readonly StackPanel _linhas = new();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Visible };
    private readonly Border _cabecalho;
    private int? _selecionada;
    private bool _porOuro;
    private bool _bloqueadas;

    public HuntPanel(HuntModule modulo, IModuleHostServices host)
    {
        _modulo = modulo;
        _host = host;
        _cabecalho = Cabecalho();

        foreach (var fixo in new UIElement[] { _tituloCards, _cards, _tituloRanking, _barra, _cabecalho })
            SetDock(fixo, Dock.Top);
        foreach (var elemento in new UIElement[] { _tituloCards, _cards, _tituloRanking, _barra, _cabecalho })
            Children.Add(elemento);
        // O cabeçalho fica fora do ScrollViewer (para não sumir ao rolar), então desconta o vão real da barra.
        _scroll.Content = _linhas;
        _scroll.SizeChanged += (_, _) => AlinharCabecalho();
        _scroll.ScrollChanged += (_, e) => { if (e.ViewportWidthChange != 0) AlinharCabecalho(); };
        Children.Add(_scroll);
        Atualizar();
    }

    /// <summary>Mede o vão que a barra de rolagem ocupa e aplica no cabeçalho: as colunas casam em pixel.</summary>
    private void AlinharCabecalho()
    {
        if (_scroll.ViewportWidth <= 0 || _scroll.ActualWidth <= 0) return;
        var barra = _scroll.ActualWidth - _scroll.ViewportWidth;
        if (Math.Abs(_cabecalho.Margin.Right - barra) < 0.5) return;
        _cabecalho.Margin = new Thickness(0, 0, barra, 6);
    }

    private Brush Cor(string chave) => (Brush)Application.Current.Resources[chave];

    public void Atualizar()
    {
        var contas = _host.ContasDoPerfilSelecionado();
        if (_selecionada is null || !contas.Contains(_selecionada.Value)) _selecionada = contas.FirstOrDefault(c => _modulo.Ultimos.ContainsKey(c), contas.FirstOrDefault());

        _cards.Children.Clear();
        foreach (var id in contas) _cards.Children.Add(Card(id));
        _tituloCards.Visibility = contas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        _barra.Children.Clear();
        _linhas.Children.Clear();
        if (_selecionada is not { } sel || !_modulo.Ultimos.TryGetValue(sel, out var u) || u.Mensagem is not HuntReport r)
        {
            MostrarRanking(false);
            _linhas.Children.Add(Texto(contas.Count == 0
                ? "Nenhuma conta neste perfil."
                : "Abra o jogo nas contas e coloque um pokémon em campo: as hunts aparecem aqui.", muted: true));
            return;
        }

        MostrarRanking(true);
        _barra.Children.Add(Alternar("XP/h", !_porOuro, () => { _porOuro = false; Atualizar(); }));
        _barra.Children.Add(Alternar("Gold/h líquido", _porOuro, () => { _porOuro = true; Atualizar(); }));
        var bloq = new CheckBox { Content = "mostrar bloqueadas", IsChecked = _bloqueadas, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bloq.Click += (_, _) => { _bloqueadas = bloq.IsChecked == true; Atualizar(); };
        _barra.Children.Add(bloq);
        if (r.Pokemon is { Aoe: true } pk) AdicionarAjusteAoe(sel, pk.AoeAlvos);
        _barra.Children.Add(Texto(Resumo(r), muted: true, margem: new Thickness(14, 0, 0, 0)));

        var lista = (_porOuro ? r.PorOuro : r.PorXp).Where(l => _bloqueadas || l.Liberada).ToList();
        if (lista.Count == 0) _linhas.Children.Add(Texto("Nenhuma hunt liberada para o nível deste treinador.", muted: true));
        for (var i = 0; i < lista.Count; i++) _linhas.Children.Add(LinhaDaHunt(lista[i], i));
    }

    /// <summary>Sem relatório da conta escolhida não há tabela: some com o título, os filtros e o cabeçalho.</summary>
    private void MostrarRanking(bool visivel)
    {
        var v = visivel ? Visibility.Visible : Visibility.Collapsed;
        _tituloRanking.Visibility = v;
        _barra.Visibility = v;
        _cabecalho.Visibility = v;
    }

    private static string Resumo(HuntReport r)
    {
        var partes = new List<string> { $"bônus XP ×{r.BonusXpPokemon.ToString("0.##", Br)}" };
        if (r.BikeRaridade is { } bike) partes.Add($"bike {bike}");
        partes.Add(r.Pocao is { } p ? $"poção: {p} (limiar {r.Limiar:P0})" : "sem poção automática");
        if (r.DiaSazonal is { Length: > 0 } dia) partes.Add($"dia sazonal: {HuntReport.TipoEmPortugues(dia)}{(r.HeroiDoDia ? " (seu pokémon ×2)" : "")}");
        return string.Join(" · ", partes);
    }

    private void AdicionarAjusteAoe(int contaId, int alvos)
    {
        _barra.Children.Add(Texto("AoE: alvos por golpe", muted: true, margem: new Thickness(14, 0, 4, 0)));
        _barra.Children.Add(Botao("−", async () => await _modulo.DefinirAoeAsync(contaId, Math.Max(1, alvos - 1))));
        _barra.Children.Add(Texto(alvos.ToString(), bold: true, margem: new Thickness(6, 0, 6, 0)));
        _barra.Children.Add(Botao("+", async () => await _modulo.DefinirAoeAsync(contaId, Math.Min(16, alvos + 1))));
    }

    private Border Card(int contaId)
    {
        var nome = _host.NomeDaConta(contaId) ?? "Conta";
        var corpo = new StackPanel();
        corpo.Children.Add(Texto(nome, bold: true));
        if (_modulo.Ultimos.TryGetValue(contaId, out var u) && u.Mensagem is HuntReport r)
        {
            var pk = r.Pokemon;
            var selos = new List<string>();
            if (pk?.ZMove is { } z) selos.Add($"Z-{z}");
            if (pk?.Aoe == true) selos.Add($"AoE×{pk.AoeAlvos}");
            if (r.BikeRaridade is { } b) selos.Add($"🚲 {b}");
            corpo.Children.Add(Texto($"{pk?.Nome ?? "—"} · Nv {pk?.Level}  {string.Join(" ", selos)}", muted: true));
            var xp = r.PorXp.FirstOrDefault(l => l.Liberada && !l.Inviavel);
            var ouro = r.PorOuro.FirstOrDefault(l => l.Liberada && !l.Inviavel);
            corpo.Children.Add(Texto(xp is null ? "XP: —" : $"★ XP: {xp.Nome} · {Compacto(xp.XpPokemonH)}/h", cor: "Yellow"));
            corpo.Children.Add(Texto(ouro is null ? "Gold: —" : $"💰 Gold: {ouro.Nome} · {Compacto(ouro.LiquidoH)}/h", cor: "Green"));
            corpo.Children.Add(Texto(u.Aviso ?? $"atualizado {Idade(u.Recebido)}", muted: true));
        }
        else if (u is { Mensagem: HuntStatus s })
        {
            corpo.Children.Add(Texto(s.Texto, muted: true));
        }
        else
        {
            corpo.Children.Add(Texto("Esperando o jogo…", muted: true));
        }

        var card = new Border
        {
            Style = (Style)Application.Current.Resources["ArcadeCard"],
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 8, 8),
            Cursor = Cursors.Hand,
            Child = corpo,
            BorderBrush = Cor(contaId == _selecionada ? "Purple" : "Outline"),
            Background = Cor(contaId == _selecionada ? "CardActive" : "Card"),
        };
        card.MouseLeftButtonUp += (_, _) => { _selecionada = contaId; Atualizar(); };
        return card;
    }

    /// <summary>Barra de títulos: mesma grade e mesmas larguras das linhas, descontando a barra de rolagem.</summary>
    private Border Cabecalho()
    {
        var celulas = Colunas
            .Select(c => Texto(c.Titulo, muted: true, bold: true, direita: c.Tipo == TipoColuna.Numero))
            .ToArray();
        return new Border
        {
            Background = Cor("Pill"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 5, 6, 5),
            Margin = new Thickness(0, 0, LarguraBarra, 6),
            Child = Grade(celulas),
        };
    }

    /// <summary>Uma hunt do ranking. Linhas alternadas (zebra) para o olho não pular de linha.</summary>
    private Border LinhaDaHunt(HuntLine l, int indice)
    {
        var nome = l.Nome + (l.Atual ? "  ◀ aqui" : "") + (l.Liberada ? "" : "  🔒");
        var risco = l.Inviavel ? "não mata" : l.Risco switch
        {
            HuntRisk.UmHit => "⚠ 1 golpe mata",
            HuntRisk.Arriscado => "arriscado",
            _ => "seguro",
        };
        var celulas = new UIElement[]
        {
            Texto(nome, bold: l.Atual),
            Texto(l.Nivel.ToString("N0", Br), muted: true, direita: true),
            Texto(l.Inviavel ? "—" : Compacto(l.XpPokemonH), cor: _porOuro ? null : "Yellow", direita: true),
            Texto(l.Inviavel ? "—" : Compacto(l.LiquidoH), cor: _porOuro ? "Green" : null, direita: true),
            Texto(l.Inviavel ? "—" : l.KillsH.ToString("N0", Br), direita: true),
            Texto(l.Inviavel ? "—" : l.LutaS.ToString("0.0", Br), direita: true),
            Texto(l.PocoesH > 0 ? l.PocoesH.ToString("0.#", Br) : "—", muted: true, direita: true),
            Texto(risco, cor: l.Inviavel || l.Risco == HuntRisk.UmHit ? "Err" : l.Risco == HuntRisk.Arriscado ? "Orange" : null),
            Texto(l.Golpe ?? "—", muted: true),
        };

        var zebra = indice % 2 == 0 ? Brushes.Transparent : Cor("Card");
        var borda = new Border
        {
            Background = zebra,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 0, 2),
            Child = Grade(celulas),
            Opacity = l.Liberada && !l.Inviavel ? 1 : 0.55,
        };
        borda.ToolTip = l.Inviavel ? "O pokémon não consegue derrubar estes selvagens."
            : $"Gold bruto {Compacto(l.OuroH)}/h − poções {Compacto(l.CustoPocoesH)} − revives {Compacto(l.CustoRevivesH)} − bolas {Compacto(l.CustoBolasH)}\n" +
              $"XP do treinador {Compacto(l.XpTreinadorH)}/h · maior golpe do selvagem {l.MaiorGolpePct:P0} do seu HP · mortes/h {l.MortesH:0.##}";
        // Realce ao passar o mouse (a zebra volta quando ele sai).
        var realce = Cor("Pill");
        borda.MouseEnter += (_, _) => borda.Background = realce;
        borda.MouseLeave += (_, _) => borda.Background = zebra;
        return borda;
    }

    private static Grid Grade(params UIElement[] celulas)
    {
        var g = new Grid();
        foreach (var coluna in Colunas) g.ColumnDefinitions.Add(new ColumnDefinition { Width = coluna.Largura });
        for (var i = 0; i < celulas.Length; i++)
        {
            Grid.SetColumn(celulas[i], i);
            g.Children.Add(celulas[i]);
        }
        return g;
    }

    private static StackPanel Titulo(string texto)
    {
        var painel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        painel.Children.Add(new TextBlock
        {
            Text = texto.ToUpperInvariant(),
            Style = (Style)Application.Current.Resources["SectionTitle"],
            Margin = new Thickness(0),
        });
        return painel;
    }

    private TextBlock Texto(string texto, bool muted = false, bool bold = false, string? cor = null, Thickness? margem = null, bool direita = false) => new()
    {
        Text = texto,
        FontSize = 12,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        Foreground = Cor(cor ?? (muted ? "Muted" : "Text")),
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextAlignment = direita ? TextAlignment.Right : TextAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = margem ?? new Thickness(0, 0, 6, 0),
    };

    private Button Alternar(string texto, bool ativo, Action aoClicar)
    {
        var b = new Button { Content = texto, Style = (Style)Application.Current.Resources[ativo ? "ArcadeSelectedButton" : "ArcadeButton"] };
        b.Click += (_, _) => aoClicar();
        return b;
    }

    private static Button Botao(string texto, Func<Task> aoClicar)
    {
        var b = new Button { Content = texto, Padding = new Thickness(9, 1, 9, 1), Margin = new Thickness(0) };
        b.Click += async (_, _) => await aoClicar();
        return b;
    }

    /// <summary>900 mil, 1,2 mi, 3,4 bi — como o jogo mostra números grandes.</summary>
    public static string Compacto(double v)
    {
        var a = Math.Abs(v);
        return a switch
        {
            >= 1e9 => (v / 1e9).ToString("0.#", Br) + " bi",
            >= 1e6 => (v / 1e6).ToString("0.#", Br) + " mi",
            >= 1e3 => (v / 1e3).ToString("0.#", Br) + " mil",
            _ => v.ToString("0", Br),
        };
    }

    private static string Idade(DateTime quando)
    {
        var s = (DateTime.Now - quando).TotalSeconds;
        return s < 60 ? "agora" : s < 3600 ? $"há {s / 60:0} min" : $"há {s / 3600:0} h";
    }
}
