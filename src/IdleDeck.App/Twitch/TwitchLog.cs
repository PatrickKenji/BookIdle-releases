using System.IO;
using IdleDeck.Core;

namespace IdleDeck.App.Twitch;

/// <summary>Registro de diagnóstico da Twitch (<c>twitch.log</c> na pasta de dados). Nunca recebe chaves.</summary>
public static class TwitchLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.Root, "twitch.log");

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
