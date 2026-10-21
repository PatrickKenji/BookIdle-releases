using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using IdleDeck.Core;
using IdleDeck.Core.Settings;
using Velopack;

namespace IdleDeck.App;

public partial class App : Application
{
    /// <summary>Esta é a cópia de TESTE (aberta com dados separados, BOOKIDLE_HOME): a janela e a bandeja avisam.</summary>
    public static bool IsTestCopy => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AppPaths.HomeVariable));

    /// <summary>
    /// O Velopack trata os argumentos de instalação/atualização (--veloapp-*) e sai; no uso normal volta na hora.
    /// Por isso roda antes de qualquer coisa do WPF.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!SingleInstance.TryAcquire())
        {
            Shutdown();
            return;
        }

        WindowChrome.RegisterForAllWindows();
        try
        {
            StartMainWindow(e);
        }
        catch (Exception ex)
        {
            Log(ex);
            ArcadeDialog.Show(null, "Não abriu",
                $"O BookIdle não conseguiu abrir:\n{ex.Message}\n\nDetalhes em {Path.Combine(AppPaths.Root, "erros.log")}",
                kind: ArcadeDialogKind.Error);
            Shutdown();
        }
    }

    private void StartMainWindow(StartupEventArgs e)
    {
        var store = new SettingsStore(AppPaths.SettingsFile);
        var settings = store.Load();
        // O renome mudou o caminho do .exe: regrava a entrada de "iniciar com o Windows", se estiver ligada.
        if (settings.StartWithWindows && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AppPaths.HomeVariable)))
        {
            try { StartupRegistration.Set(true); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { Log(ex); }
        }
        Themes.ThemeManager.Apply(settings.Theme);
        var window = new MainWindow(settings, store)
        {
            StartInTray = e.Args.Contains(StartupRegistration.TrayArgument),
        };
        if (window.StartInTray) window.WindowState = WindowState.Minimized;
        SingleInstance.ListenForShowRequests(() => Dispatcher.BeginInvoke(window.ShowFromTray));
        window.Closed += (_, _) =>
        {
            if (window.RestartRequested)
            {
                SingleInstance.Release();
                Process.Start(Environment.ProcessPath!);
            }
            else if (window.HasPendingUpdate)
            {
                SingleInstance.Release();
                window.ApplyPendingUpdate(); // fecha, troca os arquivos e abre a versão nova
            }
            Shutdown();
        };
        window.Show();
        DispatcherUnhandledException += OnUnhandledException;
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        e.Handled = true;
    }

    public static void Log(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.AppendAllText(Path.Combine(AppPaths.Root, "erros.log"), $"[{DateTime.Now:s}] {exception}\n\n");
        }
        catch (IOException) { }
    }
}
