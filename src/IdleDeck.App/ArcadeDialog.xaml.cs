using System.Windows;

namespace IdleDeck.App;

public enum ArcadeDialogButtons { Ok, YesNo }

public enum ArcadeDialogKind { Info, Warning, Error }

/// <summary>Caixa de aviso/confirmação no estilo Arcade (substitui o MessageBox do Windows).</summary>
public partial class ArcadeDialog : Window
{
    private ArcadeDialog() => InitializeComponent();

    /// <summary>Mostra e devolve true para OK/Sim.</summary>
    public static bool Show(Window? owner, string title, string message,
        ArcadeDialogButtons buttons = ArcadeDialogButtons.Ok, ArcadeDialogKind kind = ArcadeDialogKind.Info)
    {
        var dialog = new ArcadeDialog { Title = title };
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
            dialog.YesButton.Content = "Sim";
            dialog.NoButton.Visibility = Visibility.Visible;
        }

        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }

    private void Yes_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void No_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
