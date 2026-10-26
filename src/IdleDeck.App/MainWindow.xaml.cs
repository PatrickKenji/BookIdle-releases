using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IdleDeck.App.Browser;
using IdleDeck.App.Health;
using IdleDeck.App.Layout;
using IdleDeck.App.Login;
using IdleDeck.App.Modules;
using IdleDeck.App.Sidebar;
using IdleDeck.App.Market;
using IdleDeck.App.Twitch;
using System.Windows.Documents;
using IdleDeck.Core;
using IdleDeck.Core.Audio;
using IdleDeck.Core.Health;
using IdleDeck.Core.Layout;
using IdleDeck.Core.Modules;
using IdleDeck.Core.Net;
using IdleDeck.Core.Profiles;
using IdleDeck.Core.Settings;
using IdleDeck.Core.Twitch;
using IdleDeck.Core.Vote;
using IdleDeck.Core.Vault;

namespace IdleDeck.App;

public partial class MainWindow : Window, IModuleHostServices
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    /// <summary>Todas as contas de todos os perfis; ficam sempre no Grid central.</summary>
    private readonly List<AccountView> _views = [];
    private readonly Dictionary<AccountView, AccountItem> _items = [];
    private readonly ObservableCollection<ProfileCard> _cards = [];
    private readonly MemoryResetPolicy _resetPolicy = new(TimeSpan.FromMinutes(10));
    private readonly MemoryMonitor _memoryMonitor;
    private readonly KeepAliveService _keepAlive;
    private readonly TrayIcon _tray;
    private readonly VaultStore _vault = new(AppPaths.VaultFile);
    private readonly AutoLoginService _autoLogin = new();
    private readonly ModuleHost _modules;
    /// <summary>Janela aberta de cada módulo (um popup por módulo; clicar de novo traz para frente).</summary>
    private readonly Dictionary<string, ModuleWindow> _janelasDeModulo = [];
    /// <summary>Linha de resumo embaixo do botão do módulo na lateral.</summary>
    private readonly List<ModuleButton> _moduleButtons = [];
    private readonly AccountDragController _drag;
    private const string TopIdleVotedMessage = "topidle:voted";
    private static readonly TimeSpan VoteResyncAfter = TimeSpan.FromSeconds(20);
    private static readonly string TopIdleVoteScript = AccountWebWindow.LoadScript("Scripts.topidle-vote.js");
    /// <summary>Lê da página do jogo quando foi o último voto e quando libera de novo (vale até para votos feitos fora do app).</summary>
    private static readonly string TopIdleStatusScript = AccountWebWindow.LoadScript("Scripts.topidle-status.js");
    private static readonly string AutoCatchScript = AccountWebWindow.LoadScript("Scripts.auto-catch.js");
    private const string TopIdleStatusPrefix = "topidle:status:";
    private readonly TwitchService _twitch;
    private readonly Dictionary<AccountView, AccountWebWindow> _voteWindows = [];
    private readonly System.Windows.Threading.DispatcherTimer _voteTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private Dictionary<string, Credential>? _credentials;
    private string? _masterPassword;
    private BrowserHost? _host;
    private DomainBlocker? _blocker;
    private int _nextRuntimeId = 1;
    private bool _immersive;
    private bool _exiting;
    private bool _recreating;
    private bool _browserNotice;
    private WindowState _restoreState = WindowState.Maximized;

    public MainWindow(AppSettings settings, SettingsStore store)
    {
        _settings = settings;
        _store = store;
        _modules = new ModuleHost(ModuleHost.All(this));
        InitializeComponent();
        if (App.IsTestCopy) Title = "BookIdle " + Loc.T("(TESTE)");
        _drag = new AccountDragController(AccountsGrid, VisibleViews, DropAccount);
        _twitch = new TwitchService(Dispatcher, CoreOfAccount);
        _twitch.Changed += () => Dispatcher.BeginInvoke(RefreshTwitchUi);
        foreach (var module in _modules.Modules) module.SummaryChanged += (_, _) => RefreshSummaries();

        ProfileList.ItemsSource = _cards;
        foreach (var profile in _settings.Profiles)
        {
            var card = new ProfileCard(profile);
            _cards.Add(card);
            foreach (var account in profile.Accounts) CreateAccountView(card, account);
        }

        _memoryMonitor = new MemoryMonitor(() => _host, () => _views);
        _memoryMonitor.Updated += OnMemorySampled;
        _keepAlive = new KeepAliveService(Dispatcher);
        _keepAlive.BrowserProcessExited += async (_, _) => await RecreateBrowserAsync();
        _tray = new TrayIcon(ShowFromTray, ResetAll, ExitApp, () => _ = CheckForUpdateAsync(manual: true),
            Updates.AppVersion.Current + (App.IsTestCopy ? " " + Loc.T("(TESTE)") : ""));

        _voteTimer.Tick += (_, _) => { foreach (var item in _items.Values) item.RefreshVote(); };
        _voteTimer.Start();
        BuildModuleButtons();
        ApplyLayout();
        UpdateMuteUi();
        RefreshTwitchUi();
        Loaded += async (_, _) =>
        {
            _keepAlive.Start();
            _memoryMonitor.Start();
            StartUpdateChecks();
            var browsers = StartBrowsersWithRetryAsync();
            if (StartInTray) HideToTray();
            UnlockVaultAtStartup();
            SyncTwitch();
            await browsers;
            // as células já têm o tamanho final: garante que cada WebView2 esteja do tamanho certo
            foreach (var view in _views) view.RealinharTela();
            ShowWhatsNewIfPending();
        };
    }

    /// <summary>Aberto pelo início com o Windows: carrega as contas e vai direto para a bandeja.</summary>
    public bool StartInTray { get; set; }

    // ---------- Atualização automática ----------

    private readonly Updates.AppUpdater _updater = new();
    private Velopack.UpdateInfo? _pendingUpdate;
    private string? _declinedVersion;
    private bool _checkingUpdate;

    /// <summary>Uma atualização foi baixada e o app vai se trocar ao fechar.</summary>
    public bool HasPendingUpdate => _pendingUpdate is not null;

    public void ApplyPendingUpdate()
    {
        if (_pendingUpdate is { } update) _updater.ApplyAndRestart(update);
    }

    /// <summary>Procura versão nova 30 s depois de abrir e de hora em hora (só se o app foi instalado pelo instalador).</summary>
    private void StartUpdateChecks()
    {
        if (!_updater.Configured || !_updater.Installed) return;
        var first = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        first.Tick += (_, _) => { first.Stop(); _ = CheckForUpdateAsync(manual: false); };
        first.Start();
        var again = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        again.Tick += (_, _) => _ = CheckForUpdateAsync(manual: false);
        again.Start();
    }

    /// <param name="manual">Pedido pelo usuário (menu da bandeja): responde sempre. Automático: só fala se houver versão nova.</param>
    private async Task CheckForUpdateAsync(bool manual)
    {
        if (_checkingUpdate) return;
        if (!_updater.Configured || !_updater.Installed)
        {
            if (manual)
                ArcadeDialog.Show(this, Loc.T("Atualizações"), _updater.Configured
                    ? Loc.T("Esta cópia do BookIdle não foi instalada pelo instalador, então não se atualiza sozinha.")
                    : Loc.T("A atualização automática ainda não foi configurada nesta versão."));
            return;
        }

        _checkingUpdate = true;
        try
        {
            var update = await _updater.CheckAsync();
            if (update is null)
            {
                if (manual) ArcadeDialog.Show(this, Loc.T("Atualizações"), Loc.T("Você já está na versão mais recente ({0}).", Updates.AppVersion.Current));
                return;
            }

            var version = Updates.AppUpdater.VersionOf(update);
            if (!manual && _declinedVersion == version) return; // já disse "agora não" para esta; não insiste até reabrir
            ShowFromTray();
            var message = Updates.AppUpdater.NotesOf(update) is { } notes
                ? Loc.T("Há uma versão nova do BookIdle ({0}); esta é a {1}.\n\nO que muda:\n{2}\n\nAtualizar agora? O app fecha e abre de novo sozinho, e as contas continuam logadas.", version, Updates.AppVersion.Current, notes.Summary)
                : Loc.T("Há uma versão nova do BookIdle ({0}); esta é a {1}.\n\nAtualizar agora? O app fecha e abre de novo sozinho, e as contas continuam logadas.", version, Updates.AppVersion.Current);
            if (!ArcadeDialog.Show(this, Loc.T("Atualização disponível"), message, ArcadeDialogButtons.YesNo, buttonsText: (Loc.T("Atualizar"), Loc.T("Agora não")), guarded: true))
            {
                _declinedVersion = version;
                return;
            }

            _tray.Notify("BookIdle", Loc.T("Baixando a versão {0}…", version));
            await _updater.DownloadAsync(update);
            _pendingUpdate = update;
            ExitApp(); // fecha direito (salva e solta os navegadores); o Closed do App aplica a troca
        }
        catch (Exception e)
        {
            App.Log(e);
            if (manual) ArcadeDialog.Show(this, Loc.T("Atualizações"), Loc.T("Não consegui verificar agora:\n{0}", e.Message), kind: ArcadeDialogKind.Warning);
        }
        finally { _checkingUpdate = false; }
    }

    // ---------- O que mudou (depois de atualizar) ----------

    /// <summary>Já havia configurações ao abrir (não é a primeira instalação).</summary>
    public bool SettingsExisted { get; init; }

    private bool _whatsNewChecked;

    /// <summary>
    /// Na primeira abertura de uma versão nova, mostra as notas detalhadas dela. Se o app abriu na bandeja, espera ele
    /// aparecer: a versão só fica registrada depois que a pessoa viu (ou se não há nada para mostrar).
    /// </summary>
    private void ShowWhatsNewIfPending()
    {
        if (_whatsNewChecked || !IsVisible || WindowState == WindowState.Minimized) return;
        _whatsNewChecked = true;
        var current = Updates.AppVersion.Current;
        if (_settings.LastRunVersion == current) return;
        if (IdleDeck.Core.Updates.ReleaseNotes.ShouldShowAfterUpdate(_settings.LastRunVersion, current, SettingsExisted)
            && Updates.AppUpdater.CurrentNotes() is { } notes)
            ArcadeDialog.Show(this, Loc.T("Novidades da versão {0}", current), notes.Details, wide: true);
        _settings.LastRunVersion = current;
        SaveSettings();
    }

    /// <summary>O app deve abrir de novo depois de fechar (mudança que exige reiniciar o navegador).</summary>
    public bool RestartRequested { get; private set; }

    private GameProfile Profile => _settings.SelectedProfile;

    private ProfileCard CardOf(GameProfile profile) => _cards.First(c => c.Profile == profile);

    /// <summary>Contas do perfil, na ordem do perfil.</summary>
    private List<AccountView> ViewsOf(GameProfile profile) =>
        profile.Accounts.Select(a => _views.First(v => v.Config.ProfileName == a.ProfileName)).ToList();

    // ---------- Contas ----------

    private AccountView CreateAccountView(ProfileCard card, AccountConfig account)
    {
        var view = new AccountView(_nextRuntimeId++, card.Profile, account);
        var item = new AccountItem(view);
        view.HeaderClicked += (_, _) => FocusAccount(view);
        _drag.Attach(view);
        view.ExpandRequested += (_, _) => ExpandAccount(view);
        view.MuteToggleRequested += (_, _) => SetMuted(view, !view.IsMuted);
        view.AutoCatchToggleRequested += (_, _) => SetAutoCatch(view, !view.Config.AutoCatch);
        view.ZoomChanged += (_, _) => SetProfileZoom(view.Profile, view.Zoom);
        view.WebMessage += (_, message) => OnWebMessage(view, message);
        view.NewWindowRequested += (_, e) =>
        {
            if (_host is { } host)
                _ = PopupWindow.OpenAsync(this, host, view, e, (source, message) => OnLoginMessage(view, source, message),
                    open => _autoLogin.SetPopupOpen(view, open));
        };
        _ = view.AddStartupScriptAsync(AutoLoginService.DetectScript);
        _ = view.AddStartupScriptAsync(TopIdleStatusScript);
        _ = view.AddStartupScriptAsync(AutoCatchScript);
        _ = _modules.RegisterScriptsAsync(view);
        _views.Add(view);
        _items[view] = item;
        card.Accounts.Add(item);
        AccountsGrid.Children.Add(view);
        return view;
    }

    /// <summary>Sobe o navegador de uma conta criada com o app já aberto.</summary>
    private async Task StartViewAsync(AccountView view)
    {
        if (_host is null || _blocker is null) return; // o arranque geral vai iniciar
        try
        {
            await view.InitializeAsync(_host, _blocker);
            _keepAlive.Attach(view);
            ApplyLayout();
            view.Start();
        }
        catch (Exception e)
        {
            App.Log(e);
            view.Status = AccountStatus.Error;
        }
    }

    private async Task RemoveAccountViewAsync(AccountView view)
    {
        _keepAlive.Detach(view);
        _twitch.Stop(view.Config.ProfileName);
        _views.Remove(view);
        CardOf(view.Profile).Accounts.Remove(_items[view]);
        _items.Remove(view);
        AccountsGrid.Children.Remove(view);
        await view.DeleteBrowserDataAsync();
        RemoveCredential(view.Config.ProfileName);
    }

    private void OnWebMessage(AccountView view, string message)
    {
        if (message.StartsWith(TopIdleStatusPrefix, StringComparison.Ordinal))
        {
            ApplyVoteStatus(view, message[TopIdleStatusPrefix.Length..]);
            return;
        }
        if (message.StartsWith(MarketProbeLog.Prefix, StringComparison.Ordinal))
        {
            if (_probing.Contains(view)) MarketProbeLog.Write(view.Config.ProfileName, message[MarketProbeLog.Prefix.Length..]);
            return;
        }
        if (_modules.Route(view.RuntimeId, message)) return;
        if (view.Core is { } core) OnLoginMessage(view, core, message);
    }

    private void OnLoginMessage(AccountView view, Microsoft.Web.WebView2.Core.CoreWebView2 source, string message)
    {
        if (AutoLoginService.TryParseState(message) is { } state)
            _ = HandleLoginAsync(view, source, state);
    }

    private async Task HandleLoginAsync(AccountView view, Microsoft.Web.WebView2.Core.CoreWebView2 source, string state)
    {
        try { await _autoLogin.HandleAsync(view, source, state); }
        catch (Exception e) { App.Log(e); }
    }

    // ---------- Diagnóstico do mercado ----------

    private static readonly string MarketProbeScript = AccountWebWindow.LoadScript("Scripts.market-probe.js");
    private readonly HashSet<AccountView> _probing = [];

    /// <summary>Liga o diagnóstico nesta conta (injeta o script e recarrega) ou o silencia (o script some no próximo reinício do app).</summary>
    private async void MarketProbe_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AccountItem item }) return;
        var view = item.View;
        if (!_probing.Remove(view))
        {
            _probing.Add(view);
            await view.AddStartupScriptAsync(MarketProbeScript);
            MarketProbeLog.Write(view.Config.ProfileName, "diagnóstico ligado");
            view.Reload();
        }
        item.IsProbing = _probing.Contains(view);
    }

    // ---------- Navegador ----------

    private async Task StartBrowsersAsync()
    {
        var host = await BrowserHost.CreateAsync(_settings);
        var blocker = new DomainBlocker(_settings.BlockedDomains);
        var views = _views.ToList();
        await Task.WhenAll(views.Select(v => v.InitializeAsync(host, blocker)));
        _host = host;
        _blocker = blocker;
        foreach (var view in views) _keepAlive.Attach(view);
        ApplyLayout();
        foreach (var view in views) view.Start();
    }

    /// <summary>
    /// Sobe o navegador e as contas, tentando de novo com espera crescente se falhar
    /// (ex.: o navegador anterior ainda fechando depois de um reinício, ou o WebView2 atualizando).
    /// </summary>
    private async Task StartBrowsersWithRetryAsync()
    {
        var backoff = new RetryBackoff();
        while (!_exiting)
        {
            try
            {
                await StartBrowsersAsync();
                if (_browserNotice) ShowNotice(null);
                _browserNotice = false;
                return;
            }
            catch (Exception e)
            {
                App.Log(e);
                _host = null;
                var delay = backoff.NextDelay();
                _browserNotice = true;
                ShowNotice(Loc.T("O navegador não abriu ({0}). Tentando de novo em {1:0} s…", e.Message, delay.TotalSeconds));
                foreach (var view in _views)
                {
                    view.Recreate();
                    view.Status = AccountStatus.Error;
                }
                await Task.Delay(delay);
            }
        }
    }

    /// <summary>O processo principal do navegador morreu: recria o ambiente e todas as contas.</summary>
    private async Task RecreateBrowserAsync()
    {
        if (_recreating || _exiting) return;
        _recreating = true;
        try
        {
            await Task.Yield(); // não descartar o WebView2 dentro do próprio evento dele
            _host = null;
            foreach (var view in _views) view.Recreate();
            await Task.Delay(TimeSpan.FromSeconds(2));
            await StartBrowsersWithRetryAsync();
        }
        finally
        {
            _recreating = false;
        }
    }

    /// <summary>Aviso visível na barra superior (null esconde).</summary>
    private void ShowNotice(string? text)
    {
        NoticeText.Text = text ?? "";
        NoticeText.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------- Memória ----------

    private void OnMemorySampled(IReadOnlyList<AccountView> views, MemorySnapshot snapshot)
    {
        var now = DateTime.UtcNow;
        var limitBytes = _settings.MemoryLimitMb * 1024.0 * 1024.0;
        for (var i = 0; i < views.Count; i++)
        {
            var view = views[i];
            if (!_items.TryGetValue(view, out var item)) continue; // removida durante a amostra
            var bytes = snapshot.AccountBytes[i];
            item.MemoryBytes = bytes;
            item.RamFraction = Math.Clamp(bytes / limitBytes, 0, 1);
            if (_resetPolicy.ShouldAutoReset(view.RuntimeId, bytes, _settings.MemoryLimitMb, _settings.AutoResetEnabled, now))
                ResetAccount(view);
        }
        foreach (var card in _cards) card.Refresh();
        SharedMemoryText.Text = Format.Bytes(snapshot.SharedBytes);
        TotalMemoryText.Text = Format.Bytes(snapshot.TotalBytes);
    }

    // ---------- Layout ----------

    private void ApplyLayout()
    {
        var windowShown = IsVisible && WindowState != WindowState.Minimized;
        var profileViews = ViewsOf(Profile);
        var result = LayoutCalculator.Compute(Profile.Layout, profileViews.Count, Profile.FocusedAccount);

        AccountsGrid.RowDefinitions.Clear();
        foreach (var w in result.RowWeights)
            AccountsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(w, GridUnitType.Star) });
        AccountsGrid.ColumnDefinitions.Clear();
        foreach (var w in result.ColumnWeights)
            AccountsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });

        // contas de outros perfis: escondidas, mas rodando
        foreach (var view in _views.Except(profileViews))
        {
            view.Visibility = Visibility.Collapsed;
            view.SetActive(false);
            _items[view].IsFocused = false;
        }

        var multi = Profile.Layout != LayoutMode.Tabs && profileViews.Count > 1;
        for (var i = 0; i < profileViews.Count; i++)
        {
            var (view, cell) = (profileViews[i], result.Cells[i]);
            Grid.SetRow(view, cell.Row);
            Grid.SetColumn(view, cell.Column);
            Grid.SetRowSpan(view, cell.RowSpan);
            Grid.SetColumnSpan(view, cell.ColumnSpan);
            view.Visibility = cell.Visible ? Visibility.Visible : Visibility.Collapsed;
            view.Margin = multi ? new Thickness(3) : new Thickness(0);
            view.ShowHeader = multi;
            view.IsHighlighted = multi && i == Profile.FocusedAccount;
            view.SetActive(cell.Visible && windowShown);
            _items[view].IsFocused = i == Profile.FocusedAccount;
        }

        foreach (var card in _cards) card.IsSelected = card.Profile == Profile;
        EmptyState.Visibility = profileViews.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LayoutTabsButton.Style = LayoutButtonStyle(LayoutMode.Tabs);
        LayoutGridButton.Style = LayoutButtonStyle(LayoutMode.Grid2x2);
        LayoutMainButton.Style = LayoutButtonStyle(LayoutMode.MainPlus3);
        BuildTabStrip(profileViews);
        RailAccounts.ItemsSource = CardOf(Profile).Accounts; // quadrados do menu recolhido
        ContasMudaram?.Invoke(this, EventArgs.Empty);
        ZoomText.Text = $"{Profile.Zoom * 100:0}%";
        ApplyImmersive();
    }

    // ---------- Zoom ----------

    private const double ZoomStep = 0.1;

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetProfileZoom(Profile, Profile.Zoom - ZoomStep);
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetProfileZoom(Profile, Profile.Zoom + ZoomStep);

    /// <summary>Aplica o zoom a todas as contas do perfil (arredondado a 5%, entre 50% e 150%).</summary>
    private void SetProfileZoom(GameProfile profile, double zoom)
    {
        zoom = Math.Clamp(Math.Round(zoom * 20) / 20, GameProfile.MinZoom, GameProfile.MaxZoom);
        profile.Zoom = zoom;
        foreach (var view in ViewsOf(profile))
            if (Math.Abs(view.Zoom - zoom) >= 0.005) view.SetZoom(zoom);
        if (profile == Profile) ZoomText.Text = $"{zoom * 100:0}%";
        SaveSettings();
    }

    private Style LayoutButtonStyle(LayoutMode mode) =>
        (Style)FindResource(Profile.Layout == mode ? "ArcadeSelectedButton" : "ArcadeButton");

    private void BuildTabStrip(List<AccountView> profileViews)
    {
        TabStrip.Children.Clear();
        for (var i = 0; i < profileViews.Count; i++)
        {
            var view = profileViews[i];
            var tab = new Button
            {
                Content = view.Config.Name,
                Style = (Style)FindResource(i == Profile.FocusedAccount ? "ArcadeTabSelected" : "ArcadeTab"),
            };
            tab.Click += (_, _) => FocusAccount(view);
            TabStrip.Children.Add(tab);
        }
    }

    private void ApplyImmersive()
    {
        ApplySidebar();

        TabStrip.Visibility = !_immersive && Profile.Layout == LayoutMode.Tabs && Profile.Accounts.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Lateral expandida (310 px), recolhida (barra de 72 px com o livro e as contas) ou escondida pelo F11.</summary>
    private void ApplySidebar()
    {
        var recolhida = _settings.SidebarCollapsed;
        SidePanel.Visibility = _immersive || recolhida ? Visibility.Collapsed : Visibility.Visible;
        SideRail.Visibility = !_immersive && recolhida ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Recolhe/expande o menu lateral (fica guardado nas configurações).</summary>
    private void ToggleSidebar_Click(object sender, RoutedEventArgs e) => ToggleSidebar();

    private void ToggleSidebar()
    {
        _settings.SidebarCollapsed = !_settings.SidebarCollapsed;
        ApplySidebar();
        SaveSettings();
    }

    /// <summary>Contas do perfil selecionado que estão na tela (Grade e 1+N mostram todas).</summary>
    private IReadOnlyList<AccountView> VisibleViews() => ViewsOf(Profile).Where(v => v.Visibility == Visibility.Visible).ToList();

    /// <summary>Soltou uma conta sobre outra: troca de lugar (no 1+N, soltar sobre a grande a torna a principal).</summary>
    private void DropAccount(AccountView dragged, AccountView target)
    {
        var targetIndex = Profile.Accounts.FindIndex(a => a.ProfileName == target.Config.ProfileName);
        if (Profile.Layout == LayoutMode.MainPlus3 && targetIndex == Profile.FocusedAccount)
        {
            FocusAccount(dragged);
            return;
        }
        if (!ProfileOperations.SwapAccounts(Profile, dragged.Config.ProfileName, target.Config.ProfileName)) return;
        var accounts = CardOf(Profile).Accounts; // a lateral acompanha a nova ordem
        var (a, b) = (accounts.IndexOf(_items[dragged]), accounts.IndexOf(_items[target]));
        if (a >= 0 && b >= 0) (accounts[a], accounts[b]) = (accounts[b], accounts[a]);
        ApplyLayout();
        SaveSettings();
    }

    /// <summary>Botão ⛶ de uma janela da Grade/1+N: vai para as Abas com ela selecionada.</summary>
    private void ExpandAccount(AccountView view)
    {
        if (!ProfileOperations.ExpandToTabs(view.Profile, view.Config.ProfileName)) return;
        _settings.SelectedProfileId = view.Profile.Id;
        ApplyLayout();
        SaveSettings();
        view.RealinharTela();
    }

    // ---------- Twitch ----------

    /// <summary>O navegador de uma conta (pelo nome do perfil dela), ou null se ainda não subiu.</summary>
    private Microsoft.Web.WebView2.Core.CoreWebView2? CoreOfAccount(string profileName) =>
        _views.FirstOrDefault(v => v.Config.ProfileName == profileName)?.Core;

    /// <summary>Deixa as conexões do chat iguais ao que está configurado (contas com Twitch + canais do perfil).</summary>
    private void SyncTwitch()
    {
        var desired = new List<TwitchService.Desired>();
        foreach (var profile in _settings.Profiles)
        {
            if (profile.TwitchChannels.Count == 0) continue;
            foreach (var account in profile.Accounts)
                if (account.TwitchLogin is { Length: > 0 } login)
                    desired.Add(new TwitchService.Desired(account.ProfileName, login, profile.TwitchChannels));
        }
        _twitch.Sync(desired);
        RefreshTwitchUi();
    }

    /// <summary>Ícone e dica da Twitch em cada conta: verde (em todos os canais), amarelo (parcial), cinza (sem Twitch/sem chave).</summary>
    private void RefreshTwitchUi()
    {
        foreach (var (view, item) in _items)
        {
            var channels = view.Profile.TwitchChannels.Count;
            var login = view.Config.TwitchLogin;
            if (login is null)
            {
                item.SetTwitch(channels > 0 ? TwitchIcon.Gray : TwitchIcon.Hidden,
                    Loc.T("Sem Twitch conectada. Botão direito na conta → Conectar Twitch…"));
                continue;
            }
            if (channels == 0)
            {
                item.SetTwitch(TwitchIcon.Gray, Loc.T("Twitch {0}: nenhum canal configurado (⋯ do perfil → Twitch)", login));
                continue;
            }
            switch (_twitch.StateOf(view.Config.ProfileName))
            {
                case null:
                    item.SetTwitch(TwitchIcon.Gray, Loc.T("Twitch {0}: desligada. Botão direito → Reconectar Twitch…", login));
                    break;
                case { Status: TwitchStatus.AuthFailed }:
                    item.SetTwitch(TwitchIcon.Gray, Loc.T("Twitch {0}: a conta saiu da Twitch (ou entrou outra). Botão direito → Reconectar Twitch…", login));
                    break;
                case { Status: TwitchStatus.Connected } s when s.Joined == s.Wanted && s.Wanted > 0:
                    item.SetTwitch(TwitchIcon.Green, Loc.T("Twitch {0}: ligada em {1} de {2} canais", login, s.Joined, s.Wanted));
                    break;
                case var s:
                    item.SetTwitch(TwitchIcon.Yellow, s!.Status == TwitchStatus.Connected
                        ? Loc.T("Twitch {0}: entrando nos canais ({1} de {2})…", login, s.Joined, s.Wanted)
                        : Loc.T("Twitch {0}: conectando…", login));
                    break;
            }
        }
    }

    private void EditTwitchChannels(ProfileCard card)
    {
        var window = new TwitchChannelsWindow(this, card.Profile.Name, card.Profile.TwitchChannels);
        if (window.ShowDialog() != true) return;
        card.Profile.TwitchChannels = window.Channels;
        SaveSettings();
        SyncTwitch();
    }

    private async void ConnectTwitch_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: AccountItem item }) await ConnectTwitchAsync(item);
    }

    /// <summary>
    /// Entrar na Twitch como num navegador: abre o site da Twitch no navegador DESTA conta (cada conta tem a sua sessão)
    /// e, assim que a pessoa entrar, liga o chat com o login que ficou ali. Sem registrar app nem digitar código.
    /// </summary>
    private async Task ConnectTwitchAsync(AccountItem item)
    {
        var view = item.View;
        if (_host is null || view.Core is not { } core)
        {
            ArcadeDialog.Show(this, "Twitch", Loc.T("O navegador ainda está abrindo. Tente de novo em instantes."));
            return;
        }

        var before = await ReadTwitchSessionAsync(core);
        if (before is not null && ArcadeDialog.Show(this, "Twitch",
                Loc.T("Esta conta já está logada na Twitch como {0}.\n\nUsar essa Twitch?\n\n(Se for a conta errada, escolha Não: na janela que abrir, saia da Twitch e entre na conta certa.)", before.Login),
                ArcadeDialogButtons.YesNo))
        {
            CompleteTwitchConnection(view, before.Login);
            return;
        }

        var window = await AccountWebWindow.OpenAsync(this, _host, view, "Twitch", "https://www.twitch.tv/login");
        if (window is null)
        {
            ArcadeDialog.Show(this, "Twitch", Loc.T("Não foi possível abrir a página da Twitch."), kind: ArcadeDialogKind.Error);
            return;
        }

        // enquanto a janela estiver aberta, vigia o login (a chave nova é diferente da que já havia, se havia)
        var closed = false;
        window.Closed += (_, _) => closed = true;
        while (!closed)
        {
            await Task.Delay(1500);
            if (closed || view.Core is not { } current) break;
            if (await ReadTwitchSessionAsync(current) is { } now && now.Token != before?.Token)
            {
                window.Close();
                CompleteTwitchConnection(view, now.Login);
                return;
            }
        }
    }

    private static async Task<TwitchSession?> ReadTwitchSessionAsync(Microsoft.Web.WebView2.Core.CoreWebView2 core)
    {
        try { return await TwitchSession.ReadAsync(core); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            App.Log(ex); // navegador sendo recriado: tenta de novo no próximo toque
            return null;
        }
    }

    private void CompleteTwitchConnection(AccountView view, string twitchLogin)
    {
        var accounts = view.Profile.Accounts;
        var index = accounts.FindIndex(a => a.ProfileName == view.Config.ProfileName);
        if (index < 0) return;
        accounts[index] = accounts[index] with { TwitchLogin = twitchLogin };
        view.UpdateConfig(accounts[index]);
        SaveSettings();
        _twitch.Stop(view.Config.ProfileName); // login novo: recomeça a conexão
        SyncTwitch();

        if (view.Profile.TwitchChannels.Count == 0)
            ArcadeDialog.Show(this, "Twitch", Loc.T("Twitch {0} conectada.\n\nFalta dizer em quais canais ela deve ficar: ⋯ do perfil → Twitch (bônus de live)…", twitchLogin));
    }

    private void DisconnectTwitch_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { Tag: AccountItem item }) return;
        var view = item.View;
        var accounts = view.Profile.Accounts;
        var index = accounts.FindIndex(a => a.ProfileName == view.Config.ProfileName);
        if (index < 0) return;
        accounts[index] = accounts[index] with { TwitchLogin = null };
        view.UpdateConfig(accounts[index]);
        SaveSettings();
        _twitch.Stop(view.Config.ProfileName);
        SyncTwitch();
    }

    // ---------- Voto no TopIdle ----------

    private async void Vote_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { Tag: AccountItem item }) return;
        var view = item.View;
        if (_voteWindows.TryGetValue(view, out var open))
        {
            open.Activate();
            return;
        }
        if (_host is null)
        {
            ArcadeDialog.Show(this, "TopIdle", Loc.T("O navegador ainda está abrindo. Tente de novo em instantes."));
            return;
        }
        var voted = false;
        var window = await AccountWebWindow.OpenAsync(this, _host, view, "TopIdle", VoteCooldown.VoteUrl, TopIdleVoteScript,
            message =>
            {
                if (message != TopIdleVotedMessage) return;
                voted = true;
                RecordVote(view);
            });
        if (window is null)
        {
            ArcadeDialog.Show(this, "TopIdle", Loc.T("Não foi possível abrir a página de voto."), kind: ArcadeDialogKind.Error);
            return;
        }
        _voteWindows[view] = window;
        var openedAt = DateTime.UtcNow;
        window.Closed += (_, _) =>
        {
            _voteWindows.Remove(view);
            // a página não avisou o voto, mas a janela ficou aberta o bastante para ter votado: o jogo, ao recarregar,
            // diz o status verdadeiro (topidle-status.js) e o botão do menu se acerta sozinho
            if (!voted && DateTime.UtcNow - openedAt > VoteResyncAfter && _items.ContainsKey(view)) view.Reload();
        };
    }

    /// <summary>A página confirmou o voto: guarda a hora para a contagem de 24 h.</summary>
    private void RecordVote(AccountView view) => SetLastVote(view, DateTime.UtcNow);

    /// <summary>O jogo contou (na página da conta) quando foi o último voto: guarda, para o botão mostrar o tempo que falta.</summary>
    private void ApplyVoteStatus(AccountView view, string json)
    {
        if (Core.Vote.TopIdleStatus.Parse(json) is not { } status) return;
        var merged = status.Reconcile(view.Config.LastVoteUtc);
        if (view.Config.LastVoteUtc is { } stored && Math.Abs((stored - merged).TotalSeconds) < 1) return;
        SetLastVote(view, merged);
    }

    private void SetLastVote(AccountView view, DateTime whenUtc)
    {
        var accounts = view.Profile.Accounts;
        var index = accounts.FindIndex(a => a.ProfileName == view.Config.ProfileName);
        if (index < 0) return;
        accounts[index] = accounts[index] with { LastVoteUtc = whenUtc };
        view.UpdateConfig(accounts[index]);
        SaveSettings();
        if (_items.TryGetValue(view, out var item)) item.RefreshVote();
    }

    // ---------- Lateral: reordenar contas arrastando ----------

    private AccountItem? _rowPressed;
    private Point _rowPressPoint;
    private Adorner? _insertion;
    private AdornerLayer? _insertionLayer;

    private static bool IsInteractive(object? source)
    {
        for (var node = source as DependencyObject; node is not null;)
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase) return true;
            node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    private void AccountRow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _rowPressed = e.ChangedButton == MouseButton.Left && !IsInteractive(e.OriginalSource) && sender is FrameworkElement { Tag: AccountItem item }
            ? item
            : null;
        _rowPressPoint = e.GetPosition(null);
    }

    /// <summary>Passou da distância mínima com o botão apertado: começa o arrasto da conta.</summary>
    private void AccountRow_MouseMove(object sender, MouseEventArgs e)
    {
        if (_rowPressed is not { } item || e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement row) return;
        var moved = e.GetPosition(null) - _rowPressPoint;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _rowPressed = null;
        row.Opacity = 0.5;
        try { DragDrop.DoDragDrop(row, new DataObject(typeof(AccountItem), item), DragDropEffects.Move); }
        finally
        {
            row.Opacity = 1;
            ClearInsertion();
        }
    }

    /// <summary>A conta arrastada, a conta sobre a qual está e se cairia depois dela. Só vale dentro do mesmo perfil.</summary>
    private static (AccountItem Dragged, AccountItem Target, bool After)? RowDrop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AccountItem target } row) return null;
        if (e.Data.GetData(typeof(AccountItem)) is not AccountItem dragged) return null;
        if (dragged == target || dragged.View.Profile != target.View.Profile) return null;
        return (dragged, target, e.GetPosition(row).Y >= row.ActualHeight / 2);
    }

    private void AccountRow_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (RowDrop(sender, e) is not { } drop)
        {
            e.Effects = DragDropEffects.None;
            ClearInsertion();
            return;
        }
        e.Effects = DragDropEffects.Move;
        ShowInsertion((FrameworkElement)sender, drop.After);
    }

    private void AccountRow_DragLeave(object sender, DragEventArgs e) => ClearInsertion();

    private void AccountRow_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearInsertion();
        if (RowDrop(sender, e) is { } drop) MoveAccountInSidebar(drop.Dragged, drop.Target, drop.After);
    }

    private void ShowInsertion(FrameworkElement row, bool below)
    {
        ClearInsertion();
        _insertionLayer = AdornerLayer.GetAdornerLayer(row);
        if (_insertionLayer is null) return;
        _insertion = new InsertionAdorner(row, below);
        _insertionLayer.Add(_insertion);
    }

    private void ClearInsertion()
    {
        if (_insertion is not null) _insertionLayer?.Remove(_insertion);
        _insertion = null;
        _insertionLayer = null;
    }

    /// <summary>Soltou na lista: muda a ordem das contas do perfil (e, com ela, as abas, a grade e o menu recolhido).</summary>
    private void MoveAccountInSidebar(AccountItem dragged, AccountItem target, bool after)
    {
        var profile = dragged.View.Profile;
        if (!ProfileOperations.MoveAccount(profile, dragged.View.Config.ProfileName, target.View.Config.ProfileName, after)) return;
        var items = CardOf(profile).Accounts;
        for (var i = 0; i < profile.Accounts.Count; i++)
        {
            var item = items.First(a => a.View.Config.ProfileName == profile.Accounts[i].ProfileName);
            var at = items.IndexOf(item);
            if (at != i) items.Move(at, i);
        }
        ApplyLayout();
        SaveSettings();
    }

    // ---------- Mudo ----------

    /// <summary>Muta/tira o mudo de uma conta; fica guardado e volta igual ao abrir o app.</summary>
    private void SetMuted(AccountView view, bool muted)
    {
        var accounts = view.Profile.Accounts;
        var index = accounts.FindIndex(a => a.ProfileName == view.Config.ProfileName);
        if (index < 0 || accounts[index].Muted == muted) return;
        accounts[index] = accounts[index] with { Muted = muted };
        view.UpdateConfig(accounts[index]);
        _items[view].IsMuted = muted;
        SaveSettings();
        UpdateMuteUi();
    }

    /// <summary>Botão geral: todas as contas de todos os perfis; se todas já estão mudas, tira o mudo de todas.</summary>
    private void MuteAll_Click(object sender, RoutedEventArgs e)
    {
        var target = MuteRule.TargetState(_views.Select(v => v.IsMuted));
        foreach (var view in _views.ToList()) SetMuted(view, target);
    }

    private void ToggleMute_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AccountItem item }) SetMuted(item.View, !item.View.IsMuted);
        e.Handled = true;
    }

    // ---------- Captura automática ----------

    /// <summary>Liga/desliga a captura automática de uma conta; fica guardado e volta igual ao abrir o app.</summary>
    private void SetAutoCatch(AccountView view, bool on)
    {
        var accounts = view.Profile.Accounts;
        var index = accounts.FindIndex(a => a.ProfileName == view.Config.ProfileName);
        if (index < 0 || accounts[index].AutoCatch == on) return;
        accounts[index] = accounts[index] with { AutoCatch = on };
        view.UpdateConfig(accounts[index]);
        _items[view].IsAutoCatch = on;
        SaveSettings();
    }

    private void ToggleAutoCatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AccountItem item }) SetAutoCatch(item.View, !item.View.Config.AutoCatch);
        e.Handled = true;
    }

    /// <summary>Ícones do mudo geral (🔇 só quando todas estão mudas).</summary>
    private void UpdateMuteUi()
    {
        var allMuted = _views.Count > 0 && _views.All(v => v.IsMuted);
        var glyph = allMuted ? "🔇" : "🔊";
        var tip = allMuted ? Loc.T("Tirar o mudo de todas as contas") : Loc.T("Mutar todas as contas");
        foreach (var button in new[] { RailMuteButton, SideMuteButton })
        {
            button.Content = glyph;
            button.ToolTip = tip;
        }
    }

    private void Theme_Click(object sender, RoutedEventArgs e) => ToggleTheme();

    private void ToggleTheme()
    {
        _settings.Theme = _settings.Theme == ThemeKind.Dark ? ThemeKind.Arcade : ThemeKind.Dark;
        Themes.ThemeManager.Apply(_settings.Theme);
        SaveSettings();
    }

    private void SetLayout(LayoutMode mode)
    {
        Profile.Layout = mode;
        ApplyLayout();
        SaveSettings();
    }

    private void SelectProfile(GameProfile profile)
    {
        if (_settings.SelectedProfileId == profile.Id) return;
        _settings.SelectedProfileId = profile.Id;
        RefreshSummaries(); // os botões dependem da URL do perfil, não da seleção: só o resumo muda
        ApplyLayout();
        SaveSettings();
    }

    /// <summary>Dá foco a uma conta (seleciona o perfil dela se for outro).</summary>
    private void FocusAccount(AccountView view)
    {
        var index = view.Profile.Accounts.FindIndex(a => a.ProfileName == view.Config.ProfileName);
        if (index < 0) return;
        var changed = view.Profile != Profile || index != view.Profile.FocusedAccount;
        view.Profile.FocusedAccount = index;
        if (view.Profile != Profile)
        {
            SelectProfile(view.Profile);
            return;
        }
        if (!changed) return;
        ApplyLayout();
        SaveSettings();
    }

    private void SaveSettings()
    {
        try { _store.Save(_settings); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { App.Log(e); }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) // digitando um nome: não roubar as teclas
        {
            base.OnPreviewKeyDown(e);
            return;
        }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        switch (key)
        {
            case Key.D1 or Key.NumPad1 when ctrl: SetLayout(LayoutMode.Tabs); break;
            case Key.D2 or Key.NumPad2 when ctrl: SetLayout(LayoutMode.Grid2x2); break;
            case Key.D3 or Key.NumPad3 when ctrl: SetLayout(LayoutMode.MainPlus3); break;
            case Key.OemMinus or Key.Subtract when ctrl: SetProfileZoom(Profile, Profile.Zoom - ZoomStep); break;
            case Key.OemPlus or Key.Add when ctrl: SetProfileZoom(Profile, Profile.Zoom + ZoomStep); break;
            case Key.D0 or Key.NumPad0 when ctrl: SetProfileZoom(Profile, 1.0); break;
            case Key.Tab when ctrl:
                var views = ViewsOf(Profile);
                if (views.Count > 0) FocusAccount(views[(Profile.FocusedAccount + 1) % views.Count]);
                break;
            case Key.F11:
                _immersive = !_immersive;
                ApplyImmersive();
                break;
            case Key.Escape when _drag.IsDragging:
                _drag.Cancel();
                break;
            case Key.B when ctrl:
                ToggleSidebar();
                break;
            case Key.T when ctrl:
                ToggleTheme();
                break;
            default:
                base.OnPreviewKeyDown(e);
                return;
        }
        e.Handled = true;
    }

    // ---------- Lateral: perfis ----------

    private void ProfileHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProfileCard card }) SelectProfile(card.Profile);
    }

    /// <summary>Clique no quadrado da conta no menu recolhido: dá foco naquela conta.</summary>
    private void RailAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AccountItem item }) FocusAccount(item.View);
        e.Handled = true;
    }

    private void ToggleCollapse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ProfileCard card }) return;
        card.IsCollapsed = !card.IsCollapsed;
        SaveSettings();
        e.Handled = true;
    }

    private void ProfileMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ProfileCard card } button) return;
        var edit = new MenuItem { Header = Loc.T("Editar perfil") };
        edit.Click += (_, _) => EditProfile(card);
        var twitch = new MenuItem { Header = Loc.T("Twitch (bônus de live)…") };
        twitch.Click += (_, _) => EditTwitchChannels(card);
        var delete = new MenuItem { Header = Loc.T("Excluir perfil"), Foreground = (System.Windows.Media.Brush)FindResource("Err") };
        delete.Click += async (_, _) => await DeleteProfileAsync(card);
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(edit);
        menu.Items.Add(twitch);
        menu.Items.Add(delete);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var window = new ProfileWindow { Owner = this };
        if (window.ShowDialog() != true) return;
        var profile = ProfileOperations.AddProfile(_settings, window.ProfileName, window.Url, window.MaxAccounts);
        _cards.Add(new ProfileCard(profile));
        BuildModuleButtons();
        ApplyLayout();
        SaveSettings();
    }

    private void EditProfile(ProfileCard card)
    {
        var window = new ProfileWindow(card.Profile) { Owner = this };
        if (window.ShowDialog() != true) return;
        var urlChanged = card.Profile.Url != window.Url;
        card.Profile.Name = window.ProfileName;
        card.Profile.Url = window.Url;
        card.Profile.MaxAccounts = window.MaxAccounts;
        card.Refresh();
        if (urlChanged)
            foreach (var view in ViewsOf(card.Profile)) view.Start();
        BuildModuleButtons();
        SaveSettings();
    }

    private async Task DeleteProfileAsync(ProfileCard card)
    {
        if (_settings.Profiles.Count <= 1)
        {
            ArcadeDialog.Show(this, Loc.T("Excluir perfil"), Loc.T("Este é o único perfil de jogo. Crie outro antes de excluir este."));
            return;
        }
        var count = card.Profile.Accounts.Count;
        if (!ArcadeDialog.Show(this, Loc.T("Excluir perfil"),
                Loc.T("Excluir \"{0}\" e suas {1} conta(s)? O login salvo dessas contas será apagado.", card.Profile.Name, count),
                ArcadeDialogButtons.YesNo, ArcadeDialogKind.Warning)) return;

        foreach (var view in ViewsOf(card.Profile)) await RemoveAccountViewAsync(view);
        ProfileOperations.RemoveProfile(_settings, card.Profile.Id);
        _cards.Remove(card);
        SaveVaultIfUnlocked();
        BuildModuleButtons();
        ApplyLayout();
        SaveSettings();
    }

    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProfileCard card }) await AddAccountAsync(card);
    }

    private async void AddAccountToSelected_Click(object sender, RoutedEventArgs e) => await AddAccountAsync(CardOf(Profile));

    private async Task AddAccountAsync(ProfileCard card)
    {
        var account = ProfileOperations.AddAccount(_settings, card.Profile);
        if (account is null) return;
        var view = CreateAccountView(card, account);
        card.Profile.FocusedAccount = card.Profile.Accounts.Count - 1;
        card.Refresh();
        if (card.Profile != Profile) SelectProfile(card.Profile);
        else ApplyLayout();
        SaveSettings();
        await StartViewAsync(view);
    }

    private async void RemoveAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AccountItem item }) return;
        var view = item.View;
        if (!ArcadeDialog.Show(this, Loc.T("Remover conta"),
                Loc.T("Remover \"{0}\"? O login salvo desta conta será apagado.", view.Config.Name),
                ArcadeDialogButtons.YesNo, ArcadeDialogKind.Warning)) return;

        var profile = view.Profile;
        await RemoveAccountViewAsync(view);
        ProfileOperations.RemoveAccount(profile, view.Config.ProfileName);
        SaveVaultIfUnlocked();
        CardOf(profile).Refresh();
        ApplyLayout();
        SaveSettings();
    }

    // ---------- Lateral: contas ----------

    private void AccountRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AccountItem item } && !item.IsRenaming) FocusAccount(item.View);
    }

    private void AccountName_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { Tag: AccountItem item }) return;
        item.IsRenaming = true;
        e.Handled = true;
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AccountItem item }) item.IsRenaming = true;
        e.Handled = true;
    }

    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box || !box.IsVisible) return;
        Dispatcher.BeginInvoke(() =>
        {
            box.Text = (box.Tag as AccountItem)?.Name ?? box.Text;
            box.Focus();
            box.SelectAll();
        });
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: AccountItem item } box) return;
        if (e.Key == Key.Enter) CommitRename(item, box.Text);
        else if (e.Key == Key.Escape) item.IsRenaming = false;
        else return;
        e.Handled = true;
    }

    private void RenameBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { Tag: AccountItem { IsRenaming: true } item } box) CommitRename(item, box.Text);
    }

    private void CommitRename(AccountItem item, string text)
    {
        item.IsRenaming = false;
        var view = item.View;
        if (ProfileOperations.RenameAccount(view.Profile, view.Config.ProfileName, text) is null) return;
        var config = view.Profile.Accounts.First(a => a.ProfileName == view.Config.ProfileName);
        view.UpdateConfig(config);
        item.Name = config.Name;
        ApplyLayout();
        SaveSettings();
    }

    // ---------- Cofre ----------

    private void UnlockVaultAtStartup()
    {
        if (!_vault.Exists) return;
        if (_vault.RequiresMasterPassword)
        {
            if (UnlockWindow.Prompt(this, _vault, out var credentials, out var master)) SetCredentials(credentials, master);
            return;
        }
        try { SetCredentials(_vault.Unlock(null), null); }
        catch (VaultUnlockException e)
        {
            App.Log(e);
            ShowNotice(Loc.T("Não foi possível abrir o cofre de senhas: login automático desligado. Abra o 🔑 Cofre para apagar e recriar."));
        }
    }

    private void SetCredentials(Dictionary<string, Credential>? credentials, string? master)
    {
        _credentials = credentials;
        _masterPassword = master;
        _autoLogin.SetCredentials(credentials);
    }

    private void RemoveCredential(string profileName) => _credentials?.Remove(profileName);

    /// <summary>Grava o cofre depois de remover contas (só se estiver destravado; senão a credencial fica órfã, sem efeito).</summary>
    private void SaveVaultIfUnlocked()
    {
        if (_credentials is null || !_vault.Exists) return;
        try { _vault.Save(_credentials, _masterPassword); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { App.Log(e); }
    }

    /// <summary>A página de apoio do criador (LivePix). Abre no navegador padrão; nada é enviado pelo app.</summary>
    private const string SupportUrl = "https://livepix.gg/tickelicia";

    private void Support_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SupportUrl) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            App.Log(ex);
            ArcadeDialog.Show(this, Loc.T("Apoiar"), Loc.T("Não consegui abrir o navegador. A página é:\n{0}", SupportUrl), kind: ArcadeDialogKind.Warning);
        }
    }

    private void Vault_Click(object sender, RoutedEventArgs e)
    {
        if (_vault.Exists && _credentials is null)
        {
            if (_vault.RequiresMasterPassword)
            {
                if (!UnlockWindow.Prompt(this, _vault, out var credentials, out var master, Loc.T("Destrave o cofre para editar."))) return;
                SetCredentials(credentials, master);
            }
            else
            {
                try { SetCredentials(_vault.Unlock(null), null); }
                catch (VaultUnlockException)
                {
                    if (!ArcadeDialog.Show(this, Loc.T("Cofre"),
                            Loc.T("Não foi possível abrir o cofre (corrompido ou criado em outro usuário/PC). Apagar o cofre e começar um novo?"),
                            ArcadeDialogButtons.YesNo, ArcadeDialogKind.Warning)) return;
                    if (!TryDeleteVault()) return;
                    SetCredentials(null, null);
                    ShowNotice(null);
                }
            }
        }

        var window = new VaultWindow(_vault, _settings, _credentials ?? [], _masterPassword) { Owner = this };
        if (window.ShowDialog() != true) return;
        SetCredentials(window.Deleted ? null : window.SavedCredentials, window.SavedMasterPassword);
        ApplyLoginMethods(window.Deleted ? null : window.SavedMethods);
    }

    /// <summary>Grava o método de login de cada conta (null = cofre apagado: todas viram Nenhum).</summary>
    private void ApplyLoginMethods(Dictionary<string, LoginMethod>? methods)
    {
        foreach (var profile in _settings.Profiles)
            for (var i = 0; i < profile.Accounts.Count; i++)
            {
                var account = profile.Accounts[i];
                var method = methods is null ? LoginMethod.None : methods.GetValueOrDefault(account.ProfileName, account.Login);
                if (method == account.Login) continue;
                profile.Accounts[i] = account with { Login = method };
                var view = _views.First(v => v.Config.ProfileName == account.ProfileName);
                view.UpdateConfig(profile.Accounts[i]);
                _items[view].Login = method;
            }
        SaveSettings();
    }

    private bool TryDeleteVault()
    {
        try
        {
            _vault.Delete();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ArcadeDialog.Show(this, Loc.T("Cofre"), Loc.T("Não foi possível apagar o cofre: {0}", ex.Message), kind: ArcadeDialogKind.Error);
            return false;
        }
    }

    private async void FillLogin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AccountItem item }) return;
        if (_credentials is null) { Vault_Click(sender, e); return; }
        if (!await _autoLogin.FillAsync(item.View, submit: false))
            ArcadeDialog.Show(this, item.Name, Loc.T("Nenhum formulário de login visível nesta conta, ou não há credencial salva para ela."));
    }

    private async void RetryLogin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AccountItem item }) return;
        _autoLogin.ClearLockout(item.View);
        await _autoLogin.FillAsync(item.View, submit: true);
    }

    // ---------- Botões ----------

    private void LayoutTabs_Click(object sender, RoutedEventArgs e) => SetLayout(LayoutMode.Tabs);
    private void LayoutGrid_Click(object sender, RoutedEventArgs e) => SetLayout(LayoutMode.Grid2x2);
    private void LayoutMain_Click(object sender, RoutedEventArgs e) => SetLayout(LayoutMode.MainPlus3);

    private void ResetAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AccountItem item }) ResetAccount(item.View);
        e.Handled = true;
    }

    private void ResetProfile_Click(object sender, RoutedEventArgs e)
    {
        foreach (var view in ViewsOf(Profile)) ResetAccount(view);
    }

    /// <summary>Reencaixa a janela da conta (corrige WebView2 deslocado/faixa vazia sem recarregar o jogo).</summary>
    private void ResyncView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AccountItem item }) item.View.RealinharTela();
        e.Handled = true;
    }

    private void ResetAll()
    {
        foreach (var view in _views.ToList()) ResetAccount(view);
    }

    private void ResetAccount(AccountView view)
    {
        _resetPolicy.RecordReset(view.RuntimeId, DateTime.UtcNow);
        _ = view.RestartAsync();
    }

    // ---------- Bandeja ----------

    public void ShowFromTray()
    {
        Show();
        WindowState = _restoreState;
        Activate();
        ApplyLayout();
        Dispatcher.BeginInvoke(ShowWhatsNewIfPending, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void HideToTray()
    {
        Hide();
        foreach (var view in _views) view.SetActive(false);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
            return;
        }
        _restoreState = WindowState;
        // Maximizar/restaurar muda o tamanho das células; o WebView2 às vezes fica com o tamanho antigo.
        Dispatcher.InvokeAsync(() =>
        {
            foreach (var view in _views) view.RealinharTela();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        _keepAlive.Dispose();
        _memoryMonitor.Dispose();
        _tray.Dispose();
        base.OnClosing(e);
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_settings) { Owner = this };
        if (window.ShowDialog() != true) return;

        SaveSettings();

        if (window.RestartRequired && ArcadeDialog.Show(this, Loc.T("Reiniciar"),
                Loc.T("Algumas mudanças só valem depois de reiniciar o app (todas as contas recarregam). Reiniciar agora?"),
                ArcadeDialogButtons.YesNo))
        {
            RestartRequested = true;
            ExitApp();
        }
    }

    // ---------- Módulos ----------

    public event EventHandler? ContasMudaram;

    public string? NomeDaConta(int contaId) => _views.FirstOrDefault(v => v.RuntimeId == contaId)?.Config.Name;

    public IReadOnlyList<int> ContasDoPerfilSelecionado() => ViewsOf(Profile).Select(v => v.RuntimeId).ToList();

    public IReadOnlyList<int> TodasAsContasDoJogo(string host) =>
        _views.Where(v => Uri.TryCreate(v.Profile.Url, UriKind.Absolute, out var uri) && ModuleRouter.HostMatches(uri.Host, host))
              .Select(v => v.RuntimeId).ToList();

    public IdleDeck.Core.Market.SniperAccountConfig SniperDaConta(int contaId)
    {
        var view = _views.FirstOrDefault(v => v.RuntimeId == contaId);
        if (view is null) return new IdleDeck.Core.Market.SniperAccountConfig();
        var key = view.Config.ProfileName;
        if (!view.Profile.Sniper.TryGetValue(key, out var config))
            view.Profile.Sniper[key] = config = new IdleDeck.Core.Market.SniperAccountConfig();
        return config;
    }

    public void SalvarConfiguracao() => SaveSettings();

    public void Notificar(string titulo, string texto) => _tray.Notify(titulo, texto);

    public async Task<string?> ExecutarNaContaAsync(int contaId, string script)
    {
        if (_views.FirstOrDefault(v => v.RuntimeId == contaId)?.Core is not { } core) return null;
        try { return await core.ExecuteScriptAsync(script); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { return null; }
    }

    /// <summary>Um botão por módulo dentro do card do perfil (só nos perfis cujo site casa com o módulo).</summary>
    private void BuildModuleButtons()
    {
        _moduleButtons.Clear();
        foreach (var card in _cards)
        {
            card.Modules.Clear();
            if (!Uri.TryCreate(card.Profile.Url, UriKind.Absolute, out var uri)) continue;
            foreach (var module in _modules.Modules.Where(m => ModuleRouter.UrlMatches(uri, m)))
            {
                var botao = new ModuleButton(module, card.Profile);
                card.Modules.Add(botao);
                _moduleButtons.Add(botao);
            }
        }
        RefreshSummaries();
    }

    /// <summary>
    /// Resumo embaixo do botão do módulo — só no perfil selecionado, que é o perfil de onde o
    /// módulo lê as contas.
    /// </summary>
    private void RefreshSummaries()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RefreshSummaries);
            return;
        }
        foreach (var botao in _moduleButtons)
            botao.SetSummary(botao.Profile == Profile ? botao.Module.Summary : null);
    }

    /// <summary>Clique no botão do módulo dentro do card: seleciona aquele perfil e abre a janela.</summary>
    private void Module_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ModuleButton botao }) return;
        if (!ReferenceEquals(botao.Profile, Profile)) SelectProfile(botao.Profile);
        AbrirModulo(botao.Module);
        e.Handled = true;
    }

    /// <summary>Abre (ou traz para frente) a janela do módulo. Não é modal: o jogo continua acessível.</summary>
    private void AbrirModulo(IModuleView module)
    {
        if (_janelasDeModulo.TryGetValue(module.Id, out var aberta))
        {
            if (aberta.WindowState == WindowState.Minimized) aberta.WindowState = WindowState.Normal;
            aberta.Activate();
            return;
        }

        FrameworkElement conteudo;
        try { conteudo = _modules.PanelFor(module); }
        catch (Exception e)
        {
            App.Log(e);
            ArcadeDialog.Show(this, Loc.T("Módulo {0}", module.Title), Loc.T("Não foi possível abrir: {0}", e.Message),
                ArcadeDialogButtons.Ok, ArcadeDialogKind.Error);
            return;
        }

        var janela = new ModuleWindow(module.Title, conteudo, module.Description)
        {
            Owner = this,
        };
        janela.Closed += (_, _) => _janelasDeModulo.Remove(module.Id);
        _janelasDeModulo[module.Id] = janela;
        janela.Show();
    }
}
