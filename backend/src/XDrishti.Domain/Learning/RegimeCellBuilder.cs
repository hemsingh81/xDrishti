using XDrishti.Domain.Market.Context;

namespace XDrishti.Domain.Learning;

/// <summary>The regime cells made from tagged trades, and how the tags came out.</summary>
/// <param name="Cells">One input per cell and regime part that has at least one trade, in the order of the cells then the ordinal order of the part.</param>
/// <param name="Tally">The tags of every trade read.</param>
public sealed record RegimeCells(IReadOnlyList<CellInput> Cells, RegimeTally Tally);

/// <summary>
/// Splits cells by the regime of their trades (P6-02, ADR 0146). Pure. A trade with no label is in no regime cell and is counted in the tally; a trade labelled from the index alone is in a different cell from one labelled from the index and the VIX
/// (<see cref="RegimeLabel.CellPart"/>), so a cell never mixes the two bases. For every input cell, the trades of its regime cells plus its unlabelled trades are exactly its trades.
/// </summary>
public static class RegimeCellBuilder
{
    /// <param name="cells">The <c>*</c> cells.</param>
    /// <param name="labels">For each cell, the label of each of its trades, in the same order as <see cref="CellInput.Trades"/>.</param>
    public static RegimeCells Build(IReadOnlyList<CellInput> cells, IReadOnlyList<IReadOnlyList<RegimeLabel>> labels)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(labels);
        if (labels.Count != cells.Count)
        {
            throw new ArgumentException("One list of labels per cell.", nameof(labels));
        }

        var result = new List<CellInput>();
        var all = new List<RegimeLabel>();
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (labels[i].Count != cell.Trades.Count)
            {
                throw new ArgumentException($"The cell {cell.Cell.Key} has {cell.Trades.Count} trades and {labels[i].Count} labels.", nameof(labels));
            }

            var byPart = new SortedDictionary<string, List<CellTrade>>(StringComparer.Ordinal);
            for (var j = 0; j < cell.Trades.Count; j++)
            {
                var label = labels[i][j];
                all.Add(label);
                if (!label.IsAvailable)
                {
                    continue;
                }

                if (!byPart.TryGetValue(label.CellPart, out var trades))
                {
                    trades = [];
                    byPart[label.CellPart] = trades;
                }

                trades.Add(cell.Trades[j]);
            }

            foreach (var (part, trades) in byPart)
            {
                result.Add(new CellInput(cell.Cell with { Regime = part }, cell.Costed, trades));
            }
        }

        return new RegimeCells(result.AsReadOnly(), RegimeTally.Of(all));
    }
}
