namespace XDrishti.SimOracle;

/// <summary>Which partial exit the oracle walks (P4-02): none, <c>PARTIAL_BE</c> or <c>PARTIAL_TRAIL</c>.</summary>
public enum OraclePolicyKind
{
    None,
    Be,
    Trail,
}

/// <summary>
/// The exit policies of P4-02 as the oracle reads them from the rule tables (D1 to D9 of the spec and ADR 0050): the partial (T1 is the <c>RewardRisk</c> of the rules, the target of the position; <see cref="Book"/> is the share booked
/// there; <see cref="T2Rr"/> the second target of the break-even policy; <see cref="TrailMult"/> the multiple of the ATR of the trail) and the time stop (<see cref="MinProgressRr"/> null for none; <see cref="IntradayBars"/> for an
/// intraday walk, <see cref="SwingSessions"/> for a swing one). <see cref="Bars"/> are the closed bars of the evaluation timeframe the policies read, ascending by close instant.
/// </summary>
public sealed record OraclePolicy(
    OraclePolicyKind Kind,
    decimal Book,
    decimal T2Rr,
    decimal TrailMult,
    decimal? MinProgressRr,
    int IntradayBars,
    int SwingSessions,
    IReadOnlyList<OraclePolicyBar> Bars);
