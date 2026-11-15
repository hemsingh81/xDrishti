using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Lab;

/// <summary>
/// The cost stress gates of CF-05 (ADR 0206) for a Lab verdict: the stored out-of-sample trades restated at the stress multipliers, the delay test's signals judged, and, only when the owner has turned <c>Enforce</c> on, the outcome capped at MARGINAL by a miss.
/// With <c>Enforce</c> off (the default) the outcome, the gates, the limits and the run key are exactly what they were; the figure is added beside them. Pure over its inputs.
/// </summary>
internal sealed class LabCostStress(CostStressOptions options, CostBufferOptions buffer)
{
    /// <summary>Printed when the trades carry no charges (a gross run) and the cost buffer is not usable: there is nothing to stress.</summary>
    public const string Gross = "unavailable: the trades carry no charges from a stored sheet (a gross run) and the cost buffer setting is not usable";

    /// <summary>Printed with the figure of a gross run stressed on the cost buffer.</summary>
    public const string BufferFormula = "stressed R = gross R - m x buffer for each stored out-of-sample trade, then averaged (GROSS-01: costs are gross, the stated cost buffer per round trip stands for charges and slippage)";

    private const string ExpectancyGate = "expectancy_R";

    /// <summary>True when the stress is computed.</summary>
    public bool IsOn => options.IsOn;

    /// <summary>True when a missed stress gate caps a PASS (the owner's switch).</summary>
    public bool Enforce => options.Enforce;

    /// <param name="verdict">The verdict as judged, for the existing expectancy gate, the outcome and the limits.</param>
    /// <param name="oosTrades">The out-of-sample trades the verdict was judged on.</param>
    /// <param name="sheetId">The charge sheet the trades were costed with; null when none is known.</param>
    /// <param name="costsChecked">The sheet carries a bill check.</param>
    /// <param name="delay">The signals of the delay test; null when the run did not take it.</param>
    /// <param name="ratesSha256">The hash of the sheet's rates.</param>
    /// <param name="tiers">The tier and capacity counts (ADR 0223), carried on the view and read by nothing here.</param>
    public LabVerdictView Apply(LabVerdictView verdict, IReadOnlyList<SelectionTrade> oosTrades, string? sheetId, bool costsChecked, DelaySignals? delay, string? ratesSha256 = null, CostStressTiersView? tiers = null)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        ArgumentNullException.ThrowIfNull(oosTrades);
        if (!options.IsOn)
        {
            return verdict;
        }

        var gate = verdict.Gates.FirstOrDefault(g => string.Equals(g.Name, ExpectancyGate, StringComparison.Ordinal));
        var gross = oosTrades.Any(t => t.RExact is null);

        // GROSS-01 (ADR 0294): a gross run is stressed on gross R less the cost buffer: each trade's cost is the buffer, so its R at multiplier m is gross - m x buffer. The buffer is the stated cost (there is no bill to check), so the rows are judged;
        // an unusable buffer leaves them unavailable, never a pass.
        // shortcut: the buffer is a start-validated singleton (the host refuses an out-of-range value), so an invalid buffer cannot occur in a running host; the arm is kept fail closed for a host that skips the validation
        var buffered = gross && buffer.IsValid;
        IReadOnlyList<StressTrade> trades = buffered ? [.. oosTrades.Select(t => new StressTrade(t.R, buffer.Apply(t.R)))] : gross ? [] : [.. oosTrades.Select(t => new StressTrade(t.RExact!.Value, t.R))];
        // the 1x row of a buffered run is judged on its own figure (gross - 1 x buffer) against the gate's adjusted threshold, not on the gross gate's verdict
        var oneXPassed = buffered ? CostStress.Mean(trades, 1) is { } oneX && oneX >= (gate?.Adjusted ?? double.PositiveInfinity) : gate?.Passed ?? false;
        var result = CostStress.Evaluate(trades, options, gate?.Adjusted ?? 0, oneXPassed, buffered || costsChecked, buffered ? buffer.Label() : "net");
        var unavailable = gross && !buffered;
        var rows = result.Points.Select(p => new CostStressRowView(p.Multiplier, unavailable ? oosTrades.Count : p.Trades, p.ExpectancyR, p.Threshold, p.Comparison, Word(p.Status), unavailable ? Gross : p.Reason)).ToList();
        var delayView = delay is null ? null : DelayOf(delay, costsChecked);
        var tradable = unavailable ? StressStatus.Unavailable : result.Tradable;
        var misses = new List<string>();
        for (var i = 1; i < result.Points.Count; i++)
        {
            if (result.Points[i].Status != StressStatus.Pass)
            {
                misses.Add(Multiplier(result.Points[i].Multiplier) + " stress " + Word(result.Points[i].Status));
            }
        }

        if (delayView is not null && delayView.Status != "pass")
        {
            misses.Add("delay test " + delayView.Status);
        }

        var cap = options.Enforce && misses.Count > 0 && string.Equals(verdict.Outcome, "PASS", StringComparison.Ordinal);
        var view = new CostStressView(
            options.Mode.ToString(), options.Enforce, options.KeyText(), options.SnapshotHash(), costsChecked, sheetId, rows, Word(tradable), delayView, cap, buffered ? BufferFormula : CostStress.Formula, CostStress.FirstOrderLimit, ratesSha256, tiers, buffered ? buffer.Label() : null);
        return cap
            ? verdict with { Outcome = "MARGINAL", Limits = [.. verdict.Limits, "capped at MARGINAL by the cost stress gates (enforced): " + string.Join(", ", misses)], CostStress = view }
            : verdict with { CostStress = view };
    }

    private CostStressDelayView DelayOf(DelaySignals delay, bool costsChecked)
    {
        if (delay.Error is { } error)
        {
            return new CostStressDelayView(delay.Minutes, 0, 0, null, null, null, options.DelayRetention, "unavailable", "unavailable: the delayed run failed: " + error, DelayStress.Note);
        }

        var r = DelayStress.Evaluate(delay.Signals, options.DelayRetention, costsChecked);
        return new CostStressDelayView(delay.Minutes, r.Signals, r.DelayedFilled, r.BaseMeanR, r.DelayedMeanR, r.Retention, r.Required, Word(r.Status), r.Reason, DelayStress.Note);
    }

    private static string Word(StressStatus status) => status switch
    {
        StressStatus.Pass => "pass",
        StressStatus.Fail => "fail",
        _ => "unavailable",
    };

    private static string Multiplier(double m) => m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";
}
