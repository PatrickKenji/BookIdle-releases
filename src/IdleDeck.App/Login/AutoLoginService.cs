using System.IO;
using System.Text.Json;
using IdleDeck.App.Browser;
using IdleDeck.Core.Modules;
using IdleDeck.Core.Settings;
using IdleDeck.Core.Vault;
using Microsoft.Web.WebView2.Core;

namespace IdleDeck.App.Login;

/// <summary>
/// Login automático. Senha: preenche e envia com a credencial do cofre. Google: clica em "Entrar com Google"
/// e escolhe a conta pelo e-mail; se o Google pedir senha/2FA ou bloquear, marca "Precisa de você".
/// Para depois de 3 falhas seguidas (conta fica com "Erro de login").
/// </summary>
public sealed class AutoLoginService
{
    private const string GoogleHost = "accounts.google.com";
    private static readonly TimeSpan PickCooldown = TimeSpan.FromSeconds(8);
    private const int MaxPicks = 3;

    private readonly LoginAttemptTracker _tracker = new();
    private readonly Dictionary<int, DateTime> _lastPick = [];
    private readonly Dictionary<int, int> _picks = [];
    private readonly HashSet<int> _openPopups = [];
    private Dictionary<string, Credential>? _credentials;

    public static string DetectScript { get; } = LoadScript();

    public bool IsUnlocked => _credentials is not null;

    public void SetCredentials(Dictionary<string, Credential>? credentials) => _credentials = credentials;

    /// <summary>Popup de login aberto para a conta: enquanto existir, a tela de login do jogo não dispara nada.</summary>
    public void SetPopupOpen(AccountView view, bool open)
    {
        if (open) _openPopups.Add(view.RuntimeId);
        else _openPopups.Remove(view.RuntimeId);
    }

    /// <summary>Lê uma mensagem <c>{"app":"login","state":"..."}</c>; devolve o estado ou null.</summary>
    public static string? TryParseState(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object
                   && root.TryGetProperty("app", out var app) && app.ValueEquals("login")
                   && root.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String
                ? state.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <param name="source">A página que mandou a mensagem: a própria conta ou um popup dela.</param>
    public async Task HandleAsync(AccountView view, CoreWebView2 source, string state)
    {
        var id = view.RuntimeId;
        var now = DateTime.UtcNow;
        var google = state.StartsWith("google-", StringComparison.Ordinal);

        // estados do Google só valem vindos do Google; os outros, só do site do jogo
        if (google ? !IsOnHost(source, GoogleHost) : !IsOnAccountSite(view, source)) return;

        var userBusy = view.Status == AccountStatus.NeedsUser;
        if (state == "form")
        {
            if (_openPopups.Contains(id)) return; // o login está acontecendo no popup
            if (!userBusy) _tracker.RecordLoginFormSeen(id, now);
        }
        Credential? credential = null;
        var hasCredential = _credentials?.TryGetValue(view.Config.ProfileName, out credential) == true;
        var canAttempt = google
            ? !_lastPick.TryGetValue(id, out var picked) || now - picked >= PickCooldown
            : _tracker.CanAttempt(id, now);

        var action = LoginDecider.Decide(view.Config.Login, state, hasCredential, _tracker.IsLockedOut(id), canAttempt, userBusy);
        switch (action)
        {
            case LoginAction.LoggedIn:
                _tracker.RecordLoggedIn(id);
                _picks.Remove(id);
                if (view.Status is AccountStatus.LoginError or AccountStatus.NeedsUser) view.Status = AccountStatus.Online;
                break;
            case LoginAction.LockedOut:
                view.Status = AccountStatus.LoginError;
                break;
            case LoginAction.NeedsUser:
                view.Status = AccountStatus.NeedsUser;
                break;
            case LoginAction.FillPassword:
                if (await RunAsync(source, FillScript(credential!, submit: true))) _tracker.RecordAttempt(id, now);
                break;
            case LoginAction.ClickGoogle:
                if (await RunAsync(source, "window.__idledeckClickGoogle && window.__idledeckClickGoogle()"))
                    _tracker.RecordAttempt(id, now);
                break;
            case LoginAction.PickGoogleAccount:
                _lastPick[id] = now;
                _picks[id] = _picks.GetValueOrDefault(id) + 1;
                if (_picks[id] > MaxPicks)
                {
                    view.Status = AccountStatus.NeedsUser; // o seletor volta sempre: algo pede atenção
                    break;
                }
                var script = $"window.__idledeckPickGoogleAccount && window.__idledeckPickGoogleAccount({JsonSerializer.Serialize(credential!.Username)})";
                if (!await RunAsync(source, script)) view.Status = AccountStatus.NeedsUser; // e-mail não está no seletor
                break;
        }
    }

    /// <summary>Preenche (e opcionalmente envia) o login na página da conta, só se ela estiver no site do jogo.</summary>
    public async Task<bool> FillAsync(AccountView view, bool submit)
    {
        if (view.Core is not { } core || !IsOnAccountSite(view, core)) return false;
        if (_credentials is null || !_credentials.TryGetValue(view.Config.ProfileName, out var credential)) return false;

        if (view.Config.Login == LoginMethod.Google)
            return submit && await RunAsync(core, "window.__idledeckClickGoogle && window.__idledeckClickGoogle()");
        return await RunAsync(core, FillScript(credential, submit));
    }

    /// <summary>Libera uma conta bloqueada por falhas (ou que precisava de você) para tentar de novo.</summary>
    public void ClearLockout(AccountView view)
    {
        _tracker.Clear(view.RuntimeId);
        _lastPick.Remove(view.RuntimeId);
        _picks.Remove(view.RuntimeId);
        if (view.Status is AccountStatus.LoginError or AccountStatus.NeedsUser) view.Status = AccountStatus.Online;
    }

    private static string FillScript(Credential credential, bool submit) =>
        $"window.__idledeckFillLogin && window.__idledeckFillLogin(" +
        $"{JsonSerializer.Serialize(credential.Username)}, {JsonSerializer.Serialize(credential.Password)}, " +
        $"{(submit ? "true" : "false")})";

    private static async Task<bool> RunAsync(CoreWebView2 core, string script)
    {
        try { return await core.ExecuteScriptAsync(script) == "true"; }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { return false; }
    }

    private static bool IsOnHost(CoreWebView2 core, string host) =>
        Uri.TryCreate(core.Source, UriKind.Absolute, out var current)
        && current.Scheme == Uri.UriSchemeHttps
        && current.Host.Equals(host, StringComparison.OrdinalIgnoreCase);

    private static bool IsOnAccountSite(AccountView view, CoreWebView2 core) =>
        Uri.TryCreate(core.Source, UriKind.Absolute, out var current)
        && Uri.TryCreate(view.Url, UriKind.Absolute, out var configured)
        && current.Scheme == configured.Scheme
        && (ModuleRouter.HostMatches(current.Host, configured.Host)
            || ModuleRouter.HostMatches(configured.Host, current.Host));

    private static string LoadScript()
    {
        using var stream = typeof(AutoLoginService).Assembly.GetManifestResourceStream("Scripts.login-detect.js")
                           ?? throw new InvalidOperationException("Script de login não embutido.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
