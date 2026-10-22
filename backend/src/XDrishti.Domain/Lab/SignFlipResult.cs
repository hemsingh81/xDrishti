namespace XDrishti.Domain.Lab;

/// <summary>The sign-flip baseline (spec P5-05, assumption 9): the observed mean R, the 95th percentile of the mean when signs are a coin flip, and the share of draws that reached the observed mean (with the observed draw counted).</summary>
public sealed record SignFlipResult(int Trades, int Draws, ulong Seed, double ObservedMeanR, double NullMeanP95, double PValue);
