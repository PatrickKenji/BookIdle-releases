using IdleDeck.Core.Settings;

namespace IdleDeck.Core.Layout;

public record CellPlacement(int Row, int Column, int RowSpan, int ColumnSpan, bool Visible);

public record LayoutResult(double[] RowWeights, double[] ColumnWeights, IReadOnlyList<CellPlacement> Cells);

/// <summary>
/// Calcula onde cada conta fica no Grid central. As contas nunca saem do Grid;
/// trocar de layout só muda posição, tamanho e visibilidade.
/// </summary>
public static class LayoutCalculator
{
    public static LayoutResult Compute(LayoutMode mode, int accountCount, int focused)
    {
        if (accountCount <= 0) return new LayoutResult([1.0], [1.0], []);
        if (accountCount == 1 || mode == LayoutMode.Tabs)
        {
            var cells = Enumerable.Range(0, accountCount)
                .Select(i => new CellPlacement(0, 0, 1, 1, Visible: accountCount == 1 || i == focused))
                .ToList();
            return new LayoutResult([1.0], [1.0], cells);
        }

        return mode switch
        {
            LayoutMode.Grid2x2 => Grid(accountCount),
            LayoutMode.MainPlus3 => MainPlus3(accountCount, focused),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    /// <summary>Grade automática: ⌈√n⌉ colunas, preenchida por linhas.</summary>
    private static LayoutResult Grid(int count)
    {
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (int)Math.Ceiling(count / (double)columns);
        var cells = Enumerable.Range(0, count)
            .Select(i => new CellPlacement(i / columns, i % columns, 1, 1, true))
            .ToList();
        return new LayoutResult(Enumerable.Repeat(1.0, rows).ToArray(), Enumerable.Repeat(1.0, columns).ToArray(), cells);
    }

    private static LayoutResult MainPlus3(int count, int focused)
    {
        var small = count - 1;
        var cells = new CellPlacement[count];
        var nextRow = 0;
        for (var i = 0; i < count; i++)
        {
            cells[i] = i == focused
                ? new CellPlacement(0, 0, small, 1, true)
                : new CellPlacement(nextRow++, 1, 1, 1, true);
        }
        return new LayoutResult(Enumerable.Repeat(1.0, small).ToArray(), [3.0, 1.0], cells);
    }
}
