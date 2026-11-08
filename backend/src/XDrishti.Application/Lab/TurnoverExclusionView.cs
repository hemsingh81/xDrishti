namespace XDrishti.Application.Lab;

/// <summary>
/// What the filter <c>min_turnover</c> left out of a Lab verdict (CF-05, ADR 0230): candidates whose 20-day median traded value was under the threshold or unknown have no trade, so the trades behind the verdict are the survivors of a liquidity filter, not a clean tier sample.
/// Present only for a strategy that names the filter (null otherwise, so the stored JSON of every other verdict is unchanged). Counts only; the candidates are those of the first exit of every timeframe, which every exit candidate shares.
/// </summary>
/// <param name="Candidates">Candidates dated in the window, summed over the timeframes.</param>
/// <param name="Blocked">Candidates blocked by the filter (under the threshold or unknown).</param>
/// <param name="ThresholdCrore">The threshold the run used, in crore rupees.</param>
public sealed record TurnoverExclusionView(long Candidates, long Blocked, decimal ThresholdCrore)
{
    /// <summary>The share of the candidates blocked; 0 when there were none.</summary>
    public double Share => Candidates <= 0 ? 0 : (double)Blocked / Candidates;
}
