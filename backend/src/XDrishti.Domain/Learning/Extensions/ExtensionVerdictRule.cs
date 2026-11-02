using System.Globalization;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// The verdict table of the comparison (P13-05). NOT_EVALUABLE when folds, paired candidates or an interval are missing; KEEP when every criterion holds (margins widened by the multiple-testing factor); DROP when it is not a KEEP and even the upper bound stays below the minimum gain
/// (not widened); INCONCLUSIVE otherwise. An <c>unavailable</c> input is never a pass: it fails KEEP and it does not support DROP.
/// </summary>
public static class ExtensionVerdictRule
{
    /// <summary>Applies the table.</summary>
    public static ExtensionVerdictResult Decide(ExtensionVerdictInput i, ExtensionComparisonOptions o)
    {
        ArgumentNullException.ThrowIfNull(i);
        ArgumentNullException.ThrowIfNull(o);
        var c = CultureInfo.InvariantCulture;
        var missing = new List<string>();
        if (i.EvaluableFolds < o.MinEvaluableFolds)
        {
            missing.Add(string.Create(c, $"folds {i.EvaluableFolds} of {o.MinEvaluableFolds}"));
        }

        if (i.PairedCandidates < o.MinPairedCandidates)
        {
            missing.Add(string.Create(c, $"paired candidates {i.PairedCandidates:N0} of {o.MinPairedCandidates:N0}"));
        }

        if (i.Lower is null)
        {
            missing.Add("lower bound unavailable");
        }

        if (i.SensitivityUsed && i.LowerSensitivity is null)
        {
            missing.Add("lower bound of the sensitivity block unavailable");
        }

        if (missing.Count > 0)
        {
            return new ExtensionVerdictResult(ExtensionVerdict.NotEvaluable, missing, []);
        }

        var gain = o.MinGainR * i.Factor;
        var failed = new List<string>();
        if (o.MinFlips > 0 && i.Flips < o.MinFlips)
        {
            failed.Add(string.Create(c, $"flips {i.Flips:N0} of {o.MinFlips:N0}"));
        }

        if (!(i.Lower >= gain))
        {
            failed.Add(string.Create(c, $"lower bound {Fmt(i.Lower)} R below {gain:F3} R"));
        }

        if (i.SensitivityUsed && !(i.LowerSensitivity >= gain))
        {
            failed.Add(string.Create(c, $"lower bound of the sensitivity block {Fmt(i.LowerSensitivity)} R below {gain:F3} R"));
        }

        var needed = (int)Math.Ceiling((i.EvaluableFolds * o.PositiveFoldShare) - 1e-9);
        if (i.PositiveFolds < needed)
        {
            failed.Add(string.Create(c, $"gain above zero in {i.PositiveFolds} of {i.EvaluableFolds} folds, {needed} needed"));
        }

        if (!(i.DeltaLogLoss <= o.MaxDeltaLogLoss * i.Factor))
        {
            failed.Add(string.Create(c, $"change in log loss {Fmt(i.DeltaLogLoss)} not at most {o.MaxDeltaLogLoss * i.Factor:F4}"));
        }

        if (o.MaxDeltaBrier is { } brier && !(i.DeltaBrier <= brier * i.Factor))
        {
            failed.Add(string.Create(c, $"change in Brier score {Fmt(i.DeltaBrier)} not at most {brier * i.Factor:F4}"));
        }

        if (!(i.EceBPp <= o.MaxEcePp))
        {
            failed.Add(string.Create(c, $"calibration error {Fmt(i.EceBPp)} pp above {o.MaxEcePp:F1} pp"));
        }

        if (failed.Count == 0)
        {
            return new ExtensionVerdictResult(ExtensionVerdict.Keep, [], []);
        }

        var upper = i.Upper is { } u && (!i.SensitivityUsed || i.UpperSensitivity is not null) ? Math.Max(u, i.UpperSensitivity ?? u) : (double?)null;
        return new ExtensionVerdictResult(upper is { } top && top < o.MinGainR ? ExtensionVerdict.Drop : ExtensionVerdict.Inconclusive, [], failed);
    }

    private static string Fmt(double? v) => v is { } x ? x.ToString("F4", CultureInfo.InvariantCulture) : "unavailable";
}
