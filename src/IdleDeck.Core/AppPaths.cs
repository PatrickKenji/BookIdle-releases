namespace IdleDeck.Core;

public static class AppPaths
{
    /// <summary>
    /// Pasta de dados. A variável <c>BOOKIDLE_HOME</c> (só para testes e desenvolvimento) aponta para outra pasta,
    /// para abrir uma instância isolada sem tocar nos logins e no cofre reais.
    /// </summary>
    public static string Root { get; } = ResolveRoot(
        Environment.GetEnvironmentVariable(HomeVariable),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdleDeckPessoal"));

    public const string HomeVariable = "BOOKIDLE_HOME";

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string VaultFile => Path.Combine(Root, "vault.dat");
    public static string WebViewData => Path.Combine(Root, "WebView2");

    public static string ResolveRoot(string? overrideDir, string defaultRoot) =>
        string.IsNullOrWhiteSpace(overrideDir) ? defaultRoot : overrideDir;
}
