namespace XDrishti.Domain.PairResearch;

/// <summary>A pair kept for the quarter: the hedge ratio of the formation window (<c>alpha</c>, <c>beta</c>), the mean of its daily spread, its half-life in sessions and its test.</summary>
public sealed record SelectedPair(string Y, string X, double Alpha, double Beta, double SpreadMean, double HalfLifeSessions, double Tau, double PValue)
{
    /// <summary>The stored key, <c>Y|X</c>.</summary>
    public string Key => Y + "|" + X;
}
