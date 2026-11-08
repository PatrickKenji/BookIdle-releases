using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using IdleDeck.Core.Lives;

namespace IdleDeck.App.Modules.Lives;

/// <summary>
/// Painel da janela "Lives": abas Twitch e Kick; em cada uma, um cartão por conta (total de pontos e o que ela assiste)
/// e a tabela dos canais (na ordem da lista do perfil), com quem está ao vivo e o saldo de cada conta em cada canal
/// (selo amarelo ▶ = a conta está assistindo esse canal agora). Relê sozinho a cada minuto enquanto a janela está aberta.
/// </summary>
public sealed class LivesPanel : DockPanel
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(1);
    private static readonly Brush TwitchColor = Frozen(0x91, 0x46, 0xFF);
    private static readonly Brush KickColor = Frozen(0x53, 0xFC, 0x18);
    private static readonly Brush KickText = Frozen(0x0B, 0x1A, 0x05);

    private readonly LivesModule _module;
    private readonly Button _twitchTab = TabButton("Twitch");
    private readonly Button _kickTab = TabButton("Kick");
    private readonly Button _refresh = new() { Content = Loc.T("⟳ Atualizar"), Padding = new Thickness(12, 5, 12, 5) };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), FontSize = 12 };
    private readonly ContentControl _cards = new();
    private readonly ContentControl _body = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _errors = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 10, 0, 0) };
    private readonly DispatcherTimer _timer = new() { Interval = RefreshEvery };
    private bool _kick;
    private bool _loading;

    public LivesPanel(LivesModule module)
    {
        _module = module;
        _status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _errors.SetResourceReference(TextBlock.ForegroundProperty, "Err");

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 14), LastChildFill = false };
        top.Children.Add(_twitchTab);
        top.Children.Add(_kickTab);
        top.Children.Add(_status);
        DockPanel.SetDock(_refresh, Dock.Right);
        top.Children.Add(_refresh);
        SetDock(top, Dock.Top);
        Children.Add(top);
        SetDock(_cards, Dock.Top);
        Children.Add(_cards); // cartões das contas fixos em cima; só a tabela rola
        SetDock(_errors, Dock.Bottom);
        Children.Add(_errors);
        _scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        Children.Add(_scroll);

        _twitchTab.Click += (_, _) => { _kick = false; _scroll.ScrollToTop(); _ = LoadAsync(); };
        _kickTab.Click += (_, _) => { _kick = true; _scroll.ScrollToTop(); _ = LoadAsync(); };
        _refresh.Click += (_, _) => _ = LoadAsync();
        _timer.Tick += (_, _) => _ = LoadAsync();
        _module.ContasMudaram += (_, _) => Dispatcher.BeginInvoke(() => { if (IsVisible) _ = LoadAsync(); });

        // só lê enquanto a janela está aberta
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { _ = LoadAsync(); _timer.Start(); }
            else _timer.Stop();
        };
        PaintTabs();
    }

    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        PaintTabs();
        _status.Text = Loc.T("lendo…");
        _refresh.IsEnabled = false;
        try
        {
            var (board, errors) = _kick ? await _module.ReadKickAsync() : await _module.ReadTwitchAsync();
            _cards.Content = board.Accounts.Count > 0 && board.Rows.Count > 0 ? AccountCards(board) : null;
            _body.Content = Build(board);
            _errors.Text = string.Join("\n", errors);
            _status.Text = Loc.T("atualizado às {0:HH:mm} · {1} ao vivo de {2}", DateTime.Now, board.Rows.Count(r => r.Live), board.Rows.Count);
        }
        finally
        {
            _loading = false;
            _refresh.IsEnabled = true;
        }
    }

    /// <summary>Aba escolhida na cor da plataforma (roxo Twitch, verde Kick); a outra fica no estilo comum.</summary>
    private void PaintTabs()
    {
        Paint(_twitchTab, !_kick, TwitchColor, Brushes.White);
        Paint(_kickTab, _kick, KickColor, KickText);

        static void Paint(Button tab, bool selected, Brush background, Brush foreground)
        {
            if (selected)
            {
                tab.Background = background;
                tab.Foreground = foreground;
                tab.BorderBrush = background;
            }
            else
            {
                tab.ClearValue(Control.BackgroundProperty);
                tab.ClearValue(Control.ForegroundProperty);
                tab.ClearValue(Control.BorderBrushProperty);
            }
        }
    }

    private UIElement Build(LiveBoard board)
    {
        if (board.Rows.Count == 0)
            return Empty(_kick ? Loc.T("Nenhum canal da Kick neste perfil. ⋯ do perfil → Kick (bônus de live)…")
                : Loc.T("Nenhum canal da Twitch neste perfil. ⋯ do perfil → Twitch (bônus de live)…"));

        var stack = new StackPanel();
        stack.Children.Add(Table(board));

        var legend = Label(board.Accounts.Count == 0
            ? (_kick ? Loc.T("Nenhuma conta deste perfil está com a Kick conectada: só dá para ver quem está ao vivo.")
                     : Loc.T("Nenhuma conta deste perfil está com a Twitch conectada: só dá para ver quem está ao vivo."))
            : Loc.T("▶ = a conta está assistindo essa live agora (2 por conta). Os pontos sobem 10 a cada 5 min assistidos (sub: o dobro)."),
            11, muted: true);
        legend.Margin = new Thickness(2, 10, 0, 0);
        legend.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(legend);
        return stack;
    }

    /// <summary>Um cartão por conta: nome, total de pontos em destaque e as lives que ela assiste agora.</summary>
    private UIElement AccountCards(LiveBoard board)
    {
        var accent = _kick ? KickColor : TwitchColor;
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        for (var a = 0; a < board.Accounts.Count; a++)
        {
            var watching = board.Rows.Where(r => r.Cells[a].Watching).Select(r => r.Channel).ToList();
            var inner = new StackPanel();
            inner.Children.Add(Label(board.Accounts[a], 12, bold: true, muted: true));
            var total = Label(board.Totals[a].ToString("N0", Loc.Culture), 24, bold: true);
            total.Margin = new Thickness(0, 2, 0, 0);
            inner.Children.Add(total);
            inner.Children.Add(Label(Loc.T("pontos no total"), 11, muted: true));
            var now = Label(watching.Count == 0 ? Loc.T("sem live agora") : "▶ " + string.Join(", ", watching), 12,
                bold: watching.Count > 0, color: watching.Count > 0 ? "Yellow" : null, muted: watching.Count == 0);
            now.Margin = new Thickness(0, 8, 0, 0);
            now.TextTrimming = TextTrimming.CharacterEllipsis;
            inner.Children.Add(now);

            var card = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 12),
                Margin = new Thickness(0, 0, 10, 10),
                MinWidth = 170,
                MaxWidth = 240,
                BorderThickness = new Thickness(3, 0, 0, 0),
                BorderBrush = accent,
                Child = inner,
            };
            card.SetResourceReference(Border.BackgroundProperty, "Card");
            wrap.Children.Add(card);
        }
        return wrap;
    }

    /// <summary>A tabela: cabeçalho, uma linha por canal (alternando o fundo) e o total; colunas alinhadas entre as linhas.</summary>
    private UIElement Table(LiveBoard board)
    {
        var rows = new StackPanel();
        Grid.SetIsSharedSizeScope(rows, true);

        var header = Row(board.Accounts.Count);
        Put(header, 0, Label(Loc.T("CANAL"), 11, bold: true, muted: true));
        Put(header, 1, Label(Loc.T("STATUS"), 11, bold: true, muted: true));
        for (var a = 0; a < board.Accounts.Count; a++) Put(header, 2 + a, Right(Label(board.Accounts[a].ToUpperInvariant(), 11, bold: true, muted: true)));
        rows.Children.Add(Line(header, "Field"));

        for (var i = 0; i < board.Rows.Count; i++)
        {
            var line = board.Rows[i];
            var grid = Row(board.Accounts.Count);
            Put(grid, 0, Label(line.Channel, 14, bold: line.Live, muted: !line.Live && line.Cells.All(c => !c.Watching)));
            Put(grid, 1, line.Live ? LivePill() : Label(Loc.T("offline"), 12, muted: true));
            for (var a = 0; a < line.Cells.Count; a++) Put(grid, 2 + a, Right(Balance(line.Cells[a])));
            rows.Children.Add(Line(grid, i % 2 == 1 ? "HoverGlow" : null));
        }

        if (board.Accounts.Count > 0)
        {
            var total = Row(board.Accounts.Count);
            Put(total, 0, Label(Loc.T("Total"), 13, bold: true));
            for (var a = 0; a < board.Totals.Count; a++) Put(total, 2 + a, Right(Label(board.Totals[a].ToString("N0", Loc.Culture), 14, bold: true)));
            var totalLine = Line(total, "Field");
            totalLine.BorderThickness = new Thickness(0, 1, 0, 0);
            totalLine.SetResourceReference(Border.BorderBrushProperty, "Pill");
            rows.Children.Add(totalLine);
        }

        var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(0, 0, 0, 0), Child = rows, ClipToBounds = true };
        card.SetResourceReference(Border.BackgroundProperty, "Card");
        return card;
    }

    /// <summary>Saldo da conta no canal: selo amarelo quando ela está assistindo; traço quando não deu para ler.</summary>
    private static UIElement Balance(LiveBoardCell cell)
    {
        var value = cell.Balance is { } b ? b.ToString("N0", Loc.Culture) : "—";
        if (!cell.Watching) return Label(value, 14, muted: cell.Balance is null or 0);
        var text = Label("▶ " + value, 13, bold: true, color: "YellowText");
        var pill = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2), Child = text };
        pill.SetResourceReference(Border.BackgroundProperty, "Yellow");
        return pill;
    }

    private static UIElement LivePill()
    {
        var text = Label("● " + Loc.T("AO VIVO"), 11, bold: true, color: "Outline");
        var pill = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 2, 8, 2), Child = text, HorizontalAlignment = HorizontalAlignment.Left };
        pill.SetResourceReference(Border.BackgroundProperty, "Green");
        return pill;
    }

    private static Grid Row(int accounts)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "channel" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "status" });
        for (var a = 0; a < accounts; a++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "account" + a });
        return grid;
    }

    private static Border Line(Grid grid, string? background)
    {
        var line = new Border { Padding = new Thickness(16, 8, 16, 8), Child = grid };
        if (background is not null) line.SetResourceReference(Border.BackgroundProperty, background);
        return line;
    }

    private static void Put(Grid grid, int column, UIElement element)
    {
        if (element is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        var cell = new Border { Margin = new Thickness(column == 0 ? 0 : 22, 0, 0, 0), Child = element, MinWidth = column == 0 ? 150 : 0 };
        if (column >= 2) cell.MinWidth = 90;
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static UIElement Right(UIElement element)
    {
        if (element is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Right;
        return element;
    }

    private static UIElement Empty(string text)
    {
        var label = Label(text, 13, muted: true);
        label.TextWrapping = TextWrapping.Wrap;
        var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(18), Child = label };
        card.SetResourceReference(Border.BackgroundProperty, "Card");
        return card;
    }

    private static TextBlock Label(string text, double size, bool bold = false, bool muted = false, string? color = null)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal };
        block.SetResourceReference(TextBlock.ForegroundProperty, color ?? (muted ? "Muted" : "Text"));
        return block;
    }

    private static Button TabButton(string text) => new()
    {
        Content = text,
        Margin = new Thickness(0, 0, 8, 0),
        Padding = new Thickness(18, 6, 18, 6),
        FontWeight = FontWeights.Bold,
        MinWidth = 90,
    };

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
