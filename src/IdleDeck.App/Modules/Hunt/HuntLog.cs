using System.IO;
using IdleDeck.Core;

namespace IdleDeck.App.Modules.Hunt;

/// <summary>Registro do /hunt (<c>hunt.log</c> na pasta de dados): marcos e erros da página, nunca dados da conta.</summary>
public static class HuntLog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.Root, "hunt.log");

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
