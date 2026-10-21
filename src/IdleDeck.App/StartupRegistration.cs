using Microsoft.Win32;

namespace IdleDeck.App;

/// <summary>Liga/desliga o início com o Windows (HKCU\...\Run), abrindo direto na bandeja.</summary>
public static class StartupRegistration
{
    public const string TrayArgument = "--tray";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // O nome do valor continua o antigo: assim quem já tinha a opção ligada tem o caminho do .exe sobrescrito
    // (IdleDeckPessoal.exe → BookIdle.exe) em vez de ficar com duas entradas.
    private const string ValueName = "IdleDeckPessoal";

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {TrayArgument}");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
