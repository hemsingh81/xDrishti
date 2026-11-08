using System.Globalization;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>One variant of the sweep for PBO: who it is and the (exit date, net R) of each of its trades over the run's whole window.</summary>
internal sealed record PboVariant(string Label, IReadOnlyList<(DateOnly Date, double R)> Trades);

/// <summary>
/// The probability of backtest overfitting of a robust run's sweep (CF-03 part B, ADR 0228): the variants are the base run and the sweep points, each as a daily series of net R by exit date aligned on the union of those dates, cut into blocks by <see cref="Cscv"/>.
/// <see cref="Compute"/> reads nothing and writes nothing; <see cref="Apply"/> is the Gate: only with the owner's Gate mode, a PASS whose PBO is above the limit becomes MARGINAL with the reason, nothing else changes. In ReportOnly the verdict is returned as it came.
/// </summary>
internal static class LabPbo
{
    /// <summary>Printed with every figure.</summary>
    public const string LimitsText = "The variants are the base run and one-parameter-at-a-time neighbours of it, not independent strategies: PBO says whether the best of these holds out of sample, not whether the strategy family has an edge. It is diagnostic: no variant is chosen by it. With about two years of data each block holds few sessions, and a shorter window makes PBO noisy.";

    /// <summary>The figure and the series it came from, or nothing when the mode is Off.</summary>
    /// <param name="options">The settings.</param>
    /// <param name="variants">The variants that ran at least one trade, base first.</param>
    /// <param name="left">How many runs of the base and the sweep were left out because they ran no trade or were refused.</param>
    public static (PboView? View, VariantSeriesView? Series) Compute(PboOptions options, IReadOnlyList<PboVariant> variants, int left)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(variants);
        if (!options.IsOn)
        {
            return (null, null);
        }

        PboView Make(int count, int splits, double? pbo, double? median, string note) =>
            new(options.Mode.ToString(), count, options.Blocks, splits, pbo, median, options.MaxPbo, options.Metric.ToString(), false, options.KeyText(), note);

        var leftText = left > 0 ? left.ToString(CultureInfo.InvariantCulture) + " run(s) of the base and the sweep ran no trade or were refused and are not variants here. " : string.Empty;
        if (variants.Count == 0)
        {
            return (Make(0, 0, null, null, "unavailable: no variant ran a trade. " + leftText + LimitsText), null);
        }

        var aligned = AlignedSeries.Align([.. variants.Select(v => v.Trades)]);
        if (aligned.Unavailable is { } why)
        {
            return (Make(variants.Count, 0, null, null, why + ". " + leftText + LimitsText), null);
        }

        var series = new VariantSeriesView(aligned.Dates, [.. variants.Select((v, i) => new VariantSeriesRowView(v.Label, aligned.Values[i]))]);
        if (variants.Count < options.MinVariants)
        {
            return (Make(variants.Count, 0, null, null, $"unavailable: needs at least {options.MinVariants.ToString(CultureInfo.InvariantCulture)} variants (have {variants.Count.ToString(CultureInfo.InvariantCulture)}). {leftText}{LimitsText}"), series);
        }

        var result = Cscv.Pbo(aligned.Values, options.Blocks, options.Metric);
        if (result.Unavailable is { } reason)
        {
            return (Make(variants.Count, 0, null, null, reason + ". " + leftText + LimitsText), series);
        }

        var pbo = result.Pbo!.Value;
        var reading = pbo > options.MaxPbo ? "above" : "at or below";
        var inv = CultureInfo.InvariantCulture;
        var note = $"PBO {pbo.ToString("0.####", inv)} over {variants.Count.ToString(inv)} variants, {options.Blocks.ToString(inv)} blocks, {result.Splits.ToString(inv)} splits, scored by {options.Metric.ToString().ToLowerInvariant()} of the daily R: in that share of splits the in-sample best variant fell to or below the out-of-sample median; "
            + $"median logit {result.MedianLogit!.Value.ToString("0.####", inv)}; {reading} the {options.MaxPbo.ToString("0.####", inv)} limit ({(options.IsGate ? "Gate mode: a PASS above it is capped at MARGINAL" : "informational: no gate uses it")}). {leftText}{LimitsText}";
        return (Make(variants.Count, result.Splits, pbo, result.MedianLogit, note), series);
    }

    /// <summary>The verdict after the Gate: unchanged unless the mode is Gate, the outcome is PASS and the PBO is above the limit.</summary>
    public static LabVerdictView Apply(PboOptions options, LabVerdictView verdict)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(verdict);
        if (!options.IsGate || !string.Equals(verdict.Outcome, "PASS", StringComparison.Ordinal) || verdict.Robustness?.Pbo is not { Pbo: { } pbo } view)
        {
            return verdict;
        }

        var cap = VerdictEvaluator.CapByPbo(VerdictOutcome.Pass, options, pbo);
        return cap.Reason is null
            ? verdict
            : verdict with { Outcome = "MARGINAL", Limits = [.. verdict.Limits, cap.Reason], Robustness = verdict.Robustness with { Pbo = view with { Capped = true } } };
    }
}
