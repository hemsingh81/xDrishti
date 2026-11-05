using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>The fixed sentences of the sequential test (spec CF-09, criterion 12; ADR 0213). A test pins them. The words of a state never say the strategy is good: the state texts hold no claim of that kind.</summary>
public static class SequentialWording
{
    /// <summary>Said with every response: the flag is not a decision and the fixed gates decide.</summary>
    public const string ReportOnly = "report only, never a pass: the fixed gates of P10 still decide a pass; this flag shortens no calendar gate and changes no P10 number";

    /// <summary>Said when the figures are modelled shadow fills.</summary>
    public const string ModelledFills = "modelled fills; no queue, no latency";

    /// <summary>The words of a state.</summary>
    public static string StateText(SequentialState state) => state switch
    {
        SequentialState.Monitoring => "monitoring: the evidence does not yet favour either side",
        SequentialState.OnTrack => "on track so far (a label that can revert): the evidence favours the stated alternative over a mean of 0, nothing more",
        SequentialState.Caution => "caution: the evidence now favours a mean near 0 over the stated alternative at the first odds level",
        SequentialState.Failure => "failure: the evidence favoured a mean near 0 over the stated alternative at the second odds level, and the flag stays for this epoch until the epoch restarts, a setting changes or the cost basis of the series changes (a confirmation, a new or unconfirmed charge sheet, a grade without charges, each of which can flip it between gross and net R)",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <summary>The sentences every response carries: the model, the odds with the cumulative false-failure bound, what the indifference point is, the same-day discount with the correlation shown, and that the fixed gates decide.</summary>
    public static IReadOnlyList<string> Statements(SequentialOptions o, double rho, bool rhoIsPlaceholder)
    {
        ArgumentNullException.ThrowIfNull(o);
        var ci = CultureInfo.InvariantCulture;
        var later = Math.Exp(o.FailureBoundary - o.OnTrackLevel) * 100;
        return
        [
            string.Create(ci, $"Gaussian model of the mean net R per independent trade: a mean of 0 against {o.Mu1:0.###} R, sigma {o.Sigma:0.###} R (never lower; raised to the sample standard deviation of the earlier trades from {o.SigmaFromN} trades, which only delays a flag)."),
            string.Create(ci, $"Odds: caution at an evidence ratio of {o.CautionOdds:0.##} to 1 and failure at {o.FailureOdds:0.##} to 1 in favour of a mean near 0. A strategy whose true mean is {o.Mu1:0.###} R is wrongly flagged caution at most {o.BetaCaution / (1 - o.Alpha) * 100:0.#} % of the time and failure at most {o.BetaFail / (1 - o.Alpha) * 100:0.#} %, then about {later:0.00} % per later return to the cap (Gaussian approximation)."),
            string.Create(ci, $"The indifference point is mu1 / 2 = {o.Mu1 / 2:0.###} R: the evidence favours a mean near 0 over {o.Mu1:0.###} R at the stated odds; it does not say there is no edge."),
            string.Create(ci, $"Candidates of one day are correlated, so each trade counts for 1 over the design effect 1 + (m - 1) x rho of its day, with rho {rho:0.00}") + (rhoIsPlaceholder ? " (a placeholder until CF-01 estimates it)." : " (estimated)."),
            ReportOnly + ".",
        ];
    }
}
