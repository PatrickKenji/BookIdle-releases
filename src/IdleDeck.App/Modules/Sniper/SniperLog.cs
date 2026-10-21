using System.IO;
using IdleDeck.Core;

namespace IdleDeck.App.Modules.Sniper;

/// <summary>Registro do sniper (<c>sniper.log</c> na pasta de dados): o que viu, pediu e o que o jogo respondeu. Sem chaves.</summary>
public static class SniperLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.Root, "sniper.log");

    public static void Write(string account, string text)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Root);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"[{DateTime.Now:HH:mm:ss}] {account}: {text}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
