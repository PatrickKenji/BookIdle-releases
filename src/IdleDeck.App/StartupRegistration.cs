using Microsoft.Win32;

namespace IdleDeck.App;

/// <summary>Liga/desliga o início com o Windows (HKCU\...\Run), abrindo direto na bandeja.</summary>
public static class StartupRegistration
{
    public const string TrayArgument = "--tray";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BookIdle";
    // Nome da versão antiga: removido ao ligar/desligar, para não ficar com duas entradas.
    private const string LegacyValueName = "IdleDeckPessoal";

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {TrayArgument}");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
