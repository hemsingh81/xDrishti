using XDrishti.Domain.Shadow;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The sequential failure test of the paper edge series (spec CF-09, ADR 0213): a Gaussian-mean SPRT of H0 mean 0 against H1 mean <c>mu1</c>, used on the failure side only. The running sum of the log likelihood ratios is reflected at the on-track level;
/// reaching the caution or the failure boundary flags the series, and reaching the on-track level is a label. It never passes anything: the fixed gate of P10 stays the only pass. Each trade's term is divided by the design effect of its day (correlated
/// same-day candidates are discounted), and sigma is the configured one or, from <c>SigmaFromN</c> earlier trades, the larger sample standard deviation of those trades (a larger sigma only shrinks a step, so it delays a flag, never hastens it).
/// Pure: no clock, no random source, one pass over the series.
/// </summary>
public static class SequentialFailureTest
{
    /// <summary>The log likelihood ratio of one trade at sigma: <c>(mu1 / sigma^2) * (x - mu1 / 2)</c>.</summary>
    public static double Llr(double r, double mu1, double sigma) => mu1 / (sigma * sigma) * (r - (mu1 / 2));

    /// <summary>The sigma a step uses: the configured one, or the sample standard deviation of the <paramref name="earlierTrades"/> trades before it when there are at least <paramref name="sigmaFromN"/> of them and it is larger. Never below the configured sigma.</summary>
    public static double SigmaUsed(double configured, double sampleSd, int earlierTrades, int sigmaFromN) =>
        earlierTrades >= sigmaFromN && double.IsFinite(sampleSd) && sampleSd > configured ? sampleSd : configured;

    /// <summary>Runs the test over the series in order. <paramref name="rho"/> is the same-day correlation (0 to 1).</summary>
    public static SequentialResult Run(IReadOnlyList<SequentialTrade> series, SequentialOptions options, double rho)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Problems().Count > 0)
        {
            throw new ArgumentException("The sequential options are out of range: " + string.Join("; ", options.Problems()), nameof(options));
        }

        if (!double.IsFinite(rho) || rho is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rho), "The correlation must be between 0 and 1.");
        }

        double lower = options.CautionBoundary, failure = options.FailureBoundary, cap = options.OnTrackLevel;
        double sum = 0, effective = 0, sigma = options.Sigma, mean = 0, m2 = 0, lowest = 0;
        int? firstCaution = null, firstFailure = null;
        var lowestAt = 0;
        var path = new List<double>(series.Count);
        for (var i = 0; i < series.Count; i++)
        {
            var trade = series[i];
            if (!double.IsFinite(trade.R))
            {
                throw new ArgumentOutOfRangeException(nameof(series), "A trade's R must be a finite number.");
            }

            if (trade.DayCandidates < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(series), "A trade's day must have at least one candidate.");
            }

            // sigma known at this step: the trades before it (causal; no term is recomputed later)
            sigma = SigmaUsed(options.Sigma, i >= 2 ? Math.Sqrt(m2 / (i - 1)) : 0, i, options.SigmaFromN);
            var design = EdgeCount.DesignEffect(trade.DayCandidates, rho);
            sum = Math.Min(cap, sum + (Llr(trade.R, options.Mu1, sigma) / design));
            effective += 1 / design;
            path.Add(sum);

            var number = i + 1;
            if (sum <= lower)
            {
                firstCaution ??= number;
            }

            if (sum <= failure)
            {
                firstFailure ??= number;
            }

            if (number == 1 || sum < lowest)
            {
                lowest = sum;
                lowestAt = number;
            }

            var delta = trade.R - mean;
            mean += delta / number;
            m2 += delta * (trade.R - mean);
        }

        var state = firstFailure is not null ? SequentialState.Failure
            : sum <= lower ? SequentialState.Caution
            : sum >= cap ? SequentialState.OnTrack
            : SequentialState.Monitoring;
        return new SequentialResult(state, sum, lower, failure, cap, series.Count, effective, rho, sigma, firstCaution, firstFailure, lowest, lowestAt, path.AsReadOnly());
    }
}
