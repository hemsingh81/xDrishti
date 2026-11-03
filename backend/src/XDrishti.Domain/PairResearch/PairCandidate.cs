namespace XDrishti.Domain.PairResearch;

/// <summary>A candidate pair of one formation: <c>y</c> is the member with the larger 20-session mean turnover at the window end. The indices point into the member list.</summary>
public sealed record PairCandidate(string Y, string X, int YIndex, int XIndex, double Correlation)
{
    /// <summary>The stored key, <c>Y|X</c>.</summary>
    public string Key => Y + "|" + X;
}
