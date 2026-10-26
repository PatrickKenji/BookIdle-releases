using System.IO;
using System.Reflection;
using IdleDeck.Core.Updates;
using Velopack;
using Velopack.Sources;

namespace IdleDeck.App.Updates;

/// <summary>A versão deste BookIdle (a que o script de lançamento gravou no executável).</summary>
public static class AppVersion
{
    public static string Current { get; } = Read();

    private static string Read()
    {
        var text = typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = text.IndexOf('+'); // "1.0.1+abc123" → "1.0.1"
        return plus > 0 ? text[..plus] : text;
    }
}

/// <summary>
/// Atualização automática (Velopack): olha as "Releases" do repositório do GitHub, baixa só o que mudou e reinicia o app.
/// Só funciona no app INSTALADO pelo Setup.exe; rodando do Visual Studio / pasta de compilação ela fica inativa.
/// </summary>
public sealed class AppUpdater
{
    /// <summary>
    /// O repositório (público) onde os instaladores são publicados, ex.: <c>https://github.com/seu-usuario/BookIdle-releases</c>.
    /// Vazio = atualização desligada (o app funciona igual, só não procura versão nova).
    /// </summary>
    public const string RepoUrl = "https://github.com/PatrickKenji/BookIdle-releases";

    private readonly UpdateManager? _manager;

    /// <summary>Variável de ambiente que troca a origem das atualizações (pasta ou endereço) — para testar sem publicar.</summary>
    public const string SourceVariable = "BOOKIDLE_UPDATE_SOURCE";

    public AppUpdater()
    {
        var test = Environment.GetEnvironmentVariable(SourceVariable);
        if (!string.IsNullOrWhiteSpace(test)) _manager = new UpdateManager(test);
        else if (RepoUrl.Length > 0) _manager = new UpdateManager(new GithubSource(RepoUrl, null, false));
    }

    /// <summary>O endereço das atualizações já foi definido no código.</summary>
    public bool Configured => _manager is not null;

    /// <summary>Este programa foi instalado pelo instalador (só assim ele consegue se atualizar).</summary>
    public bool Installed => _manager?.IsInstalled == true;

    /// <summary>A versão nova, ou null se já está na mais recente.</summary>
    public async Task<UpdateInfo?> CheckAsync() =>
        _manager is null ? null : await _manager.CheckForUpdatesAsync();

    public Task DownloadAsync(UpdateInfo update, Action<int>? progress = null) =>
        _manager is null ? Task.CompletedTask : _manager.DownloadUpdatesAsync(update, progress);

    /// <summary>Fecha o app, aplica a versão baixada e abre de novo.</summary>
    public void ApplyAndRestart(UpdateInfo update) => _manager?.ApplyUpdatesAndRestart(update);

    public static string VersionOf(UpdateInfo update) => update.TargetFullRelease.Version.ToString();

    /// <summary>Notas da versão nova, publicadas junto com ela (<c>vpk pack --releaseNotes</c>).</summary>
    public static ReleaseNotes? NotesOf(UpdateInfo update) => ReleaseNotes.Parse(update.TargetFullRelease.NotesMarkdown, Loc.Code);

    /// <summary>Notas desta versão, embutidas no executável (<c>release-notes/X.Y.Z.md</c>).</summary>
    public static ReleaseNotes? CurrentNotes()
    {
        using var stream = typeof(AppUpdater).Assembly.GetManifestResourceStream($"ReleaseNotes.{AppVersion.Current}.md");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return ReleaseNotes.Parse(reader.ReadToEnd(), Loc.Code);
    }
}
