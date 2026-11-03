namespace XDrishti.Domain.Pairs;

/// <summary>The Benjamini-Hochberg decision: whether each test (by input index) is rejected, the number rejected (<c>K</c>) and the number of tests counted (<c>M</c>, unavailable tests included).</summary>
public sealed record BhOutcome(IReadOnlyList<bool> Rejected, int K, int M);
