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

    /// <summary>Contas, nomes ou o perfil selecionado mudaram: o painel deve se redesenhar.</summary>
    event EventHandler? ContasMudaram;
}
