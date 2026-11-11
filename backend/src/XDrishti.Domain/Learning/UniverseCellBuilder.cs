namespace XDrishti.Domain.Learning;

/// <summary>The tier cells made from tagged trades, and how the tags came out.</summary>
/// <param name="Cells">One input per cell and tier that has at least one trade, in the order of the cells then the tier order (high, mid, low).</param>
/// <param name="Unknown">Trades with no ranked tier (too few bars, not in the tracked set that date, or no dated data): in the <c>*</c> cell only.</param>
/// <param name="Unavailable">Trades dated when too few shares could be ranked: in the <c>*</c> cell only.</param>
public sealed record UniverseCells(IReadOnlyList<CellInput> Cells, int Unknown, int Unavailable);

/// <summary>
/// Splits cells by the liquidity tier of their trades on the decision date (BK-11, ADR 0261). Pure. A trade without a ranked tier is in no tier cell and is counted; the tier cells' trades plus the untiered ones are exactly the cell's trades. Only the
/// <c>*</c>-regime cells are split (no regime x tier cells: one extra dimension, never a product).
/// </summary>
public static class UniverseCellBuilder
{
    /// <param name="cells">The <c>*</c> cells.</param>
    /// <param name="tiers">For each cell, the tier of each of its trades, in the order of <see cref="CellInput.Trades"/>.</param>
    public static UniverseCells Build(IReadOnlyList<CellInput> cells, IReadOnlyList<IReadOnlyList<UniverseTier>> tiers)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(tiers);
        if (tiers.Count != cells.Count)
        {
            throw new ArgumentException("One list of tiers per cell.", nameof(tiers));
        }

        var result = new List<CellInput>();
        var unknown = 0;
        var unavailable = 0;
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (tiers[i].Count != cell.Trades.Count)
            {
                throw new ArgumentException($"The cell {cell.Cell.Key} has {cell.Trades.Count} trades and {tiers[i].Count} tiers.", nameof(tiers));
            }

            var byTier = new Dictionary<UniverseTier, List<CellTrade>>();
            for (var j = 0; j < cell.Trades.Count; j++)
            {
                var tier = tiers[i][j];
                if (tier.CellPart() is null)
                {
                    if (tier == UniverseTier.Unavailable)
                    {
                        unavailable++;
                    }
                    else
                    {
                        unknown++;
                    }

                    continue;
                }

                if (!byTier.TryGetValue(tier, out var trades))
                {
                    byTier[tier] = trades = [];
                }

                trades.Add(cell.Trades[j]);
            }

            foreach (var tier in new[] { UniverseTier.High, UniverseTier.Mid, UniverseTier.Low })
            {
                if (byTier.TryGetValue(tier, out var trades))
                {
                    result.Add(new CellInput(cell.Cell with { Universe = tier.CellPart()! }, cell.Costed, trades));
                }
            }
        }

        return new UniverseCells(result.AsReadOnly(), unknown, unavailable);
    }
}
