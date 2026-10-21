namespace IdleDeck.Core;

public static class AppPaths
{
    /// <summary>
    /// Pasta de dados. A variável <c>BOOKIDLE_HOME</c> (só para testes e desenvolvimento) aponta para outra pasta,
    /// para abrir uma instância isolada sem tocar nos logins e no cofre reais.
    /// </summary>
    public static string Root { get; } = ResolveRoot(Environment.GetEnvironmentVariable(HomeVariable), DefaultRoot);

    /// <summary>
    /// Pasta de dados padrão. Não pode ser <c>%LOCALAPPDATA%\BookIdle</c>: essa é a pasta do instalador, que o desinstalador apaga inteira.
    /// A pasta da versão antiga (IdleDeckPessoal) é movida para cá na primeira abertura.
    /// </summary>
    private static string DefaultRoot
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return MigrateLegacy(Path.Combine(local, "IdleDeckPessoal"), Path.Combine(local, "BookIdleData"));
        }
    }

    public const string HomeVariable = "BOOKIDLE_HOME";

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string VaultFile => Path.Combine(Root, "vault.dat");
    public static string WebViewData => Path.Combine(Root, "WebView2");

    /// <summary>
    /// Devolve a pasta a usar. Se só existe a antiga, move para a nova (tudo ou nada: o Windows recusa o movimento se algo ali
    /// estiver aberto, e então continuamos na antiga para não perder nada). Nunca sobrescreve uma pasta nova que já exista.
    /// </summary>
    public static string MigrateLegacy(string legacyDir, string currentDir)
    {
        if (Directory.Exists(currentDir) || !Directory.Exists(legacyDir)) return currentDir;
        try
        {
            Directory.Move(legacyDir, currentDir);
            return currentDir;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return legacyDir;
        }
    }

    public static string ResolveRoot(string? overrideDir, string defaultRoot) =>
        string.IsNullOrWhiteSpace(overrideDir) ? defaultRoot : overrideDir;
}
