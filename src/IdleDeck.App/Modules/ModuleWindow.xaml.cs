using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace IdleDeck.App.Modules;

/// <summary>
/// Janela própria de um módulo: recebe o painel que o módulo desenha e o hospeda com espaço largo
/// (a lateral de 310 px não serve para tabelas). Não é modal: o jogo continua acessível atrás.
/// Fechar devolve o painel ao módulo, para a próxima abertura reusar o mesmo estado.
/// </summary>
public partial class ModuleWindow : Window
{
    private readonly FrameworkElement _conteudo;

    public ModuleWindow(string titulo, FrameworkElement conteudo, string? subtitulo = null)
    {
        _conteudo = conteudo;
        InitializeComponent();
        Title = titulo;
        TitleText.Text = titulo.TrimStart('/', ' ').ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(subtitulo))
        {
            SubtitleText.Text = subtitulo;
            SubtitleText.Visibility = Visibility.Visible;
        }

        // O painel pode estar em outra janela/painel (reabertura): tira de lá antes de adotar.
        if (conteudo.Parent is Panel antigo) antigo.Children.Remove(conteudo);
        Body.Child = conteudo;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
        Closed += (_, _) => Body.Child = null;
    }

    /// <summary>O painel deste módulo (para os testes/checagens de quem abriu).</summary>
    public FrameworkElement Conteudo => _conteudo;

    /// <summary>Sem barra do Windows, o cabeçalho faz o papel dela: arrasta a janela e maximiza no duplo clique.</summary>
    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DentroDeBotao(e.OriginalSource)) return; // o ✕ continua sendo um clique, não um arrasto
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        try { DragMove(); }
        catch (InvalidOperationException) { } // botão solto no meio do arrasto: ignora
    }

    private static bool DentroDeBotao(object? origem)
    {
        for (var atual = origem as DependencyObject; atual is not null;)
            if (atual is ButtonBase) return true;
            else atual = atual is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(atual)
                : LogicalTreeHelper.GetParent(atual);
        return false;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Minimiza só esta janela; a janela principal e as outras ficam como estão.</summary>
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
}
