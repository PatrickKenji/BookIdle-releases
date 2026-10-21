using System.IO;
using IdleDeck.Core;

namespace IdleDeck.App.Market;

/// <summary>Registro do diagnóstico do mercado (<c>market-probe.log</c> na pasta de dados). Só recebe a forma do tráfego, já mascarada.</summary>
public static class MarketProbeLog
{
    public const string Prefix = "probe:";
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.Root, "market-probe.log");

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
