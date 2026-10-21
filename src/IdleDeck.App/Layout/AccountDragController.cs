using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using IdleDeck.App.Browser;
using IdleDeck.Core.Layout;

namespace IdleDeck.App.Layout;

/// <summary>
/// Reordenar contas arrastando a barrinha de título (Grade, 1+N e Livre). Durante o arrasto cada conta do perfil
/// troca o WebView2 por uma foto dele (o controle nativo ficaria por cima de tudo que o WPF desenha), a
/// célula sob o mouse é destacada e, ao soltar, o app aplica a troca. No modo Livre a janela alvo ainda se
/// divide em zonas (esquerda, direita, acima, abaixo ou trocar) e a zona escolhida aparece destacada.
/// Esc, soltar fora de qualquer conta ou perder o mouse cancela: nada muda e o jogo volta como estava.
/// </summary>
public sealed class AccountDragController
{
    private const double StartDistance = 6;

    private enum State { Idle, Pressed, Starting, Dragging }

    private readonly Grid _area;
    private readonly Func<IReadOnlyList<AccountView>> _views;
    private readonly Func<bool> _zoned;
    private readonly Action<AccountView, AccountView, DropZone> _drop;
    private readonly Border _indicator;

    private State _state = State.Idle;
    private AccountView? _source;
    private AccountView? _target;
    private DropZone _zone = DropZone.Swap;
    private Point _pressPoint;
    private List<AccountView> _covered = [];

    /// <param name="area">Onde as contas ficam (o AccountsGrid): os retângulos das células saem dele.</param>
    /// <param name="views">Contas visíveis do perfil selecionado, na ordem do perfil.</param>
    /// <param name="zoned">Verdadeiro no modo Livre: a janela alvo se divide em zonas de soltura.</param>
    /// <param name="drop">Chamado ao soltar uma conta sobre outra: (arrastada, alvo, zona).</param>
    public AccountDragController(Grid area, Func<IReadOnlyList<AccountView>> views, Func<bool> zoned,
        Action<AccountView, AccountView, DropZone> drop)
    {
        _area = area;
        _views = views;
        _zoned = zoned;
        _drop = drop;
        _indicator = new Border
        {
            BorderThickness = new Thickness(3),
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Opacity = 0.45,
        };
        _indicator.SetResourceReference(Border.BackgroundProperty, "Yellow");
        _indicator.SetResourceReference(Border.BorderBrushProperty, "Yellow");
        Panel.SetZIndex(_indicator, 1000);
    }

    public bool IsDragging => _state is State.Starting or State.Dragging;

    public void Attach(AccountView view)
    {
        var header = view.HeaderElement;
        header.PreviewMouseLeftButtonDown += (_, e) => OnPress(view, header, e);
        header.MouseMove += (_, e) => OnMove(view, e);
        header.MouseLeftButtonUp += (_, e) => OnRelease(header, e);
        header.LostMouseCapture += (_, _) => { if (_state != State.Idle) Cancel(); };
    }

    /// <summary>Esc (ou qualquer motivo): desfaz o arrasto sem trocar nada.</summary>
    public void Cancel()
    {
        var wasActive = _state != State.Idle;
        _state = State.Idle;
        if (!wasActive) return;
        Mouse.Capture(null);
        EndDragMode();
    }

    private void OnPress(AccountView view, UIElement header, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || InsideButton(e.OriginalSource)) return;
        _state = State.Pressed;
        _source = view;
        _target = null;
        _pressPoint = e.GetPosition(_area);
        header.CaptureMouse();
    }

    private async void OnMove(AccountView view, MouseEventArgs e)
    {
        if (view != _source) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            Cancel();
            return;
        }
        var point = e.GetPosition(_area);
        if (_state == State.Pressed && (point - _pressPoint).Length >= StartDistance)
        {
            await StartAsync();
            return;
        }
        if (_state == State.Dragging) UpdateTarget(point);
    }

    private async Task StartAsync()
    {
        _state = State.Starting;
        _covered = _views().ToList();
        try
        {
            await Task.WhenAll(_covered.Select(v => v.EnterDragModeAsync()));
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        if (_state != State.Starting) // soltou o mouse (ou cancelou) enquanto as fotos eram tiradas
        {
            EndDragMode();
            return;
        }
        _state = State.Dragging;
        if (_source is not null) _source.IsDragSource = true;
        if (_zoned()) _area.Children.Add(_indicator);
        UpdateTarget(Mouse.GetPosition(_area));
    }

    private void UpdateTarget(Point point)
    {
        var target = _covered.FirstOrDefault(v => v != _source && Bounds(v).Contains(point));
        if (target != _target)
        {
            if (_target is not null) _target.IsDropTarget = false;
            _target = target;
            if (_target is not null) _target.IsDropTarget = true;
        }

        _zone = DropZone.Swap;
        if (_target is null || !_zoned())
        {
            _indicator.Visibility = Visibility.Collapsed;
            return;
        }
        var rect = Bounds(_target);
        _zone = FreeLayoutEngine.ZoneAt(new FreeRect(rect.X, rect.Y, rect.Width, rect.Height), point.X, point.Y);
        ShowIndicator(rect, _zone);
    }

    /// <summary>Metade da janela alvo do lado escolhido (ou ela inteira, para trocar de lugar).</summary>
    private void ShowIndicator(Rect cell, DropZone zone)
    {
        var area = zone switch
        {
            DropZone.Left => new Rect(cell.X, cell.Y, cell.Width / 2, cell.Height),
            DropZone.Right => new Rect(cell.X + cell.Width / 2, cell.Y, cell.Width / 2, cell.Height),
            DropZone.Above => new Rect(cell.X, cell.Y, cell.Width, cell.Height / 2),
            DropZone.Below => new Rect(cell.X, cell.Y + cell.Height / 2, cell.Width, cell.Height / 2),
            _ => cell,
        };
        _indicator.Margin = new Thickness(area.X, area.Y, 0, 0);
        _indicator.Width = area.Width;
        _indicator.Height = area.Height;
        _indicator.Visibility = Visibility.Visible;
    }

    private void OnRelease(UIElement header, MouseButtonEventArgs e)
    {
        var state = _state;
        if (state == State.Idle) return;
        var source = _source;
        var target = _target;
        var zone = _zone;
        _state = State.Idle; // antes de soltar o mouse: o LostMouseCapture não pode cancelar a troca
        header.ReleaseMouseCapture();
        if (state == State.Pressed) return; // foi só um clique: o cabeçalho trata
        e.Handled = true; // foi um arrasto: não conta como "dar destaque"
        if (state == State.Dragging) EndDragMode();
        // (se ainda estava tirando as fotos, StartAsync desfaz sozinho ao terminar)
        if (state == State.Dragging && source is not null && target is not null) _drop(source, target, zone);
    }

    private void EndDragMode()
    {
        _area.Children.Remove(_indicator);
        foreach (var view in _covered)
        {
            view.IsDropTarget = false;
            view.IsDragSource = false;
            view.ExitDragMode();
        }
        _covered = [];
        _source = null;
        _target = null;
        _zone = DropZone.Swap;
    }

    private Rect Bounds(AccountView view) =>
        view.TransformToAncestor(_area).TransformBounds(new Rect(view.RenderSize));

    private static bool InsideButton(object? source)
    {
        for (var node = source as DependencyObject; node is not null;)
        {
            if (node is ButtonBase) return true;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }
}
