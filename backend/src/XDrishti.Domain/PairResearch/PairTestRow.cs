namespace XDrishti.Domain.PairResearch;

/// <summary>One candidate's line of the formation table: the Engle-Granger statistic and p-value (or the reason there is none, counted in <c>m</c> with p = 1), its Benjamini-Hochberg rank and threshold, the half-life in sessions when it was asked for, and whether the pair was selected.</summary>
public sealed record PairTestRow(string Y, string X, double? Tau, double? PValue, string? Reason, int Rank, double Threshold, bool Rejected, double? HalfLife, string? HalfLifeReason, bool Selected, string? NotSelected);
