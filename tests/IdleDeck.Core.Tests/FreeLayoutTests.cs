using IdleDeck.Core.Layout;
using IdleDeck.Core.Settings;

namespace IdleDeck.Core.Tests;

public class FreeLayoutTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("idledeck-free-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static FreeLayout Rows(params string[][] rows) => new()
    {
        Rows = rows.Select(r => new FreeRow { Cells = r.Select(a => new FreeCell { Account = a }).ToList() }).ToList(),
    };

    private static string[][] Shape(FreeLayout l) => l.Rows.Select(r => r.Cells.Select(c => c.Account).ToArray()).ToArray();

    private static double TotalWeight(FreeLayout l) => l.Rows.Sum(r => r.Cells.Sum(c => c.Weight));

    // ---------- Criar e reconciliar ----------

    [Fact]
    public void FromGrid_matches_grid_arrangement()
    {
        var layout = FreeLayoutEngine.FromGrid(["a", "b", "c", "d"]);

        Assert.Equal([["a", "b"], ["c", "d"]], Shape(layout));
    }

    [Fact]
    public void FromGrid_with_three_accounts_fills_the_last_row()
    {
        var layout = FreeLayoutEngine.FromGrid(["a", "b", "c"]);

        Assert.Equal([["a", "b"], ["c"]], Shape(layout));
    }

    [Fact]
    public void Reconcile_drops_removed_and_appends_new_so_every_account_appears_once()
    {
        var saved = Rows(["a", "b"], ["c", "d"]);

        var layout = FreeLayoutEngine.Reconcile(saved, ["a", "c", "d", "e"]);

        Assert.Equal([["a"], ["c", "d", "e"]], Shape(layout));
        var all = layout.Rows.SelectMany(r => r.Cells).Select(c => c.Account).OrderBy(x => x).ToList();
        Assert.Equal(["a", "c", "d", "e"], all);
    }

    [Fact]
    public void Reconcile_removes_duplicates_and_empty_rows()
    {
        var saved = Rows(["a", "a"], ["gone"]);

        var layout = FreeLayoutEngine.Reconcile(saved, ["a", "b"]);

        Assert.Equal([["a", "b"]], Shape(layout));
    }

    [Fact]
    public void Reconcile_null_uses_grid()
    {
        Assert.Equal([["a", "b"], ["c"]], Shape(FreeLayoutEngine.Reconcile(null, ["a", "b", "c"])));
    }

    // ---------- Posições ----------

    [Fact]
    public void Compute_fills_area_without_overlap()
    {
        var layout = FreeLayoutEngine.FromGrid(["a", "b", "c", "d"]);

        var rects = FreeLayoutEngine.Compute(layout, 1006, 806, 6);

        Assert.Equal(new FreeRect(0, 0, 500, 400), rects["a"]);
        Assert.Equal(new FreeRect(506, 0, 500, 400), rects["b"]);
        Assert.Equal(new FreeRect(0, 406, 500, 400), rects["c"]);
        Assert.Equal(new FreeRect(506, 406, 500, 400), rects["d"]);
    }

    [Theory]
    [InlineData(200, 100)]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(-50, -50)]
    [InlineData(double.NaN, double.NaN)]
    public void Compute_tiny_or_invalid_area_never_negative_or_nan(double width, double height)
    {
        var layout = FreeLayoutEngine.FromGrid(["a", "b", "c", "d"]);

        var rects = FreeLayoutEngine.Compute(layout, width, height, 6);

        Assert.Equal(4, rects.Count);
        foreach (var r in rects.Values)
        {
            Assert.True(r.X >= 0 && r.Y >= 0 && r.Width >= 0 && r.Height >= 0, r.ToString());
            Assert.False(double.IsNaN(r.X + r.Y + r.Width + r.Height));
        }
    }

    [Fact]
    public void Compute_survives_zero_or_negative_weights()
    {
        var layout = Rows(["a", "b"]);
        layout.Rows[0].Cells[0].Weight = 0;
        layout.Rows[0].Cells[1].Weight = -3;

        var rects = FreeLayoutEngine.Compute(layout, 1006, 400, 6);

        Assert.Equal(500, rects["a"].Width);
        Assert.Equal(500, rects["b"].Width);
    }

    // ---------- Redimensionar ----------

    [Fact]
    public void ResizeColumns_moves_boundary_and_keeps_total()
    {
        var layout = Rows(["a", "b"]);
        var before = TotalWeight(layout);

        FreeLayoutEngine.ResizeColumns(layout, 0, 0, 100, 1006, 320);

        var rects = FreeLayoutEngine.Compute(layout, 1006, 400, 6);
        Assert.Equal(600, rects["a"].Width, 3);
        Assert.Equal(400, rects["b"].Width, 3);
        Assert.Equal(before, TotalWeight(layout), 6);
    }

    [Fact]
    public void ResizeColumns_respects_minimum()
    {
        var layout = Rows(["a", "b"]);

        FreeLayoutEngine.ResizeColumns(layout, 0, 0, 400, 1006, 320);

        var rects = FreeLayoutEngine.Compute(layout, 1006, 400, 6);
        Assert.Equal(680, rects["a"].Width, 3);
        Assert.Equal(320, rects["b"].Width, 3);
    }

    [Fact]
    public void ResizeColumns_does_nothing_when_both_cells_are_already_too_small()
    {
        var layout = Rows(["a", "b"]);

        FreeLayoutEngine.ResizeColumns(layout, 0, 0, 50, 500, 320); // 247 + 247 < 2 × 320

        Assert.Equal([1.0, 1.0], layout.Rows[0].Cells.Select(c => c.Weight));
    }

    [Fact]
    public void ResizeColumns_lets_a_cell_below_the_minimum_grow_without_forcing_a_jump()
    {
        var layout = Rows(["a", "b"]);
        layout.Rows[0].Cells[0].Weight = 0.2; // 50 px de 500
        layout.Rows[0].Cells[1].Weight = 1.8; // 450 px

        FreeLayoutEngine.ResizeColumns(layout, 0, 0, 200, 506, 320);

        var rects = FreeLayoutEngine.Compute(layout, 506, 400, 6);
        Assert.Equal(180, rects["a"].Width, 3); // cresceu até a vizinha chegar no mínimo
        Assert.Equal(320, rects["b"].Width, 3);
    }

    [Fact]
    public void ResizeColumns_never_shrinks_a_cell_that_is_already_below_the_minimum()
    {
        var layout = Rows(["a", "b"]);
        layout.Rows[0].Cells[0].Weight = 0.2; // 50 px
        layout.Rows[0].Cells[1].Weight = 1.8;

        FreeLayoutEngine.ResizeColumns(layout, 0, 0, -30, 506, 320);

        var rects = FreeLayoutEngine.Compute(layout, 506, 400, 6);
        Assert.Equal(50, rects["a"].Width, 3);
        Assert.Equal(450, rects["b"].Width, 3);
    }

    [Fact]
    public void ResizeRows_moves_boundary_and_respects_minimum()
    {
        var layout = Rows(["a"], ["b"]);

        FreeLayoutEngine.ResizeRows(layout, 0, 700, 806, 200);

        var rects = FreeLayoutEngine.Compute(layout, 800, 806, 6);
        Assert.Equal(600, rects["a"].Height, 3);
        Assert.Equal(200, rects["b"].Height, 3);
    }

    // ---------- Arrastar ----------

    [Fact]
    public void ZoneAt_classifies_edges_and_center()
    {
        var cell = new FreeRect(0, 0, 400, 400);

        Assert.Equal(DropZone.Swap, FreeLayoutEngine.ZoneAt(cell, 200, 200));
        Assert.Equal(DropZone.Left, FreeLayoutEngine.ZoneAt(cell, 20, 200));
        Assert.Equal(DropZone.Right, FreeLayoutEngine.ZoneAt(cell, 380, 200));
        Assert.Equal(DropZone.Above, FreeLayoutEngine.ZoneAt(cell, 200, 20));
        Assert.Equal(DropZone.Below, FreeLayoutEngine.ZoneAt(cell, 200, 380));
    }

    [Fact]
    public void Drop_swap_exchanges_cells()
    {
        var layout = Rows(["a", "b"], ["c"]);

        var ok = FreeLayoutEngine.Drop(layout, "a", "c", DropZone.Swap);

        Assert.True(ok);
        Assert.Equal([["c", "b"], ["a"]], Shape(layout));
    }

    [Fact]
    public void Drop_left_makes_neighbor_column_and_removes_empty_row()
    {
        var layout = Rows(["a"], ["b", "c"]);

        var ok = FreeLayoutEngine.Drop(layout, "a", "c", DropZone.Left);

        Assert.True(ok);
        Assert.Equal([["b", "a", "c"]], Shape(layout));
        Assert.Equal(2.0, layout.Rows[0].Cells.Sum(c => c.Weight), 6); // b = 1; a e c dividem o 1 de c
    }

    [Fact]
    public void Drop_right_puts_the_account_after_the_target()
    {
        var layout = Rows(["a", "b"], ["c"]);

        FreeLayoutEngine.Drop(layout, "c", "a", DropZone.Right);

        Assert.Equal([["a", "c", "b"]], Shape(layout));
    }

    [Fact]
    public void Drop_below_creates_new_row()
    {
        var layout = Rows(["a", "b"], ["c"]);

        var ok = FreeLayoutEngine.Drop(layout, "a", "c", DropZone.Below);

        Assert.True(ok);
        Assert.Equal([["b"], ["c"], ["a"]], Shape(layout));
    }

    [Fact]
    public void Drop_above_creates_new_row_before_the_target_row()
    {
        var layout = Rows(["a", "b"], ["c"]);

        FreeLayoutEngine.Drop(layout, "c", "a", DropZone.Above);

        Assert.Equal([["c"], ["a", "b"]], Shape(layout));
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("a", "nao-existe")]
    [InlineData("nao-existe", "a")]
    public void Drop_invalid_pair_returns_false_and_changes_nothing(string dragged, string target)
    {
        var layout = Rows(["a", "b"], ["c"]);

        var ok = FreeLayoutEngine.Drop(layout, dragged, target, DropZone.Left);

        Assert.False(ok);
        Assert.Equal([["a", "b"], ["c"]], Shape(layout));
    }

    // ---------- Configurações e layout ----------

    [Fact]
    public void Old_settings_without_Free_load_and_the_layout_survives_a_save()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "Version": 2, "MemoryLimitMb": 650 }""");
        var store = new SettingsStore(path);

        var settings = store.Load();
        Assert.Null(settings.Profiles[0].Free);
        Assert.Equal(650, settings.MemoryLimitMb);

        settings.Profiles[0].Layout = LayoutMode.Free;
        settings.Profiles[0].Free = FreeLayoutEngine.FromGrid(["conta1", "conta2", "conta3"]);
        store.Save(settings);

        var loaded = store.Load().Profiles[0];
        Assert.Equal(LayoutMode.Free, loaded.Layout);
        Assert.Equal([["conta1", "conta2"], ["conta3"]], Shape(loaded.Free!));
    }

    [Fact]
    public void LayoutCalculator_Free_puts_every_account_in_one_visible_cell()
    {
        var r = LayoutCalculator.Compute(LayoutMode.Free, 3, 1);

        Assert.Equal([1.0], r.RowWeights);
        Assert.Equal([1.0], r.ColumnWeights);
        Assert.Equal(3, r.Cells.Count);
        Assert.All(r.Cells, c => Assert.Equal(new CellPlacement(0, 0, 1, 1, true), c));
    }
}
