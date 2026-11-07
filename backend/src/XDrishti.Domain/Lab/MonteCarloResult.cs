namespace XDrishti.Domain.Lab;

/// <summary>The bootstrap of the out-of-sample trades (spec P5-05, assumption 6): the observed figures and the spread over the draws. <c>DrawdownP90</c> (SC-S1, ADR 0222) is the 90th percentile of the same fall; the draws behind every other figure are unchanged by it.</summary>
public sealed record MonteCarloResult(
    int Trades,
    int Draws,
    ulong Seed,
    double ObservedMeanR,
    double ObservedMaxDrawdownR,
    double MeanP05,
    double MeanP50,
    double MeanP95,
    double DrawdownP50,
    double DrawdownP95,
    double ShareNotPositive,
    double? DrawdownP90 = null);
