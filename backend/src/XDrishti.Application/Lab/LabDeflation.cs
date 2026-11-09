using System.Globalization;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Confidence;

namespace XDrishti.Application.Lab;

/// <summary>
/// The deflated Sharpe ratio of a Lab verdict (CF-03, ADR 0205), computed after the robustness stage so the sweep points are among the trials. N is the number of variants of the family whose statistics are stored (the latest row of each variant hash, this run's rows replacing
/// stored ones), <c>sigma_SR</c> the sample standard deviation of their Sharpe ratios per trade, T the effective trades of CF-01 (or the raw count by setting). Reads only; nothing is written here. Nothing in the verdict reads the result.
/// </summary>
internal sealed class LabDeflation(ITestLedger ledger, IVariantStatsStore store, DeflationOptions options)
{
    /// <summary>The share of the ledger's variants whose statistics must be stored (review fix 2).</summary>
    public const double MinCoverage = 0.8;

    /// <summary>Printed with every figure: why it is conservative and why it can be too lenient.</summary>
    public const string LimitsText = "N counts neighbouring variants as independent: the bar is higher than strictly needed. The formulas rest on normal-theory approximations and on a trial count that is not the number of truly independent ideas; unrecorded hand edits and strategies never logged make it too lenient. The judged out-of-sample trades are the walk-forward winners of an exit selection whose candidate exits are not ledger variants, so they are not in N (lenient in that respect). The trial Sharpe ratios mix the out-of-sample variant with in-sample sweep points.";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The figure for the run, or null when the mode is Off.</summary>
    /// <param name="strategyId">The family.</param>
    /// <param name="verdict">The verdict, for the effective trades.</param>
    /// <param name="oosResults">R of the out-of-sample trades the verdict was judged on.</param>
    /// <param name="runRows">The statistics of this run's variants (the judged variant and the sweep points), not yet stored.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async ValueTask<DeflatedSharpeView?> ComputeAsync(string strategyId, LabVerdictView verdict, IReadOnlyList<double> oosResults, IReadOnlyList<VariantStatsRow> runRows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        ArgumentNullException.ThrowIfNull(oosResults);
        ArgumentNullException.ThrowIfNull(runRows);
        if (!options.IsOn)
        {
            return null;
        }

        var moments = TradeMoments.Of(oosResults);
        var ledgerHashes = (await ledger.ListAsync(strategyId, cancellationToken)).Where(r => !r.IsTrainingSweep).Select(r => r.StrategyHash).ToHashSet(StringComparer.Ordinal);
        ledgerHashes.UnionWith(runRows.Select(r => r.VariantHash));
        var latest = new Dictionary<string, VariantStatsRow>(StringComparer.Ordinal);
        foreach (var stored in (await store.ListAsync(strategyId, cancellationToken)).OrderBy(r => r.CreatedAt))
        {
            latest[stored.VariantHash] = stored;
        }

        foreach (var row in runRows)
        {
            latest[row.VariantHash] = row;
        }

        var trials = latest.Values.Where(r => r.SrPerTrade is { } sr && double.IsFinite(sr)).Select(r => r.SrPerTrade!.Value).ToList();
        var ledgerCount = ledgerHashes.Count;
        var raw = options.TBasis == DeflationBasis.Raw;
        var t = raw ? moments.N : verdict.Evidence.Dependence?.NEff;
        DeflatedSharpeView Missing(string reason, int? n = null, double? sigma = null, double? bar = null) =>
            new DeflatedSharpeView(options.Mode.ToString(), n, ledgerCount, sigma, bar, Finite(moments.Sr), t, Finite(moments.Skew), Finite(moments.Kurt), null, options.MinProbability, raw ? "raw" : "nEff", options.KeyText(), "unavailable: " + reason + ". " + LimitsText);

        int count;
        double sigma;
        string source;
        if (options.AssumedTrialSrSd is { } assumed)
        {
            count = ledgerCount;
            sigma = assumed;
            source = "the spread of the trial Sharpe ratios is the owner's assumption (" + assumed.ToString("0.####", Inv) + ")";
        }
        else
        {
            count = trials.Count;
            if (count < 2)
            {
                return Missing("trial Sharpe ratios not stored (fewer than 2)", count);
            }

            if (count < MinCoverage * ledgerCount)
            {
                return Missing($"trial statistics stored for only {count.ToString(Inv)} of {ledgerCount.ToString(Inv)} variants", count);
            }

            var mean = trials.Average();
            sigma = Math.Sqrt(trials.Sum(s => (s - mean) * (s - mean)) / (count - 1));
            source = "the spread of the trial Sharpe ratios is from " + count.ToString(Inv) + " stored variants";
        }

        if (t is null)
        {
            return Missing("effective trades unavailable" + (verdict.Evidence.Dependence is { } d ? " (" + d.Note.Replace("unavailable: ", string.Empty, StringComparison.Ordinal) + ")" : " (effective n is off)"), count, sigma);
        }

        var bar = DeflatedSharpe.ExpectedMax(count, sigma);
        var dsr = DeflatedSharpe.Deflated(moments.Sr, count, sigma, t.Value, moments.Skew, moments.Kurt);
        if (dsr.Value is not { } p)
        {
            return Missing(dsr.Unavailable!["unavailable: ".Length..], count, sigma, Finite(bar));
        }

        var reading = p >= options.MinProbability ? "at or above" : "below";
        var note = $"deflated Sharpe ratio {p.ToString("0.####", Inv)} is {reading} the {options.MinProbability.ToString("0.###", Inv)} reading level (informational: no gate uses it); the luck bar is {bar.ToString("0.####", Inv)} per trade over {count.ToString(Inv)} trials; {source}; T = {t.Value.ToString("0.#", Inv)} {(raw ? "raw" : "effective")} trades. {LimitsText}";
        return new DeflatedSharpeView(options.Mode.ToString(), count, ledgerCount, sigma, bar, moments.Sr, t, Finite(moments.Skew), Finite(moments.Kurt), p, options.MinProbability, raw ? "raw" : "nEff", options.KeyText(), note);
    }

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;
}
