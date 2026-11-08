namespace XDrishti.Domain.Readiness;

/// <summary>The trades of one evidence set counted by liquidity tier (CF-05 D2). Counts only; no trade is named.</summary>
/// <param name="A">Trades in tier A.</param>
/// <param name="B">Trades in tier B.</param>
/// <param name="C">Trades in tier C.</param>
/// <param name="Unavailable">Trades whose tier could not be told (no usable 20-day median traded value, no bars, an instrument that does not resolve). They are not tier C.</param>
public sealed record TierCounts(int A, int B, int C, int Unavailable)
{
    /// <summary>All the trades of the set.</summary>
    public int Total => A + B + C + Unavailable;

    /// <summary>The text shown beside a hard fail, for example <c>A 3, B 2, C 0, unavailable 0</c>.</summary>
    public string Text => $"A {A}, B {B}, C {C}, unavailable {Unavailable}";
}
