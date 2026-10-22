using XDrishti.Domain.Research;

namespace XDrishti.Domain.Lab;

/// <summary>
/// Turns the walk-forward choices of the cells of a full test into the <see cref="OosEvidence"/> the verdict is judged on (spec P5-04, assumption 3). Pure: the only trades that count are those of the winner of a fold that entered in that
/// fold's test window, the same trades as the pooled figure of <see cref="ExitSelection"/>; the full-period figures never enter. Pooled windows are the test windows (same dates), a window is positive when its pooled R is above 0, the drawdown is
/// the largest peak-to-trough fall of the cumulative R in the order of exit.
/// </summary>
public static class OosEvidenceBuilder
{
    /// <param name="cells">For each cell the input of the choice and its result.</param>
    /// <param name="calibrationErrorPp">The calibration error to put in the evidence (0 for a strategy with no probability model).</param>
    public static OosPool Pool(IReadOnlyList<(SelectionInput Input, ExitSelection Selection)> cells, double calibrationErrorPp)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var taken = new List<SelectionTrade>();
        var windows = new SortedDictionary<(DateOnly, DateOnly), (int Trades, double Total)>();
        foreach (var (input, selection) in cells)
        {
            foreach (var fold in selection.Folds.Where(f => f.Winner is not null))
            {
                var mine = input.Trades.Where(t => string.Equals(t.CandidateId, fold.Winner, StringComparison.Ordinal) && t.Entry >= fold.TestStart && t.Entry <= fold.TestEnd).ToList();
                if (mine.Count == 0)
                {
                    continue;
                }

                taken.AddRange(mine);
                var key = (fold.TestStart, fold.TestEnd);
                windows.TryGetValue(key, out var so);
                windows[key] = (so.Trades + mine.Count, so.Total + mine.Sum(t => t.R));
            }
        }

        var ordered = taken.OrderBy(t => t.Exit).ThenBy(t => t.Entry).ThenBy(t => t.CandidateId, StringComparer.Ordinal).ThenBy(t => t.R).ToList();
        var gains = ordered.Where(t => t.R > 0).Sum(t => t.R);
        var losses = -ordered.Where(t => t.R < 0).Sum(t => t.R);
        var profitFactor = losses > 0 ? gains / losses : gains > 0 ? double.PositiveInfinity : 0;
        double cumulative = 0, peak = 0, drawdown = 0;
        foreach (var t in ordered)
        {
            cumulative += t.R;
            peak = Math.Max(peak, cumulative);
            drawdown = Math.Max(drawdown, peak - cumulative);
        }

        var list = windows.Select(w => new OosWindow(w.Key.Item1, w.Key.Item2, w.Value.Trades, w.Value.Total)).ToList();
        var evidence = new OosEvidence(
            ordered.Count,
            ordered.Count == 0 ? 0 : ordered.Average(t => t.R),
            profitFactor,
            list.Count,
            list.Count(w => w.TotalR > 0),
            drawdown,
            calibrationErrorPp,
            OosEvidence.WalkForward);
        return new OosPool(evidence, list.AsReadOnly());
    }
}
