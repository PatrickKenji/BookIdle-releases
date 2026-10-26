using System.Windows;

namespace IdleDeck.App;

public enum ArcadeDialogButtons { Ok, YesNo }

public enum ArcadeDialogKind { Info, Warning, Error }

/// <summary>Caixa de aviso/confirmação no estilo Arcade (substitui o MessageBox do Windows).</summary>
public partial class ArcadeDialog : Window
{
    private ArcadeDialog() => InitializeComponent();

    /// <summary>Quanto tempo os botões de um aviso protegido ficam sem responder.</summary>
    public static readonly TimeSpan GuardDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Mostra e devolve true para OK/Sim. <paramref name="wide"/>: caixa larga para textos longos (rola se precisar).
    /// <paramref name="guarded"/>: para avisos que aparecem sozinhos no meio do jogo; Enter não confirma e os botões
    /// só respondem depois de um instante, para uma tecla que a pessoa já estava apertando não decidir por ela.
    /// </summary>
    public static bool Show(Window? owner, string title, string message,
        ArcadeDialogButtons buttons = ArcadeDialogButtons.Ok, ArcadeDialogKind kind = ArcadeDialogKind.Info,
        bool wide = false, (string Yes, string No)? buttonsText = null, bool guarded = false)
    {
        var dialog = new ArcadeDialog { Title = title };
        if (wide) dialog.Width = 600;
        if (guarded)
        {
            dialog.YesButton.IsDefault = false;
            dialog.YesButton.IsEnabled = dialog.NoButton.IsEnabled = false;
            var unlock = new System.Windows.Threading.DispatcherTimer { Interval = GuardDelay };
            unlock.Tick += (_, _) =>
            {
                unlock.Stop();
                dialog.YesButton.IsEnabled = dialog.NoButton.IsEnabled = true;
            };
            dialog.Loaded += (_, _) => unlock.Start();
        }
        dialog.TitleText.Text = title.ToUpperInvariant();
        dialog.MessageText.Text = message;
        dialog.KindIcon.Text = kind switch
        {
            ArcadeDialogKind.Warning => "⚠",
            ArcadeDialogKind.Error => "✖",
            _ => "★",
        };
        dialog.KindIcon.Foreground = (System.Windows.Media.Brush)dialog.FindResource(kind == ArcadeDialogKind.Error ? "Err" : "Yellow");
        if (buttons == ArcadeDialogButtons.YesNo)
        {
            dialog.YesButton.Content = Loc.T("Sim");
            dialog.NoButton.Visibility = Visibility.Visible;
        }
        if (buttonsText is { } text)
        {
            dialog.YesButton.Content = text.Yes;
            dialog.NoButton.Content = text.No;
        }

        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }

    private void Yes_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void No_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
