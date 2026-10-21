using System.Text.Json;

namespace IdleDeck.Core.Modules;

/// <summary>
/// Uma função extra do app. O script (se houver) é injetado nas contas cujo host casa com
/// <see cref="UrlPattern"/> e conversa com o módulo via
/// <c>window.chrome.webview.postMessage(JSON.stringify({ module: Id, data }))</c>.
/// </summary>
public interface IModule
{
    string Id { get; }
    string Title { get; }
    string UrlPattern { get; }

    /// <summary>
    /// Caminhos do site em que o módulo vale (ex.: <c>["/", "/app"]</c>). Lista vazia = qualquer caminho.
    /// </summary>
    IReadOnlyList<string> AllowedPaths => Array.Empty<string>();

    string? InjectedScript { get; }
    void OnMessage(int accountIndex, JsonElement data);
}
