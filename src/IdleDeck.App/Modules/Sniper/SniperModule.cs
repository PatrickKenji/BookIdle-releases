using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using IdleDeck.Core;
using IdleDeck.Core.Market;
using IdleDeck.Core.Settings;
using IdleDeck.App.Market;

namespace IdleDeck.App.Modules.Sniper;

public sealed record FeedItem(DateTime At, FeedKind Kind, string Text);

public enum SniperState { Off, Watching, BoughtRecently, Error }

/// <summary>
/// Sniper do mercado: em cada conta do PokeIdle com a chave ligada, uma <see cref="SniperSession"/> vigia o mercado e
/// compra o que as regras mandam, por trás, pelo WebSocket que o próprio jogo abriu (ponte <c>market-bridge.js</c>).
/// </summary>
public sealed class SniperModule : IModuleView
{
    private const int MaxFeed = 200;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly string ItemsUrl = "https://pokeidle.io/assets/items.json";

    private readonly IModuleHostServices _host;
    private readonly Dictionary<int, AccountRun> _runs = [];
    private readonly OfferClaims _claims = new();
    private readonly DispatcherTimer _timer;
    private int _totalBought;
    private SniperPanel? _panel;
    private readonly List<CatalogItem> _earlyCatalog = [];
    private DateTime _nextCrawlAt = DateTime.MinValue;
    private bool _crawling, _crawled;

    public SniperModule(IModuleHostServices host)
    {
        _host = host;
        _host.ContasMudaram += (_, _) => Synchronize();
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) =>
        {
            Synchronize();
            _panel?.RefreshHeader(); // o saldo muda a toda hora
        };
        _timer.Start();
        _ = LoadCatalogAsync();
    }

    public string Id => "sniper";
    public string Title => "Sniper";
    public string UrlPattern => "pokeidle.io";
    public IReadOnlyList<string> AllowedPaths => ["/", "/app"];
    public string? InjectedScript { get; } = LoadBridgeScript();
    public string? Description => "Compra sozinho, em segundo plano, o que bate com as regras de cada conta. As contas continuam jogando.";

    public ItemCatalog? Catalog { get; private set; }

    /// <summary>Espécies que o mercado já mostrou (aprendidas das respostas e guardadas em disco).</summary>
    public SpeciesCatalog Species { get; } = LoadSpecies();

    public event EventHandler? SummaryChanged;
    public event EventHandler? Changed;

    public string? Summary
    {
        get
        {
            var watching = _runs.Values.Count(r => r.Session is not null);
            if (watching == 0 && _totalBought == 0) return "desligado";
            return $"{watching} conta{(watching == 1 ? "" : "s")} vigiando · {_totalBought} compra{(_totalBought == 1 ? "" : "s")}";
        }
    }

    public FrameworkElement CreatePanel() => _panel = new SniperPanel(this, _host);

    public void OnMessage(int accountId, JsonElement data) => Run(accountId).Channel.Deliver(data);

    // ---------- Estado por conta (a tela lê daqui) ----------

    public IReadOnlyList<int> Accounts => _host.TodasAsContasDoJogo("pokeidle.io");

    public string AccountName(int id) => _host.NomeDaConta(id) ?? $"Conta {id}";

    public SniperAccountConfig Config(int id) => _host.SniperDaConta(id);

    public IReadOnlyList<FeedItem> Feed(int id) => Run(id).Feed;

    /// <summary>Saldo da conta como o jogo contou (vale mesmo com o sniper desligado nela).</summary>
    public Balance? BalanceOf(int id) => Run(id).Tracker.Balance;

    public SniperState StateOf(int id)
    {
        var run = Run(id);
        if (run.Error) return SniperState.Error;
        if (!Config(id).Enabled) return SniperState.Off;
        return run.LastBought is { } at && DateTime.Now - at < TimeSpan.FromMinutes(2) ? SniperState.BoughtRecently : SniperState.Watching;
    }

    public void ClearFeed(int id)
    {
        Run(id).Feed.Clear();
        RaiseChanged();
    }

    /// <summary>O usuário ligou/desligou a chave da conta, ou mexeu nas regras: salva e ajusta as sessões.</summary>
    public void ConfigChanged(int id)
    {
        if (Config(id).Enabled) Run(id).Error = false;
        _host.SalvarConfiguracao();
        Synchronize();
        RaiseChanged();
    }

    // ---------- Sessões ----------

    private AccountRun Run(int id)
    {
        if (_runs.TryGetValue(id, out var run)) return run;
        run = new AccountRun { Channel = new PageMarketChannel(_host, id), Name = _host.NomeDaConta(id) ?? id.ToString() };
        run.Channel.Received += message =>
        {
            if (message is PurchaseMessage purchase)
                SniperLog.Write(run.Name, $"resposta de compra: oferta={purchase.Id} fase={purchase.Phase} ok={purchase.Ok} total={purchase.Total} moeda={purchase.Currency}");
            if (message is BalanceMessage balance)
            {
                var known = run.Tracker.Balance is not null;
                run.Tracker.Apply(balance, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if (!known && run.Tracker.Balance is not null) RaiseChanged();
            }
            OnMarketMessage(message);
        };
        return _runs[id] = run;
    }

    /// <summary>Deixa as sessões iguais às chaves: liga as que faltam, para as que sobram (conta desligada ou removida).</summary>
    private void Synchronize()
    {
        var accounts = Accounts.ToHashSet();
        foreach (var id in _runs.Keys.ToList())
        {
            var run = _runs[id];
            var wanted = accounts.Contains(id) && _host.SniperDaConta(id).Enabled;
            if (run.Session is not null && !wanted) Stop(run);
            else if (run.Session is null && wanted) Start(id, run);
        }
        foreach (var id in accounts)
            if (!_runs.ContainsKey(id) && _host.SniperDaConta(id).Enabled) Start(id, Run(id));
        SummaryChanged?.Invoke(this, EventArgs.Empty);
        MaybeCrawlSpecies();
    }

    private void Start(int id, AccountRun run)
    {
        var sink = new AccountSink(this, id, run);
        var cts = new CancellationTokenSource();
        // o registro de tentativas sobrevive a desligar/religar; as reservas de oferta valem entre as contas
        var session = new SniperSession(run.Channel, () => _host.SniperDaConta(id), sink, () => DateTimeOffset.UtcNow,
            shared: new SniperShared(run.Ledger, _claims, id.ToString(), run.Tracker));
        run.Session = session;
        run.Cts = cts;
        _ = session.RunAsync(cts.Token);
        AddFeed(run, FeedKind.Info, "Sniper ligado.");
    }

    private void Stop(AccountRun run)
    {
        run.Cts?.Cancel();
        run.Cts = null;
        run.Session = null;
        AddFeed(run, FeedKind.Info, "Sniper desligado.");
    }

    private void AddFeed(AccountRun run, FeedKind kind, string text)
    {
        SniperLog.Write(run.Name, $"{kind}: {text}");
        run.Feed.Add(new FeedItem(DateTime.Now, kind, text));
        if (run.Feed.Count > MaxFeed) run.Feed.RemoveRange(0, run.Feed.Count - MaxFeed);
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        _panel?.Refresh();
    }

    private sealed class AccountRun
    {
        public required PageMarketChannel Channel { get; init; }
        public string Name { get; set; } = "";
        public SniperLedger Ledger { get; } = new();
        public BalanceTracker Tracker { get; } = new();
        public SniperSession? Session { get; set; }
        public CancellationTokenSource? Cts { get; set; }
        public List<FeedItem> Feed { get; } = [];
        public DateTime? LastBought { get; set; }
        public bool Error { get; set; }
    }

    private sealed class AccountSink(SniperModule module, int id, AccountRun run) : ISniperSink
    {
        public void Feed(FeedKind kind, string text) => module.AddFeed(run, kind, text);

        public void Bought(SniperRule rule, MarketOffer offer, long total)
        {
            run.LastBought = DateTime.Now;
            module._totalBought++;
            module._host.Notificar($"Sniper — {module.AccountName(id)}", $"Comprou {offer.Name} por {total:N0} {(offer.Currency == Currency.Gold ? "coins" : "gemas")}.");
            module.SummaryChanged?.Invoke(module, EventArgs.Empty);
        }

        public void Disabled(string reason)
        {
            run.Error = true;
            module._host.Notificar($"Sniper — {module.AccountName(id)}", "Desligado depois de falhas seguidas. Confira o feed.");
        }

        public void ConfigChanged()
        {
            module._host.SalvarConfiguracao();
            module.RaiseChanged();
            module.Synchronize();
        }
    }

    // ---------- Catálogos aprendidos do jogo ----------

    private static string CachePath => Path.Combine(AppPaths.Root, "items.json");
    private static string LearnedItemsPath => Path.Combine(AppPaths.Root, "market-items.json");
    private static string SpeciesPath => Path.Combine(AppPaths.Root, "market-species.json");

    private void OnMarketMessage(MarketMessage message)
    {
        switch (message)
        {
            case SpeciesMessage species when Species.Merge(species.Species):
                SaveText(SpeciesPath, Species.ToJson());
                RaiseChanged();
                break;
            case CatalogMessage catalog:
                if (Catalog is null) _earlyCatalog.AddRange(catalog.Items);
                else LearnItems(catalog.Items);
                break;
        }
    }

    private void LearnItems(IEnumerable<CatalogItem> items)
    {
        if (Catalog is null || Catalog.Merge(items) == 0) return;
        SaveText(LearnedItemsPath, Catalog.ToJson());
        RaiseChanged();
    }

    private static void SaveText(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(path, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static SpeciesCatalog LoadSpecies()
    {
        try { return File.Exists(SpeciesPath) ? SpeciesCatalog.FromJson(File.ReadAllText(SpeciesPath)) : new SpeciesCatalog(); }
        catch (IOException) { return new SpeciesCatalog(); }
    }

    private async Task LoadCatalogAsync()
    {
        string? json = null;
        try
        {
            json = await Http.GetStringAsync(ItemsUrl);
            if (ItemCatalog.Parse(json).All.Count > 0) SaveText(CachePath, json);
            else json = null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            json = null;
        }

        if (json is null)
        {
            try { if (File.Exists(CachePath)) json = await File.ReadAllTextAsync(CachePath); }
            catch (IOException) { }
        }

        var catalog = ItemCatalog.Parse(json ?? "");
        try
        {
            if (File.Exists(LearnedItemsPath)) catalog.Merge(ItemCatalog.Parse(await File.ReadAllTextAsync(LearnedItemsPath)).All);
        }
        catch (IOException) { }
        Catalog = catalog;
        LearnItems(_earlyCatalog.ToList());
        _earlyCatalog.Clear();
        RaiseChanged();
    }

    /// <summary>
    /// O jogo não tem uma lista pronta de Pokémon: com uma conta conectada, lê uma vez as páginas de "espécies" do
    /// mercado (devagar, uma a cada 1,5 s) e guarda em disco. Depois disso a lista só cresce com o que o mercado mostrar.
    /// </summary>
    private void MaybeCrawlSpecies()
    {
        if (_crawling || _crawled || DateTime.Now < _nextCrawlAt) return;
        var channel = _runs.Values.Select(r => r.Channel).FirstOrDefault(c => c.IsOpen);
        if (channel is null) return;
        _crawling = true;
        _ = CrawlSpeciesAsync(channel);
    }

    private async Task CrawlSpeciesAsync(PageMarketChannel channel)
    {
        var completed = false;
        try
        {
            var filter = new PokemonFilter(Currency.Gold, false, false, null, "recentes");
            var total = int.MaxValue;
            var perPage = 24;
            for (var page = 0; page * perPage < total && page < 40; page++)
            {
                var wanted = page;
                var answer = new TaskCompletionSource<SpeciesMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Handler(MarketMessage m) { if (m is SpeciesMessage s && s.Page == wanted) answer.TrySetResult(s); }
                channel.Received += Handler;
                try
                {
                    if (!await channel.SendAsync(MarketProtocol.Species(filter, page), CancellationToken.None)) return;
                    if (await Task.WhenAny(answer.Task, Task.Delay(TimeSpan.FromSeconds(6))) != answer.Task) return;
                    var result = answer.Task.Result;
                    total = result.Total;
                    if (result.PerPage > 0) perPage = result.PerPage;
                }
                finally { channel.Received -= Handler; }
                await Task.Delay(TimeSpan.FromMilliseconds(1500));
            }
            completed = true;
        }
        catch (Exception e) when (e is InvalidOperationException or IOException) { }
        finally
        {
            _crawling = false;
            _crawled = completed;
            _nextCrawlAt = DateTime.Now.AddMinutes(1); // se não deu, tenta de novo daqui a pouco
        }
    }

    private static string LoadBridgeScript()
    {
        using var stream = typeof(SniperModule).Assembly.GetManifestResourceStream("Scripts.market-bridge.js")
            ?? throw new InvalidOperationException("Script market-bridge.js não está embutido.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
