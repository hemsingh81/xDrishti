using XDrishti.Domain.Common;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The pure figures of a model's one look at the hold-out (CF-10e part 3, ADR 0285, spec D5). The model's log loss is compared with the better of two no-ML baselines read before the block (the base rate of the stored cell statistics, and each row's own cell rate), the better being the one with
/// the lower pooled log loss on the rows, so the baseline is never chosen in the model's favour; the gain of each row is the baseline's loss minus the model's, and <see cref="PairedDayBootstrap"/> gives the probability that the mean gain is above zero. The take rule is fixed before any row is seen and never chosen from the hold-out's labels: take a candidate
/// when the model's probability is at least its cell's own rate, read before the block; its effect is the mean R per candidate of what it takes (a skipped candidate counts zero) minus the mean R of taking all. Pure; the seed is the key text.
/// </summary>
public static class ModelHoldOutFigures
{
    /// <summary>Baseline name: every row gets the base rate.</summary>
    public const string BaseRate = "base_rate";

    /// <summary>Baseline name: every row gets its own cell's rate.</summary>
    public const string CellRate = "cell_rate";

    /// <summary>The failure code when there are no rows.</summary>
    public const string NoRowsCode = "holdout.no_rows";

    /// <summary>Computes the figures of the rows.</summary>
    /// <param name="rows">The scored rows (at least one).</param>
    /// <param name="baseRate">The base rate before the block, in (0, 1).</param>
    /// <param name="draws">Bootstrap resamples, 100 to 20000.</param>
    /// <param name="level">The interval's coverage.</param>
    /// <param name="minDays">The fewest distinct days the bootstrap needs.</param>
    /// <param name="seedKey">The text the seed is made of.</param>
    public static Result<ModelHoldOutResult> Compute(IReadOnlyList<ModelHoldOutRow> rows, double baseRate, int draws, double level, int minDays, string seedKey)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentException.ThrowIfNullOrWhiteSpace(seedKey);
        if (rows.Count == 0)
        {
            return Result.Failure<ModelHoldOutResult>(Error.Validation(NoRowsCode, "The hold-out gave no row the model could score."));
        }

        if (!(baseRate > 0 && baseRate < 1))
        {
            return Result.Failure<ModelHoldOutResult>(Error.Validation("holdout.invalid", "The base rate must be in (0, 1)."));
        }

        var ordered = rows.OrderBy(r => r.Day).ThenBy(r => r.Key, StringComparer.Ordinal).ToArray();
        var model = ordered.Select(r => MetricsCalculator.RowLogLoss(r.PWin, r.Won ? 1 : 0)).ToArray();
        var viaBase = ordered.Select(r => MetricsCalculator.RowLogLoss(baseRate, r.Won ? 1 : 0)).ToArray();
        var viaCell = ordered.Select(r => MetricsCalculator.RowLogLoss(r.CellRate, r.Won ? 1 : 0)).ToArray();
        var useCell = viaCell.Average() < viaBase.Average();
        var baseline = useCell ? viaCell : viaBase;
        var gains = baseline.Zip(model, (b, m) => b - m).ToArray();
        var days = ordered.Select(r => r.Day).ToArray();

        var kept = 0.0;
        var all = 0.0;
        foreach (var r in ordered)
        {
            all += r.R;
            if (r.PWin >= r.CellRate)
            {
                kept += r.R;
            }
        }

        var name = useCell ? CellRate : BaseRate;
        var bootstrap = PairedDayBootstrap.Assess(gains, days, draws, level, seedKey + "|" + name, minDays);
        return Result.Success(new ModelHoldOutResult(
            ordered.Length,
            days.Distinct().Count(),
            model.Average(),
            baseline.Average(),
            name,
            bootstrap.IsSuccess ? bootstrap.Value.ProbabilityBetter : null,
            bootstrap.IsSuccess ? bootstrap.Value.GainLow : null,
            bootstrap.IsSuccess ? bootstrap.Value.GainHigh : null,
            (kept - all) / ordered.Length));
    }
}
