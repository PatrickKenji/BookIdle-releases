namespace IdleDeck.App.Modules;

/// <summary>O que um módulo pode pedir ao app sobre as contas (pelo id de execução delas).</summary>
public interface IModuleHostServices
{
    /// <summary>Nome da conta, ou null se ela não existe mais.</summary>
    string? NomeDaConta(int contaId);

    /// <summary>Contas do perfil de jogo selecionado, na ordem da lateral.</summary>
    IReadOnlyList<int> ContasDoPerfilSelecionado();

    /// <summary>Roda um script na página da conta e devolve o resultado em JSON (null se não deu).</summary>
    Task<string?> ExecutarNaContaAsync(int contaId, string script);

    /// <summary>Todas as contas (de qualquer perfil) cujo site é <paramref name="host"/>, na ordem dos perfis.</summary>
    IReadOnlyList<int> TodasAsContasDoJogo(string host);

    /// <summary>Configuração do sniper da conta (criada vazia na primeira vez e guardada no perfil dela).</summary>
    IdleDeck.Core.Market.SniperAccountConfig SniperDaConta(int contaId);

    /// <summary>Grava as configurações em disco (o módulo mexeu nas regras).</summary>
    void SalvarConfiguracao();

    /// <summary>Aviso na bandeja do Windows.</summary>
    void Notificar(string titulo, string texto);

    /// <summary>Listas de canais da Twitch e da Kick do perfil selecionado (ordem = prioridade).</summary>
    (IReadOnlyList<string> Twitch, IReadOnlyList<string> Kick) CanaisDasLives();

    /// <summary>Contas do perfil selecionado, com o login da Twitch/Kick (null = não conectada) e o que assistem agora.</summary>
    IReadOnlyList<LiveAccountInfo> ContasDasLives();

    /// <summary>A chave da Twitch da conta (do navegador dela), ou null.</summary>
    Task<string?> ChaveTwitchAsync(string profileName);

    /// <summary>A sessão da Kick da conta (do navegador dela), ou null.</summary>
    Task<string?> ChaveKickAsync(string profileName);

    /// <summary>Contas, nomes ou o perfil selecionado mudaram: o painel deve se redesenhar.</summary>
    event EventHandler? ContasMudaram;
}

/// <summary>Uma conta para a janela de lives.</summary>
public sealed record LiveAccountInfo(string Name, string ProfileName, string? TwitchLogin, string? KickLogin,
    IReadOnlyList<string> TwitchWatching, IReadOnlyList<string> KickWatching);
