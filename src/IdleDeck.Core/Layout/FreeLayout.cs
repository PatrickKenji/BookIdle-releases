namespace IdleDeck.Core.Layout;

/// <summary>Uma janela do modo Livre. O peso é a largura relativa dentro da linha.</summary>
public class FreeCell
{
    public string Account { get; set; } = "";
    public double Weight { get; set; } = 1;
}

/// <summary>Uma linha do modo Livre. O peso é a altura relativa entre as linhas.</summary>
public class FreeRow
{
    public double Weight { get; set; } = 1;
    public List<FreeCell> Cells { get; set; } = [];
}

/// <summary>Modo Livre: linhas, cada uma dividida em colunas. Sem sobreposição; tamanhos em proporção.</summary>
public class FreeLayout
{
    public List<FreeRow> Rows { get; set; } = [];
}

public record FreeRect(double X, double Y, double Width, double Height);

/// <summary>Onde, dentro da janela alvo, a conta arrastada foi solta.</summary>
public enum DropZone { Swap, Left, Right, Above, Below }

/// <summary>Regras puras do modo Livre (a tela só desenha o que isto calcula).</summary>
public static class FreeLayoutEngine
{
    public const double MinWidth = 320;
    public const double MinHeight = 200;
    public const double Gap = 6;

    /// <summary>Começa igual à Grade: ⌈√n⌉ colunas, preenchida por linhas (a última linha ocupa a largura toda).</summary>
    public static FreeLayout FromGrid(IReadOnlyList<string> accounts)
    {
        var layout = new FreeLayout();
        if (accounts.Count == 0) return layout;
        var columns = (int)Math.Ceiling(Math.Sqrt(accounts.Count));
        foreach (var chunk in accounts.Chunk(columns))
            layout.Rows.Add(new FreeRow { Cells = chunk.Select(a => new FreeCell { Account = a }).ToList() });
        return layout;
    }

    /// <summary>
    /// Ajusta um layout salvo às contas de hoje: tira as que não existem mais (e repetidas), apaga linhas vazias e
    /// põe as novas como coluna nova na última linha. Toda conta aparece exatamente uma vez. Sem layout salvo: Grade.
    /// </summary>
    public static FreeLayout Reconcile(FreeLayout? layout, IReadOnlyList<string> accounts)
    {
        if (layout is null || layout.Rows.Count == 0) return FromGrid(accounts);

        var wanted = accounts.ToHashSet();
        var placed = new HashSet<string>();
        var result = new FreeLayout();
        foreach (var row in layout.Rows)
        {
            var cells = row.Cells
                .Where(c => wanted.Contains(c.Account) && placed.Add(c.Account))
                .Select(c => new FreeCell { Account = c.Account, Weight = Sane(c.Weight) })
                .ToList();
            if (cells.Count > 0) result.Rows.Add(new FreeRow { Weight = Sane(row.Weight), Cells = cells });
        }
        if (result.Rows.Count == 0) return FromGrid(accounts);

        foreach (var account in accounts.Where(placed.Add))
            result.Rows[^1].Cells.Add(new FreeCell { Account = account });
        return result;
    }

    /// <summary>
    /// Retângulo de cada conta numa área de <paramref name="width"/> × <paramref name="height"/>, com
    /// <paramref name="gap"/> entre janelas. Nunca devolve tamanho negativo ou NaN, mesmo com área minúscula.
    /// </summary>
    public static IReadOnlyDictionary<string, FreeRect> Compute(FreeLayout layout, double width, double height, double gap)
    {
        var result = new Dictionary<string, FreeRect>();
        width = Clean(width);
        height = Clean(height);
        gap = Clean(gap);
        var rows = layout.Rows.Where(r => r.Cells.Count > 0).ToList();
        if (rows.Count == 0) return result;

        var availableHeight = Math.Max(0, height - gap * (rows.Count - 1));
        var rowTotal = rows.Sum(r => Sane(r.Weight));
        var y = 0.0;
        foreach (var row in rows)
        {
            var rowHeight = availableHeight * Sane(row.Weight) / rowTotal;
            var availableWidth = Math.Max(0, width - gap * (row.Cells.Count - 1));
            var cellTotal = row.Cells.Sum(c => Sane(c.Weight));
            var x = 0.0;
            foreach (var cell in row.Cells)
            {
                var cellWidth = availableWidth * Sane(cell.Weight) / cellTotal;
                result[cell.Account] = new FreeRect(x, y, cellWidth, rowHeight);
                x += cellWidth + gap;
            }
            y += rowHeight + gap;
        }
        return result;
    }

    /// <summary>
    /// Move a borda entre a coluna <paramref name="boundary"/> e a seguinte, na linha <paramref name="row"/>.
    /// <paramref name="deltaPx"/> positivo alarga a da esquerda. Respeita o mínimo; a soma dos pesos não muda.
    /// </summary>
    public static void ResizeColumns(FreeLayout layout, int row, int boundary, double deltaPx, double widthPx, double minPx)
    {
        if (row < 0 || row >= layout.Rows.Count) return;
        var cells = layout.Rows[row].Cells;
        if (boundary < 0 || boundary + 1 >= cells.Count) return;
        var available = Math.Max(0, Clean(widthPx) - Gap * (cells.Count - 1));
        var total = cells.Sum(c => Sane(c.Weight));
        if (MovePair(Sane(cells[boundary].Weight), Sane(cells[boundary + 1].Weight), total, available, deltaPx, minPx) is not var (a, b)) return;
        cells[boundary].Weight = a;
        cells[boundary + 1].Weight = b;
    }

    /// <summary>Igual a <see cref="ResizeColumns"/>, para a borda entre a linha <paramref name="boundary"/> e a seguinte.</summary>
    public static void ResizeRows(FreeLayout layout, int boundary, double deltaPx, double heightPx, double minPx)
    {
        if (boundary < 0 || boundary + 1 >= layout.Rows.Count) return;
        var available = Math.Max(0, Clean(heightPx) - Gap * (layout.Rows.Count - 1));
        var total = layout.Rows.Sum(r => Sane(r.Weight));
        if (MovePair(Sane(layout.Rows[boundary].Weight), Sane(layout.Rows[boundary + 1].Weight), total, available, deltaPx, minPx) is not var (a, b)) return;
        layout.Rows[boundary].Weight = a;
        layout.Rows[boundary + 1].Weight = b;
    }

    /// <summary>Zona de uma janela: as bordas (25 % de cada lado) viram Esquerda/Direita/Acima/Abaixo; o miolo troca de lugar.</summary>
    public static DropZone ZoneAt(FreeRect cell, double x, double y)
    {
        if (cell.Width <= 0 || cell.Height <= 0) return DropZone.Swap;
        var fx = (x - cell.X) / cell.Width;
        var fy = (y - cell.Y) / cell.Height;
        var (zone, distance) = new[]
        {
            (DropZone.Left, fx), (DropZone.Right, 1 - fx), (DropZone.Above, fy), (DropZone.Below, 1 - fy),
        }.MinBy(t => t.Item2);
        return distance < 0.25 ? zone : DropZone.Swap;
    }

    /// <summary>
    /// Solta <paramref name="dragged"/> sobre <paramref name="target"/>: troca de lugar, vira coluna vizinha ou linha nova.
    /// Par inválido (iguais ou desconhecidos): devolve false e não muda nada.
    /// </summary>
    public static bool Drop(FreeLayout layout, string dragged, string target, DropZone zone)
    {
        if (dragged == target || Find(layout, dragged) is null || Find(layout, target) is null) return false;

        if (zone == DropZone.Swap)
        {
            var (_, a) = Find(layout, dragged)!.Value;
            var (_, b) = Find(layout, target)!.Value;
            (a.Account, b.Account) = (b.Account, a.Account);
            return true;
        }

        // tira a arrastada de onde estava (linha que ficar vazia some) e encaixa ao lado/acima/abaixo do alvo
        var (oldRow, moving) = Find(layout, dragged)!.Value;
        oldRow.Cells.Remove(moving);
        if (oldRow.Cells.Count == 0) layout.Rows.Remove(oldRow);

        var (targetRow, targetCell) = Find(layout, target)!.Value;
        var moved = new FreeCell { Account = dragged };
        if (zone is DropZone.Left or DropZone.Right)
        {
            moved.Weight = Sane(targetCell.Weight) / 2; // divide a largura do alvo
            targetCell.Weight = moved.Weight;
            var at = targetRow.Cells.IndexOf(targetCell) + (zone == DropZone.Right ? 1 : 0);
            targetRow.Cells.Insert(at, moved);
        }
        else
        {
            var half = Sane(targetRow.Weight) / 2; // divide a altura da linha do alvo
            targetRow.Weight = half;
            var at = layout.Rows.IndexOf(targetRow) + (zone == DropZone.Below ? 1 : 0);
            layout.Rows.Insert(at, new FreeRow { Weight = half, Cells = [moved] });
        }
        return true;
    }

    private static (FreeRow Row, FreeCell Cell)? Find(FreeLayout layout, string account)
    {
        foreach (var row in layout.Rows)
        foreach (var cell in row.Cells)
            if (cell.Account == account) return (row, cell);
        return null;
    }

    private static (double A, double B)? MovePair(double weightA, double weightB, double totalWeight, double availablePx, double deltaPx, double minPx)
    {
        if (availablePx <= 0 || totalWeight <= 0) return null;
        var pxA = availablePx * weightA / totalWeight;
        var pxB = availablePx * weightB / totalWeight;
        // Uma célula nunca encolhe abaixo do mínimo — e, se já está abaixo (área apertada), não encolhe mais, mas
        // também não é forçada a crescer: a alça nunca fica "morta" nem dá salto.
        var lowest = Math.Min(minPx, pxA) - pxA;  // ≤ 0
        var highest = pxB - Math.Min(minPx, pxB); // ≥ 0
        var delta = Math.Clamp(double.IsFinite(deltaPx) ? deltaPx : 0, lowest, highest);
        return ((pxA + delta) / availablePx * totalWeight, (pxB - delta) / availablePx * totalWeight);
    }

    private static double Sane(double weight) => double.IsFinite(weight) && weight > 0 ? weight : 1;

    private static double Clean(double value) => double.IsFinite(value) && value > 0 ? value : 0;
}
