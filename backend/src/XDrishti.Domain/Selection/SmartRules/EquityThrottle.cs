using System.Globalization;

namespace XDrishti.Domain.Selection.SmartRules;

/// <summary>
/// The equity-curve throttle (doc 09 section 14, spec P7-06): equity is the running sum of R of the real taken trades; while the latest equity is below the average of its last N points, risk is multiplied by <c>ThrottleCut</c>.
/// Only trades closed before the decision date count. Fewer than N trades (including none) is <see cref="RuleStatus.Unavailable"/>: the throttle is inactive and says why, risk stays x1. It never reduces the number of tickets.
/// </summary>
public static class EquityThrottle
{
    /// <summary>Evaluates the throttle.</summary>
    public static ThrottleResult Evaluate(IEnumerable<TakenTrade>? trades, DateOnly decisionDate, SmartRulesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.ThrottleEnabled)
        {
            return new ThrottleResult(RuleStatus.Off, SmartRuleCodes.ThrottleOff, 1, null, null, 0, "throttle switched off");
        }

        var ordered = (trades ?? [])
            .Where(t => t.ClosedOn < decisionDate && double.IsFinite(t.R))
            .OrderBy(t => t.ClosedOn)
            .ThenBy(t => t.TradeId, StringComparer.Ordinal)
            .ThenBy(t => t.R)
            .ToList();
        var n = Math.Max(1, options.ThrottleWindowTrades);
        if (ordered.Count < n)
        {
            return new ThrottleResult(
                RuleStatus.Unavailable,
                SmartRuleCodes.ThrottleUnavailable,
                1,
                null,
                null,
                ordered.Count,
                string.Create(CultureInfo.InvariantCulture, $"inactive: {ordered.Count} real taken trades closed before the decision date, {n} needed"));
        }

        var equity = new List<double>(ordered.Count);
        var sum = 0.0;
        foreach (var t in ordered)
        {
            sum += t.R;
            equity.Add(sum);
        }

        var latest = equity[^1];
        var average = equity.Skip(equity.Count - n).Average();
        var below = Math.Round(latest, 9) < Math.Round(average, 9);
        return below
            ? new ThrottleResult(RuleStatus.Applied, SmartRuleCodes.ThrottleCut, options.ThrottleCut, latest, average, ordered.Count, string.Create(CultureInfo.InvariantCulture, $"equity {latest:0.###} R is below its {n}-trade average {average:0.###} R: risk x {options.ThrottleCut:0.##}"))
            : new ThrottleResult(RuleStatus.Applied, SmartRuleCodes.ThrottleClear, 1, latest, average, ordered.Count, string.Create(CultureInfo.InvariantCulture, $"equity {latest:0.###} R is at or above its {n}-trade average {average:0.###} R"));
    }
}
