using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IdleDeck.App.Browser;
using IdleDeck.Core.Layout;
using IdleDeck.Core.Settings;

namespace IdleDeck.App.Layout;

/// <summary>
/// Modo Livre na tela: posiciona cada conta por margem e tamanho dentro da célula única do AccountsGrid
/// (o WebView2 nunca é reparentado) e desenha alças nos vãos de 6 px entre as janelas para redimensionar.
/// Durante o redimensionamento as contas viram foto, como no arrasto, e o jogo volta ao soltar.
/// Os números (tamanhos, mínimos, trocas) vêm todos do <see cref="FreeLayoutEngine"/>.
/// </summary>
public sealed class FreeLayoutController
{
    private sealed record Handle(Border Element, bool Vertical, int Row, int Boundary);

    private readonly Grid _area;
    private readonly Func<GameProfile> _profile;
    private readonly Action _save;
    private readonly List<Handle> _handles = [];
    private IReadOnlyList<AccountView> _shown = [];
    private bool _active;

    public FreeLayoutController(Grid area, Func<GameProfile> profile, Action save)
    {
        _area = area;
        _profile = profile;
        _save = save;
        area.SizeChanged += (_, _) => { if (_active) Relayout(); };
    }

    /// <summary>
    /// Chamado a cada <c>ApplyLayout</c>. Com <paramref name="active"/>, posiciona as contas mostradas e cria as alças;
    /// sem, devolve todas as contas ao alinhamento padrão (esticadas na célula).
    /// </summary>
    public void Apply(bool active, IReadOnlyList<AccountView> shown, IEnumerable<AccountView> all)
    {
        foreach (var view in all.Where(v => !active || !shown.Contains(v))) Reset(view);
        ClearHandles();
        _active = active && shown.Count > 0;
        _shown = shown;
        if (!_active) return;

        var profile = _profile();
        var layout = FreeLayoutEngine.Reconcile(profile.Free, shown.Select(v => v.Config.ProfileName).ToList());
        var changed = Fingerprint(profile.Free) != Fingerprint(layout);
        profile.Free = layout;
        if (changed) _save();
        BuildHandles(layout);
        Relayout();
    }

    private static void Reset(FrameworkElement view)
    {
        view.HorizontalAlignment = HorizontalAlignment.Stretch;
        view.VerticalAlignment = VerticalAlignment.Stretch;
        view.Width = double.NaN;
        view.Height = double.NaN;
    }

    private void Relayout()
    {
        var layout = _profile().Free;
        if (layout is null || !_active || _area.ActualWidth <= 1 || _area.ActualHeight <= 1) return;
        var rects = FreeLayoutEngine.Compute(layout, _area.ActualWidth, _area.ActualHeight, FreeLayoutEngine.Gap);
        foreach (var view in _shown)
        {
            if (!rects.TryGetValue(view.Config.ProfileName, out var r)) continue;
            view.HorizontalAlignment = HorizontalAlignment.Left;
            view.VerticalAlignment = VerticalAlignment.Top;
            view.Margin = new Thickness(r.X, r.Y, 0, 0);
            view.Width = r.Width;
            view.Height = r.Height;
        }
        PlaceHandles(layout, rects);
    }

    // ---------- Alças de redimensionar ----------

    private void BuildHandles(FreeLayout layout)
    {
        for (var row = 0; row < layout.Rows.Count; row++)
        {
            for (var boundary = 0; boundary + 1 < layout.Rows[row].Cells.Count; boundary++)
                AddHandle(vertical: true, row, boundary);
            if (row + 1 < layout.Rows.Count) AddHandle(vertical: false, row, row);
        }
    }

    private void AddHandle(bool vertical, int row, int boundary)
    {
        var element = new Border
        {
            Background = Brushes.Transparent, // precisa de fundo para receber o mouse
            Cursor = vertical ? Cursors.SizeWE : Cursors.SizeNS,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = vertical ? "Arraste para mudar a largura" : "Arraste para mudar a altura",
        };
        var handle = new Handle(element, vertical, row, boundary);
        element.MouseEnter += (_, _) => element.SetResourceReference(Border.BackgroundProperty, "Yellow");
        element.MouseLeave += (_, _) => { if (!element.IsMouseCaptured) element.Background = Brushes.Transparent; };
        element.MouseLeftButtonDown += async (_, e) => await BeginResizeAsync(handle, e);
        element.MouseMove += (_, e) => ResizeTo(handle, e);
        element.MouseLeftButtonUp += (_, e) => EndResize(handle, e);
        element.LostMouseCapture += (_, _) => { if (_resizing is not null) FinishResize(handle); };
        Panel.SetZIndex(element, 500);
        _handles.Add(handle);
        _area.Children.Add(element);
    }

    private void ClearHandles()
    {
        foreach (var handle in _handles) _area.Children.Remove(handle.Element);
        _handles.Clear();
    }

    private void PlaceHandles(FreeLayout layout, IReadOnlyDictionary<string, FreeRect> rects)
    {
        foreach (var handle in _handles)
        {
            var cells = layout.Rows[handle.Row].Cells;
            var first = rects[cells[0].Account];
            if (handle.Vertical)
            {
                var left = rects[cells[handle.Boundary].Account];
                Place(handle.Element, left.X + left.Width, left.Y, FreeLayoutEngine.Gap, left.Height);
            }
            else
            {
                Place(handle.Element, 0, first.Y + first.Height, _area.ActualWidth, FreeLayoutEngine.Gap);
            }
        }
    }

    private static void Place(FrameworkElement element, double x, double y, double width, double height)
    {
        element.Margin = new Thickness(x, y, 0, 0);
        element.Width = width;
        element.Height = height;
    }

    // ---------- Redimensionar com o mouse ----------

    private Handle? _resizing;
    private bool _covered;

    private async Task BeginResizeAsync(Handle handle, MouseButtonEventArgs e)
    {
        if (_resizing is not null) return;
        _resizing = handle;
        _covered = false;
        handle.Element.CaptureMouse();
        e.Handled = true;
        try
        {
            await Task.WhenAll(_shown.Select(v => v.EnterDragModeAsync()));
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        _covered = true;
        if (_resizing is null) ExitCovers(); // soltou antes de as fotos ficarem prontas
    }

    private void ResizeTo(Handle handle, MouseEventArgs e)
    {
        if (_resizing != handle || !_covered || e.LeftButton != MouseButtonState.Pressed) return;
        var layout = _profile().Free;
        if (layout is null) return;
        var rects = FreeLayoutEngine.Compute(layout, _area.ActualWidth, _area.ActualHeight, FreeLayoutEngine.Gap);
        var point = e.GetPosition(_area);
        var cells = layout.Rows[handle.Row].Cells;
        if (handle.Vertical)
        {
            var left = rects[cells[handle.Boundary].Account];
            // posição do mouse em relação ao centro do vão: corrige sozinho depois de bater no mínimo
            var delta = point.X - (left.X + left.Width + FreeLayoutEngine.Gap / 2);
            FreeLayoutEngine.ResizeColumns(layout, handle.Row, handle.Boundary, delta, _area.ActualWidth, FreeLayoutEngine.MinWidth);
        }
        else
        {
            var above = rects[cells[0].Account];
            var delta = point.Y - (above.Y + above.Height + FreeLayoutEngine.Gap / 2);
            FreeLayoutEngine.ResizeRows(layout, handle.Boundary, delta, _area.ActualHeight, FreeLayoutEngine.MinHeight);
        }
        Relayout();
    }

    private void EndResize(Handle handle, MouseButtonEventArgs e)
    {
        if (_resizing != handle) return;
        e.Handled = true;
        handle.Element.ReleaseMouseCapture(); // dispara o LostMouseCapture, que encerra
        if (_resizing is not null) FinishResize(handle);
    }

    private void FinishResize(Handle handle)
    {
        _resizing = null;
        handle.Element.Background = Brushes.Transparent;
        if (_covered) ExitCovers();
        _save();
    }

    private void ExitCovers()
    {
        foreach (var view in _shown) view.ExitDragMode();
    }

    private static string Fingerprint(FreeLayout? layout) => layout is null
        ? ""
        : string.Join('|', layout.Rows.Select(r =>
            r.Weight.ToString("0.####", CultureInfo.InvariantCulture) + ":" +
            string.Join(',', r.Cells.Select(c => c.Account + "=" + c.Weight.ToString("0.####", CultureInfo.InvariantCulture)))));
}
