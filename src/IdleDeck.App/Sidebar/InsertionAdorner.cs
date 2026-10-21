using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace IdleDeck.App.Sidebar;

/// <summary>Linha amarela na borda de cima ou de baixo de uma linha da lista: "a conta vai cair aqui".</summary>
public sealed class InsertionAdorner : Adorner
{
    private readonly bool _below;

    public InsertionAdorner(UIElement row, bool below) : base(row)
    {
        _below = below;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var brush = (Brush)Application.Current.Resources["Yellow"];
        var pen = new Pen(brush, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var y = _below ? AdornedElement.RenderSize.Height + 1 : -1;
        drawing.DrawLine(pen, new Point(4, y), new Point(AdornedElement.RenderSize.Width - 4, y));
    }
}
