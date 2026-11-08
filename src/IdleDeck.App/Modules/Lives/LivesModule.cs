using System.Net.Http;
using System.Text.Json;
using System.Windows;
using IdleDeck.Core.Kick;
using IdleDeck.Core.Lives;
using IdleDeck.Core.Twitch;

namespace IdleDeck.App.Modules.Lives;

/// <summary>
/// Janela "Lives" (só visual): todos os canais da Twitch e da Kick do perfil, quem está ao vivo, o saldo de pontos de
/// cada conta em cada canal e quais lives cada conta está assistindo agora. Só lê (a mesma API que os sites usam);
/// não mexe nas lives nem abre páginas.
/// </summary>
public sealed class LivesModule(IModuleHostServices host) : IModuleView
{
    private LivesPanel? _panel;

    public string Id => "lives";
    public string Title => Loc.T("Pontos das Lives");
    public string UrlPattern => "pokeidle.io";
    public IReadOnlyList<string> AllowedPaths => ["/", "/app"];
    public string? InjectedScript => null;
    public string? Description => Loc.T("Canais da Twitch e da Kick do perfil: quem está ao vivo, quantos pontos cada conta tem em cada canal e o que ela está assistindo (▶).");
    public string? Summary => null;

#pragma warning disable CS0067 // sem resumo na lateral
    public event EventHandler? SummaryChanged;
#pragma warning restore CS0067

    public FrameworkElement CreatePanel() => _panel = new LivesPanel(this);

    public void OnMessage(int accountIndex, JsonElement data) { }

    public event EventHandler? ContasMudaram
    {
        add => host.ContasMudaram += value;
        remove => host.ContasMudaram -= value;
    }

    /// <summary>A tabela da Twitch: ao vivo pela API do site; saldos com a chave de cada conta conectada.</summary>
    public async Task<(LiveBoard Board, List<string> Errors)> ReadTwitchAsync()
    {
        var channels = host.CanaisDasLives().Twitch;
        var errors = new List<string>();
        var live = new HashSet<string>();
        var accounts = new List<LiveBoardAccount>();
        if (channels.Count == 0) return (LiveBoard.Build(channels, live, accounts), errors);

        var anyLiveRead = false;
        foreach (var info in host.ContasDasLives().Where(a => a.TwitchLogin is not null))
        {
            var balances = new Dictionary<string, int?>();
            try
            {
                var token = await host.ChaveTwitchAsync(info.ProfileName);
                if (token is null) errors.Add(Loc.T("{0}: sem login da Twitch no navegador (Reconectar Twitch)", info.Name));
                else
                {
                    foreach (var s in await TwitchGql.ReadChannelsAsync(token, channels))
                    {
                        balances[s.Channel] = s.Balance ?? 0;
                        if (s.Live) live.Add(s.Channel);
                    }
                    anyLiveRead = true;
                }
            }
            catch (Exception e) when (e is HttpRequestException or FormatException or TaskCanceledException)
            {
                errors.Add($"{info.Name}: {e.Message}");
            }
            accounts.Add(new LiveBoardAccount(info.TwitchLogin!, balances, info.TwitchWatching.ToHashSet()));
        }
        if (!anyLiveRead)
        {
            try { live = (await TwitchGql.ReadChannelsAsync(null, channels)).Where(s => s.Live).Select(s => s.Channel).ToHashSet(); }
            catch (Exception e) when (e is HttpRequestException or FormatException or TaskCanceledException) { errors.Add("Twitch: " + e.Message); }
        }
        return (LiveBoard.Build(channels, live, accounts), errors);
    }

    /// <summary>A tabela da Kick: ao vivo pela API pública; saldos com a sessão de cada conta conectada.</summary>
    public async Task<(LiveBoard Board, List<string> Errors)> ReadKickAsync()
    {
        var channels = host.CanaisDasLives().Kick;
        var errors = new List<string>();
        var accounts = new List<LiveBoardAccount>();
        if (channels.Count == 0) return (LiveBoard.Build(channels, new HashSet<string>(), accounts), errors);

        var liveErrors = new List<string>();
        var live = await KickApi.ReadLiveAsync(channels, liveErrors);
        if (liveErrors.Count > 0) errors.Add(Loc.T("Kick: não deu para ver se estão ao vivo: {0}", string.Join(", ", liveErrors)));
        foreach (var info in host.ContasDasLives().Where(a => a.KickLogin is not null))
        {
            var balances = new Dictionary<string, int?>();
            var token = await host.ChaveKickAsync(info.ProfileName);
            if (token is null) errors.Add(Loc.T("{0}: sem login da Kick no navegador (Reconectar Kick)", info.Name));
            else
            {
                foreach (var channel in channels)
                {
                    try { balances[channel] = await KickApi.ReadPointsAsync(token, channel); }
                    catch (Exception e) when (e is HttpRequestException or FormatException or TaskCanceledException)
                    {
                        balances[channel] = null;
                        errors.Add($"{info.Name} · {channel}: {e.Message}");
                    }
                }
            }
            accounts.Add(new LiveBoardAccount(info.KickLogin!, balances, info.KickWatching.ToHashSet()));
        }
        return (LiveBoard.Build(channels, live, accounts), errors);
    }
}
