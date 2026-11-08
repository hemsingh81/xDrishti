namespace XDrishti.Application.Lab;

/// <summary>
/// What conservative slippage left out of a Lab verdict (CF-05, ADR 0229): the candidates whose instrument was liquidity tier C or had no usable tier on the entry date have no trade, so the trades behind the verdict are the survivors of the filter and not a clean sample.
/// Present only on a run posted with <c>slippage: conservative</c>. Counts only; the candidates are those of the first (default) exit of every timeframe, which every exit candidate shares.
/// </summary>
/// <param name="Candidates">Candidates dated in the window, summed over the timeframes.</param>
/// <param name="TierExcluded">Candidates left out because the tier was C or unavailable.</param>
public sealed record TierExclusionView(long Candidates, long TierExcluded)
{
    /// <summary>The share of the candidates left out; 0 when there were none.</summary>
    public double Share => Candidates <= 0 ? 0 : (double)TierExcluded / Candidates;
}
