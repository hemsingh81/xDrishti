using System.Globalization;

namespace XDrishti.Domain.Selection.SmartRules;

/// <summary>
/// The market gate (doc 09 section 14, spec P7-06). VIX at or above <c>VixNoTrade</c> is no-trade; VIX at or above <c>VixReduce</c>, breadth below <c>BreadthMin</c> or a high-impact event is reduced; else normal.
/// A limit that trips on a known figure counts even when another figure is missing (it can only make the plan safer). Normal needs all three figures: a missing one with nothing tripped is <see cref="GateMode.Unavailable"/>, never a pass.
/// Figures dated on or after the decision date are not known (no look-ahead), and figures older than <c>GateMaxAgeDays</c> are stale: both read unavailable.
/// </summary>
public static class MarketGate
{
    /// <summary>Evaluates the gate.</summary>
    public static GateResult Evaluate(MarketGateInput? input, DateOnly decisionDate, SmartRulesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.GateEnabled)
        {
            return new GateResult(GateMode.Normal, RuleStatus.Off, SmartRuleCodes.GateOff, null, null, [], []);
        }

        var known = input is not null && input.AsOf < decisionDate && input.AsOf >= decisionDate.AddDays(-options.GateMaxAgeDays);
        var vix = known ? Finite(input!.Vix) : null;
        var breadth = known ? Finite(input!.Breadth) : null;
        var evt = known ? input!.HighImpactEventToday : null;
        var missing = new List<string>();
        if (vix is null)
        {
            missing.Add("vix");
        }

        if (breadth is null)
        {
            missing.Add("breadth");
        }

        if (evt is null)
        {
            missing.Add("events");
        }

        var triggers = new List<string>();
        var noTrade = vix is { } v1 && v1 >= options.VixNoTrade;
        if (noTrade)
        {
            triggers.Add(string.Create(CultureInfo.InvariantCulture, $"vix {vix:0.##} >= {options.VixNoTrade:0.##}"));
        }

        var reduced = false;
        if (!noTrade && vix is { } v2 && v2 >= options.VixReduce)
        {
            reduced = true;
            triggers.Add(string.Create(CultureInfo.InvariantCulture, $"vix {v2:0.##} >= {options.VixReduce:0.##}"));
        }

        if (breadth is { } b && b < options.BreadthMin)
        {
            reduced = true;
            triggers.Add(string.Create(CultureInfo.InvariantCulture, $"breadth {b:0.###} < {options.BreadthMin:0.###}"));
        }

        if (evt == true)
        {
            reduced = true;
            triggers.Add("high-impact event today");
        }

        if (noTrade)
        {
            return new GateResult(GateMode.NoTrade, RuleStatus.Applied, SmartRuleCodes.GateNoTrade, vix, breadth, missing, triggers);
        }

        if (reduced)
        {
            return new GateResult(GateMode.Reduced, RuleStatus.Applied, SmartRuleCodes.GateReduced, vix, breadth, missing, triggers);
        }

        return missing.Count > 0
            ? new GateResult(GateMode.Unavailable, RuleStatus.Unavailable, SmartRuleCodes.GateUnavailable, vix, breadth, missing, [])
            : new GateResult(GateMode.Normal, RuleStatus.Applied, SmartRuleCodes.GateNormal, vix, breadth, missing, []);
    }

    private static double? Finite(double? value) => value is { } v && double.IsFinite(v) ? v : null;
}
