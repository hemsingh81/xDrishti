namespace XDrishti.Domain.Lab;

/// <summary>The stationary block bootstrap of the out-of-sample trades by session (CF-03): the observed mean and the spread over the draws. The mean of a draw is the sum of the R of the trades drawn over their count.</summary>
public sealed record BlockBootstrapResult(
    int Sessions,
    int Trades,
    int BlockLength,
    int Draws,
    ulong Seed,
    double ObservedMeanR,
    double MeanP05,
    double MeanP50,
    double MeanP95,
    double DrawdownP50,
    double DrawdownP95,
    double ShareNotPositive);
