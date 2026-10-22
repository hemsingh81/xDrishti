using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning;

/// <summary>One closed trade read from a source run, with the key of the cell it belongs to.</summary>
internal sealed record SourceTrade(SimTrade Trade, string CellKey);

/// <summary>The cells and the trades read from the sources of a learning use case: the cells ordered by key, the trades in the order of the sources.</summary>
internal sealed record CellSourceData(IReadOnlyList<CellInput> Cells, IReadOnlyList<SourceTrade> Trades);

/// <summary>
/// Reads the closed trades of stored runs into cells (P6-01, shared with P6-04): strategy, timeframe and side from the trades, style and exit from the source. Refuses an unknown run, two runs of one strategy, two runs making one cell and a cell that mixes
/// charged and uncharged trades, so every use case that builds cells counts a signal once.
/// </summary>
internal static class CellSourceReader
{
    public static async ValueTask<Result<CellSourceData>> ReadAsync(IRunStore runs, IReadOnlyList<CellSource> sources, CancellationToken cancellationToken)
    {
        var inputs = new Dictionary<string, (CellId Cell, bool Costed, List<CellTrade> Trades)>(StringComparer.Ordinal);
        var all = new List<SourceTrade>();
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        var strategyOwner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (await runs.FindAsync(source.RunKey, cancellationToken) is null)
            {
                return Result.Failure<CellSourceData>(LearningErrors.RunNotFound(source.RunKey));
            }

            await foreach (var runTrade in runs.StreamTradesAsync(source.RunKey, false, cancellationToken))
            {
                var t = runTrade.Trade;
                var id = new CellId(t.StrategyId, t.TriggerTimeframe.ToCode(), t.Side.ToString().ToUpperInvariant(), source.Style, source.Exit);
                var costed = t.Costs == CostBasis.Charged && t.Costing is not null;
                var r = costed ? t.Costing!.RNet : t.RFilled;
                if (!inputs.TryGetValue(id.Key, out var cell))
                {
                    cell = (id, costed, []);
                    inputs[id.Key] = cell;
                }

                if (!strategyOwner.TryAdd(t.StrategyId, source.RunKey) && !string.Equals(strategyOwner[t.StrategyId], source.RunKey, StringComparison.Ordinal))
                {
                    return Result.Failure<CellSourceData>(LearningErrors.StrategyDuplicate(t.StrategyId));
                }

                if (cell.Costed != costed)
                {
                    return Result.Failure<CellSourceData>(LearningErrors.MixedBasis(id.Key));
                }

                if (!owner.TryAdd(id.Key, source.RunKey) && !string.Equals(owner[id.Key], source.RunKey, StringComparison.Ordinal))
                {
                    return Result.Failure<CellSourceData>(LearningErrors.CellDuplicate(id.Key));
                }

                cell.Trades.Add(new CellTrade(SessionClock.DateOf(t.ExitEpochSeconds), r, t.MaeR, t.MfeR));
                all.Add(new SourceTrade(t, id.Key));
            }
        }

        var ordered = inputs.Values.OrderBy(c => c.Cell.Key, StringComparer.Ordinal).Select(c => new CellInput(c.Cell, c.Costed, c.Trades)).ToList();
        return Result.Success(new CellSourceData(ordered, all));
    }
}
