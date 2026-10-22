namespace XDrishti.Domain.Lab;

/// <summary>The bootstrap of the out-of-sample trades (spec P5-05, assumption 6): the observed figures and the spread over the draws.</summary>
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
    double ShareNotPositive);
