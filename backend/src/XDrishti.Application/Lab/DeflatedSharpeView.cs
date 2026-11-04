namespace XDrishti.Application.Lab;

/// <summary>
/// The deflated Sharpe ratio of the verdict (CF-03, ADR 0205), shown beside the heuristic factor and changing no outcome in <c>ReportOnly</c>. Per-trade units. Every figure is null when it could not be computed and <see cref="Note"/> then starts with
/// <c>unavailable: </c> and gives the reason; an unavailable figure is never a pass.
/// </summary>
/// <param name="Mode">The mode when it was computed.</param>
/// <param name="Trials">N: the variants whose statistics are stored (the ledger count when the owner assumed the spread).</param>
/// <param name="LedgerTrials">The variants the ledger counts for the family.</param>
/// <param name="TrialSrSd">sigma_SR: the standard deviation of the trial Sharpe ratios (or the owner's assumption).</param>
/// <param name="LuckBarSr">SR0: the Sharpe ratio luck alone is expected to reach among the trials.</param>
/// <param name="Sr">The judged variant's Sharpe ratio per trade (out-of-sample).</param>
/// <param name="NEff">T: the trades the probability rests on (the effective trades unless the basis is raw).</param>
/// <param name="Skew">Skewness of the out-of-sample R.</param>
/// <param name="Kurt">Kurtosis of the out-of-sample R.</param>
/// <param name="Probability">The deflated Sharpe ratio, 0 to 1.</param>
/// <param name="MinProbability">The level it is read against; it decides nothing in ReportOnly.</param>
/// <param name="Basis"><c>nEff</c> or <c>raw</c>: what T is.</param>
/// <param name="Settings">The settings that produced the figure.</param>
/// <param name="Note">The reading and the honest limits, or the unavailable reason.</param>
public sealed record DeflatedSharpeView(
    string Mode,
    int? Trials,
    int LedgerTrials,
    double? TrialSrSd,
    double? LuckBarSr,
    double? Sr,
    double? NEff,
    double? Skew,
    double? Kurt,
    double? Probability,
    double MinProbability,
    string Basis,
    string Settings,
    string Note);
