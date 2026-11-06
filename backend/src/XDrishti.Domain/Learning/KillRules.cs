using System.Globalization;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The strategy-level kill rules of spec CF-12, D2 (ADR 0218), as pure evaluation functions that return a verdict with its numbers. They propose, persist and execute nothing: turning a firing rule into a proposal, a notice or a pause is part 12b.
/// (a) <b>cost stress</b>: on the trailing 100 trades, <c>net(1.5) = net(1) - 0.5 c</c> strictly below zero fires (c = mean of charges plus the size of the slippage, in R); fewer than 100 trades is not evaluated; a rule that fires again needs 20 new trades since it
/// last fired. (b) <b>drawdown</b>: the series' largest fall strictly above <c>factor</c> (2) times the 95th percentile of the matched draws at the series' own n. (c) <b>decay twice in a row</b>. (d) the CF-09 <b>failure boundary</b>, marked proposal only.
/// The numbers 100, 1.5, 2 and 20 are proposals of the spec; the settings that will carry them (<c>confidence.kill.*</c>) are part 12b, so they are arguments with these defaults. A missing figure is <see cref="KillRuleState.NotEvaluated"/> and never a pass.
/// </summary>
public static class KillRules
{
    /// <summary>The trades of the cost stress window (spec D2, proposal).</summary>
    public const int TrailingTrades = 100;

    /// <summary>The stress multiplier of cost, <c>net(1.5)</c> (CF-05).</summary>
    public const double StressMultiplier = 1.5;

    /// <summary>The factor on the 95th percentile of the matched drawdown (spec D2 and review fix 4, proposal).</summary>
    public const double DrawdownFactor = 2;

    /// <summary>The new trades a rule needs since it last fired before it can fire again (review fix 3, proposal).</summary>
    public const int NewTradesBetweenFirings = 20;

    private const double Tie = 1e-12;

    /// <summary>Rule (a) from the net R and the cost c (in R) of each of the latest trades, oldest first; only the last <see cref="TrailingTrades"/> are read.</summary>
    /// <param name="netR">Net R of each trade.</param>
    /// <param name="costR">Charges plus the size of the slippage, in R, of each trade (same length).</param>
    /// <param name="tradesSinceLastFiring">Trades since this rule last fired, or null when it never did.</param>
    /// <param name="stressMultiplier">The multiplier of cost (default 1.5; <c>Confidence:Kill:StressMultiplier</c>, which can only be raised).</param>
    public static KillRuleVerdict CostStress(IReadOnlyList<double> netR, IReadOnlyList<double> costR, int? tradesSinceLastFiring = null, double stressMultiplier = StressMultiplier)
    {
        ArgumentNullException.ThrowIfNull(netR);
        ArgumentNullException.ThrowIfNull(costR);
        if (netR.Count != costR.Count)
        {
            throw new ArgumentException("netR and costR must be the same length.", nameof(costR));
        }

        if (netR.Count < TrailingTrades)
        {
            return NotEvaluated("cost_stress", netR.Count, string.Create(CultureInfo.InvariantCulture, $"cost stress not evaluated: {netR.Count} graded trades, {TrailingTrades} are needed"));
        }

        var net1 = 0.0;
        var c = 0.0;
        for (var i = netR.Count - TrailingTrades; i < netR.Count; i++)
        {
            if (!double.IsFinite(netR[i]) || !double.IsFinite(costR[i]))
            {
                return NotEvaluated("cost_stress", TrailingTrades, "cost stress not evaluated: a trade has a figure that is not a number");
            }

            if (costR[i] < 0)
            {
                return NotEvaluated("cost_stress", TrailingTrades, "cost stress not evaluated: a trade has a negative cost (gross R below net R), so its cost is not a cost");
            }

            net1 += netR[i];
            c += costR[i];
        }

        return CostStress(net1 / TrailingTrades, c / TrailingTrades, TrailingTrades, tradesSinceLastFiring, stressMultiplier);
    }

    /// <summary>Rule (a) from the two means: <c>net(1.5) = net1 - 0.5 c</c>, fires when strictly below zero (a value within 1e-12 of zero is zero).</summary>
    public static KillRuleVerdict CostStress(double net1, double c, int trades, int? tradesSinceLastFiring = null, double stressMultiplier = StressMultiplier)
    {
        if (trades < TrailingTrades || !double.IsFinite(net1) || !double.IsFinite(c) || c < 0 || !double.IsFinite(stressMultiplier) || stressMultiplier < 1)
        {
            return NotEvaluated("cost_stress", trades, string.Create(CultureInfo.InvariantCulture, $"cost stress not evaluated: {trades} graded trades, {TrailingTrades} are needed, and the means must be numbers with a cost that is not negative"));
        }

        var stressed = net1 - ((stressMultiplier - 1) * c);
        var negative = stressed < -Tie;
        var text = string.Create(CultureInfo.InvariantCulture, $"net R at 1 times cost {net1:F4}, mean cost {c:F4} R, net R at {stressMultiplier:0.0#} times cost {stressed:F4} on the last {trades} trades");
        if (!negative)
        {
            return new KillRuleVerdict("cost_stress", KillRuleState.Holds, stressed, 0, trades, false, text + ": not below zero");
        }

        if (tradesSinceLastFiring is { } since && since < NewTradesBetweenFirings)
        {
            return new KillRuleVerdict("cost_stress", KillRuleState.Held, stressed, 0, trades, false, text + string.Create(CultureInfo.InvariantCulture, $": below zero, but only {since} new trades since it last fired and {NewTradesBetweenFirings} are needed, so it is not counted again"));
        }

        return new KillRuleVerdict("cost_stress", KillRuleState.Fires, stressed, 0, trades, false, text + ": below zero");
    }

    /// <summary>Rule (b): fires when the series' largest fall is strictly above <paramref name="factor"/> times <paramref name="percentile95AtOwnN"/>. The percentile must come from draws at the series' own n (a fall grows with n); null or not a number is not evaluated.</summary>
    public static KillRuleVerdict Drawdown(IReadOnlyList<double> results, double? percentile95AtOwnN, double factor = DrawdownFactor)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count == 0 || percentile95AtOwnN is not { } p || !double.IsFinite(p) || p <= 0 || !double.IsFinite(factor) || factor <= 0 || results.Any(r => !double.IsFinite(r)))
        {
            return NotEvaluated("drawdown", results.Count, "drawdown not evaluated: no trade, or no 95th percentile of the matched drawdown at the series' own n");
        }

        var fall = MonteCarlo.MaxDrawdown(results);
        var limit = factor * p;
        var fires = fall > limit + Tie;
        var text = string.Create(CultureInfo.InvariantCulture, $"largest fall {fall:F2} R over {results.Count} trades against {factor:0.##} times the 95th percentile {p:F2} R at that n = {limit:F2} R (the factor {factor:0.##} is a proposal)");
        return new KillRuleVerdict("drawdown", fires ? KillRuleState.Fires : KillRuleState.Holds, fall, limit, results.Count, false, text + (fires ? ": above" : ": not above"));
    }

    /// <summary>Rule (c): fires when the latest two completed decay windows are both below their thresholds; fewer than two completed windows is not evaluated.</summary>
    public static KillRuleVerdict DecayTwice(DecayReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Windows.Count < 2)
        {
            return NotEvaluated("decay_twice", report.Trades, string.Create(CultureInfo.InvariantCulture, $"decay twice in a row not evaluated: {report.Windows.Count} completed window(s), two are needed"));
        }

        var text = string.Create(CultureInfo.InvariantCulture, $"{report.Windows.Count} completed windows, {report.Streak} below their threshold in a row at the end");
        return new KillRuleVerdict("decay_twice", report.TwiceInARow ? KillRuleState.Fires : KillRuleState.Holds, report.Streak, 2, report.Trades, false, text);
    }

    /// <summary>Input (d): the CF-09 failure boundary. Fires only as a proposal (<see cref="KillRuleVerdict.ProposalOnly"/>); <paramref name="sequential"/> null (no series) is not evaluated.</summary>
    public static KillRuleVerdict FailureBoundary(SequentialState? sequential, double? sum, double boundary, int trades)
    {
        if (sequential is null || sum is null || !double.IsFinite(sum.Value) || trades < 1)
        {
            return NotEvaluated("failure_boundary", trades, "the CF-09 failure boundary is not evaluated: there is no series", proposalOnly: true);
        }

        var fires = sequential == SequentialState.Failure;
        var text = string.Create(CultureInfo.InvariantCulture, $"sequential sum {sum.Value:F2} against the failure boundary {boundary:F2} over {trades} trades (CF-09); a proposal only, never executed by an automatic setting");
        return new KillRuleVerdict("failure_boundary", fires ? KillRuleState.Fires : KillRuleState.Holds, sum.Value, boundary, trades, true, text);
    }

    /// <summary>A verdict for a rule whose input is missing, with the reason: never a pass and never a fail.</summary>
    public static KillRuleVerdict NotEvaluated(string rule, int trades, string text, bool proposalOnly = false) =>
        new(rule, KillRuleState.NotEvaluated, null, null, trades, proposalOnly, text);
}
