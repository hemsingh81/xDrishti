namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>A rate with its sample and its Wilson 95 % interval (spec P9-09 AC 9: every rate is printed with its interval).</summary>
public sealed record RateLine(string Name, int K, int N, double Low, double High)
{
    public static RateLine Of(string name, int k, int n)
    {
        var (low, high) = Wilson.Interval(k, n);
        return new RateLine(name, k, n, low, high);
    }
}
