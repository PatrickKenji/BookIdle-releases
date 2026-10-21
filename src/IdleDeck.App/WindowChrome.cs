using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IdleDeck.App;

/// <summary>Barra de título escura e roxa (Windows 11), para combinar com o tema Arcade.</summary>
public static class WindowChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionColor = 35;
    private static int _captionColor = 0x48172A; // #2A1748 em COLORREF (0x00BBGGRR)

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Liga em todas as janelas do app, assim que cada uma ganha um HWND.</summary>
    public static void RegisterForAllWindows() =>
        EventManager.RegisterClassHandler(typeof(Window), Window.LoadedEvent, new RoutedEventHandler((s, _) => ApplyDark((Window)s)));

    /// <summary>Cor da barra de título (#RRGGBB) de todas as janelas abertas e das próximas.</summary>
    public static void SetCaptionColor(string hex)
    {
        var rgb = Convert.ToInt32(hex.TrimStart('#'), 16);
        _captionColor = ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF); // RGB → COLORREF (BGR)
        foreach (Window window in Application.Current.Windows) ApplyDark(window);
    }

    public static void ApplyDark(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var on = 1;
        var color = _captionColor;
        // Falhas são ignoradas: em Windows mais antigos só fica a barra padrão.
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
        DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref color, sizeof(int));
    }
}
