using System.Globalization;
using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Lab;

/// <summary>
/// Turns out-of-sample numbers and a variant count into PASS, MARGINAL or FAIL with one reason per gate (spec P5-03, D2 to D4; doc 11 sections 4 and 5). Pure: no clock, no I/O, same input and same output.
/// Trades are judged against half the adjusted minimum (FAIL below it); every other gate fails hard when missed by more than 25 % of its adjusted threshold. A NaN number never passes and is a hard miss.
/// </summary>
public static class VerdictEvaluator
{
    /// <summary>A hard miss is a shortfall of more than this share of the adjusted threshold.</summary>
    public const double FailMargin = 0.25;

    /// <summary>The text of the limit for a method other than walk-forward.</summary>
    public const string ExplorationLimit = "exploration, not evidence";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <param name="gates">The configured thresholds.</param>
    /// <param name="evidence">The out-of-sample numbers.</param>
    /// <param name="variantCount">Variants already counted in the family, the one judged included; the caller passes 1 when the adjustment is off (<see cref="MultipleTesting.Effective"/>).</param>
    /// <param name="minFoldsForPass">Fewer folds than this cap the outcome at MARGINAL.</param>
    public static Verdict Evaluate(QualificationGates gates, OosEvidence evidence, int variantCount, int minFoldsForPass)
    {
        ArgumentNullException.ThrowIfNull(gates);
        ArgumentNullException.ThrowIfNull(evidence);
        var n = Math.Max(1, variantCount);
        var f = MultipleTesting.Factor(n);
        var results = new List<GateResult>(6);
        var hard = false;

        var minTrades = Math.Ceiling(Math.Round(gates.MinOosTrades * f, 9));
        var dep = evidence.Dependence;
        var gateOnEffective = dep is { Mode: ConfidenceMode.Gate };
        var tradesActual = (double)evidence.OosTrades;
        var effective = dep?.NEff is { } nEff ? DependenceEstimator.CeilN(nEff) : (int?)null;
        if (gateOnEffective && effective.HasValue)
        {
            tradesActual = effective.Value;   // CF-01 Gate mode (owner's switch): gate 1 compares effective n
        }

        // an unavailable effective n never passes: in Gate mode the gate is missed (and hard only by the plain count)
        var tradesOk = gateOnEffective && !effective.HasValue ? false : tradesActual >= minTrades;
        hard |= tradesActual < minTrades / 2;
        var tradesGate = Gate("oos_trades", gates.MinOosTrades, minTrades, tradesActual, tradesOk, ">=", n, 0);
        if (gateOnEffective)
        {
            tradesGate = tradesGate with { Reason = tradesGate.Reason + (effective.HasValue ? $"; effective n used instead of {evidence.OosTrades.ToString(Inv)} trades (Gate mode)" : $"; effective n unavailable, so the gate is not passed (Gate mode; {dep!.Note})") };
        }

        results.Add(tradesGate);

        var minExp = gates.MinExpectancyR * f;
        hard |= Min("expectancy_R", gates.MinExpectancyR, minExp, evidence.ExpectancyR, n, results);

        var minPf = 1 + ((gates.MinProfitFactor - 1) * f);
        hard |= Min("profit_factor", gates.MinProfitFactor, minPf, evidence.ProfitFactor, n, results);

        var pct = evidence.Folds > 0 ? 100.0 * evidence.PositiveFolds / evidence.Folds : 0;
        hard |= Min("positive_folds_pct", gates.MinPositiveFoldsPct, gates.MinPositiveFoldsPct, pct, n, results);

        hard |= Max("max_drawdown_R", gates.MaxDrawdownR, evidence.MaxDrawdownR, n, results);
        hard |= Max("calibration_error_pp", gates.MaxCalibrationErrorPp, evidence.CalibrationErrorPp, n, results);

        var limits = new List<string>(2);
        if (!string.Equals(evidence.Method, OosEvidence.WalkForward, StringComparison.Ordinal))
        {
            limits.Add($"{ExplorationLimit} (method {evidence.Method})");
        }

        if (evidence.Folds < minFoldsForPass)
        {
            limits.Add($"{evidence.Folds.ToString(Inv)} folds, fewer than the {minFoldsForPass.ToString(Inv)} needed for a pass");
        }

        var allPass = results.TrueForAll(r => r.Passed);
        var outcome = hard ? VerdictOutcome.Fail : allPass && limits.Count == 0 ? VerdictOutcome.Pass : VerdictOutcome.Marginal;
        return new Verdict(outcome, results, n, f, limits, NotesOf(dep, evidence.OosTrades, minTrades, gateOnEffective), dep is null ? null : dep with { EnoughByNEff = effective.HasValue ? effective.Value >= minTrades : null });
    }

    /// <summary>
    /// The PBO gate of CF-03 part B (ADR 0228). Only with <see cref="PboOptions.IsGate"/> (the owner's act) and a PASS: a PBO above <see cref="PboOptions.MaxPbo"/> (equal is allowed) caps it at MARGINAL with the reason. It never raises an outcome, never touches MARGINAL or
    /// FAIL, and a PBO that is unavailable (<paramref name="pbo"/> null, for example fewer variants than the minimum) changes nothing, as the spec's criterion 14 says. In <see cref="ConfidenceMode.ReportOnly"/> and Off the outcome is returned as it came.
    /// </summary>
    public static PboCap CapByPbo(VerdictOutcome outcome, PboOptions options, double? pbo)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsGate || outcome != VerdictOutcome.Pass || pbo is not { } p || !double.IsFinite(p) || !(p > options.MaxPbo))
        {
            return new PboCap(outcome, null);
        }

        return new PboCap(VerdictOutcome.Marginal, $"capped at MARGINAL by the probability of backtest overfitting {Num(p, 4)}, above the limit {Num(options.MaxPbo, 4)} (Gate mode)");
    }

    // CF-01: remarks only (never a limit, so the outcome is the one it was without them)
    private static List<string>? NotesOf(DependenceAssessment? dep, int trades, double minTrades, bool gateOnEffective)
    {
        if (dep is null || dep.Mode == ConfidenceMode.Off)
        {
            return null;
        }

        var notes = new List<string>(1);
        if (dep.NEff is not { } nEff)
        {
            notes.Add($"effective n unavailable ({(dep.Note.StartsWith("unavailable: ", StringComparison.Ordinal) ? dep.Note["unavailable: ".Length..] : dep.Note)}); {trades.ToString(Inv)} trades shown alone");
            return notes;
        }

        var effective = DependenceEstimator.CeilN(nEff);
        if (effective < minTrades)
        {
            var use = gateOnEffective
                ? "used in the verdict (Gate mode)"
                : effective < minTrades / 2 ? "not used in the verdict: it would read FAIL on trades if effective n were used" : "not used in the verdict: the trades gate would be missed if effective n were used";
            notes.Add($"{trades.ToString(Inv)} trades, effective {effective.ToString(Inv)} of {Num(minTrades, 0)} needed; {use}");
        }

        return notes;
    }

    /// <summary>A gate that needs at least <paramref name="adjusted"/>; true when missed by more than the margin.</summary>
    private static bool Min(string name, double required, double adjusted, double actual, int n, List<GateResult> results)
    {
        var ok = actual >= adjusted;
        results.Add(Gate(name, required, adjusted, actual, ok, ">=", n, 4));
        return double.IsNaN(actual) || actual < adjusted - (FailMargin * Math.Abs(adjusted));
    }

    /// <summary>A gate that allows at most <paramref name="threshold"/> (not tightened); true when missed by more than the margin.</summary>
    private static bool Max(string name, double threshold, double actual, int n, List<GateResult> results)
    {
        var ok = actual <= threshold;
        results.Add(Gate(name, threshold, threshold, actual, ok, "<=", n, 4));
        return double.IsNaN(actual) || actual > threshold + (FailMargin * Math.Abs(threshold));
    }

    private static GateResult Gate(string name, double required, double adjusted, double actual, bool passed, string op, int n, int digits)
    {
        var shown = passed ? "met" : "missed";
        var reason = $"{name} {Num(actual, digits)} {(passed ? op : Opposite(op))} {Num(adjusted, digits)} ({shown}; configured {Num(required, digits)}, adjusted for {n.ToString(Inv)} variant(s))";
        return new GateResult(name, required, adjusted, actual, passed, reason);
    }

    private static string Opposite(string op) => op == ">=" ? "<" : ">";

    private static string Num(double value, int digits) => value.ToString("0." + new string('#', Math.Max(digits, 0)), Inv);
}
