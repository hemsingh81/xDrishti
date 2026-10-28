namespace XDrishti.Domain.LiveTrading;

/// <summary>The statistics of the paired shortfalls: mean, sample sd, standard error, the two-sided 95 % interval and the one-sided 95 % upper and lower bounds of the mean, and the non-inferiority verdict.</summary>
public sealed record ShortfallSummary(int N, double Mean, double Sd, double Se, double Lo95, double Hi95, double UpperOneSided, double LowerOneSided, string Verdict);
