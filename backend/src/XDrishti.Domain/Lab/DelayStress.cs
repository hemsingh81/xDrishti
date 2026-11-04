using System.Globalization;

namespace XDrishti.Domain.Lab;

/// <summary>
/// The fill-one-minute-later test of CF-05 (D3), per signal. The same signals are walked with the entry fill a minute later; a signal that no longer fills counts as 0 R, because the missed trades are the ones that ran (adverse selection). Comparing the
/// means of the filled trades only would hide them and is not done. Pure.
/// </summary>
public static class DelayStress
{
    /// <summary>Printed with the figure.</summary>
    public const string Note = "same signals, per signal: a signal that no longer fills counts as 0 R; signals the delayed run takes that the base run did not are not counted; the base is the default exit over the whole window including in-sample dates, not the walk-forward winners' out-of-sample signals, and the one-position-at-a-time rule can block a signal in one run and not the other (a blocked base signal counts as 0 R)";

    /// <param name="signals">For each signal the base run filled: its base net R and the delayed run's net R, or null when the delayed run did not fill it.</param>
    /// <param name="required">The least share of the base expectancy per signal that must be kept.</param>
    /// <param name="costsChecked">Whether the charge sheet is checked against a bill; false makes the reading unavailable (the figures are still given).</param>
    public static DelayResult Evaluate(IReadOnlyList<(double BaseR, double? DelayedR)> signals, double required, bool costsChecked)
    {
        ArgumentNullException.ThrowIfNull(signals);
        if (signals.Count == 0)
        {
            return new DelayResult(0, 0, null, null, null, required, StressStatus.Unavailable, "not evaluable: the base run filled no signal");
        }

        var baseTotal = 0.0;
        var delayedTotal = 0.0;
        var filled = 0;
        foreach (var (baseR, delayedR) in signals)
        {
            baseTotal += baseR;
            if (delayedR is { } d)
            {
                delayedTotal += d;
                filled++;
            }
        }

        var n = signals.Count;
        var baseMean = baseTotal / n;
        var delayedMean = delayedTotal / n;
        if (!double.IsFinite(baseMean) || !double.IsFinite(delayedMean))
        {
            return new DelayResult(n, filled, null, null, null, required, StressStatus.Unavailable, "not evaluable: a result is not a finite number");
        }

        if (!(baseMean > 0))
        {
            return new DelayResult(n, filled, baseMean, delayedMean, null, required, StressStatus.Unavailable, "not evaluable: the base expectancy per signal is not above 0");
        }

        var retention = delayedMean / baseMean;
        if (!costsChecked)
        {
            return new DelayResult(n, filled, baseMean, delayedMean, retention, required, StressStatus.Unavailable, CostStress.CostsNotChecked);
        }

        var ok = retention >= required;
        return new DelayResult(n, filled, baseMean, delayedMean, retention, required, ok ? StressStatus.Pass : StressStatus.Fail,
            $"{filled.ToString(CultureInfo.InvariantCulture)} of {n.ToString(CultureInfo.InvariantCulture)} signals still fill; expectancy per signal keeps {(retention * 100).ToString("0.#", CultureInfo.InvariantCulture)} % of the base ({(ok ? "at least" : "under")} {(required * 100).ToString("0.#", CultureInfo.InvariantCulture)} %)");
    }
}
