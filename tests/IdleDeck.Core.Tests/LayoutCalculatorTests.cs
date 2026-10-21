using IdleDeck.Core.Layout;
using IdleDeck.Core.Settings;

namespace IdleDeck.Core.Tests;

public class LayoutCalculatorTests
{
    [Fact]
    public void Tabs_shows_only_focused()
    {
        var r = LayoutCalculator.Compute(LayoutMode.Tabs, 4, 2);

        Assert.Equal([1.0], r.RowWeights);
        Assert.Equal([1.0], r.ColumnWeights);
        Assert.Equal(4, r.Cells.Count);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(i == 2, r.Cells[i].Visible);
            Assert.Equal((0, 0, 1, 1), (r.Cells[i].Row, r.Cells[i].Column, r.Cells[i].RowSpan, r.Cells[i].ColumnSpan));
        }
    }

    [Fact]
    public void Grid2x2_places_four_cells()
    {
        var r = LayoutCalculator.Compute(LayoutMode.Grid2x2, 4, 0);

        Assert.Equal([1.0, 1.0], r.RowWeights);
        Assert.Equal([1.0, 1.0], r.ColumnWeights);
        Assert.Equal(new CellPlacement(0, 0, 1, 1, true), r.Cells[0]);
        Assert.Equal(new CellPlacement(0, 1, 1, 1, true), r.Cells[1]);
        Assert.Equal(new CellPlacement(1, 0, 1, 1, true), r.Cells[2]);
        Assert.Equal(new CellPlacement(1, 1, 1, 1, true), r.Cells[3]);
    }

    [Fact]
    public void Grid2x2_with_three_accounts()
    {
        var r = LayoutCalculator.Compute(LayoutMode.Grid2x2, 3, 0);

        Assert.Equal(3, r.Cells.Count);
        Assert.All(r.Cells, c => Assert.True(c.Visible));
        Assert.Equal(new CellPlacement(1, 0, 1, 1, true), r.Cells[2]);
    }

    [Fact]
    public void MainPlus3_focused_is_big()
    {
        var r = LayoutCalculator.Compute(LayoutMode.MainPlus3, 4, 1);

        Assert.Equal([3.0, 1.0], r.ColumnWeights);
        Assert.Equal([1.0, 1.0, 1.0], r.RowWeights);
        Assert.Equal(new CellPlacement(0, 0, 3, 1, true), r.Cells[1]);
        Assert.Equal(new CellPlacement(0, 1, 1, 1, true), r.Cells[0]);
        Assert.Equal(new CellPlacement(1, 1, 1, 1, true), r.Cells[2]);
        Assert.Equal(new CellPlacement(2, 1, 1, 1, true), r.Cells[3]);
    }

    [Fact]
    public void MainPlus3_single_account_fills()
    {
        var r = LayoutCalculator.Compute(LayoutMode.MainPlus3, 1, 0);

        Assert.Equal([1.0], r.RowWeights);
        Assert.Equal([1.0], r.ColumnWeights);
        Assert.Equal(new CellPlacement(0, 0, 1, 1, true), r.Cells[0]);
    }

    [Fact]
    public void Grid_five_accounts_three_by_two()
    {
        var r = LayoutCalculator.Compute(LayoutMode.Grid2x2, 5, 0);

        Assert.Equal([1.0, 1.0, 1.0], r.ColumnWeights);
        Assert.Equal([1.0, 1.0], r.RowWeights);
        Assert.Equal(new CellPlacement(1, 1, 1, 1, true), r.Cells[4]);
    }

    [Fact]
    public void Grid_nine_accounts_three_by_three()
    {
        var r = LayoutCalculator.Compute(LayoutMode.Grid2x2, 9, 0);

        Assert.Equal(3, r.ColumnWeights.Length);
        Assert.Equal(3, r.RowWeights.Length);
        Assert.Equal(new CellPlacement(2, 2, 1, 1, true), r.Cells[8]);
    }

    [Fact]
    public void Zero_accounts_is_empty()
    {
        var r = LayoutCalculator.Compute(LayoutMode.MainPlus3, 0, 0);

        Assert.Empty(r.Cells);
        Assert.Equal([1.0], r.RowWeights);
        Assert.Equal([1.0], r.ColumnWeights);
    }
}
