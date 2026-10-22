namespace XDrishti.Domain.Lab;

/// <summary>The tightening of the gates as a family grows (spec P5-03, D2): <c>f = 1 + 0.25 * log2(N)</c>. A documented, tunable constant, not a statistical claim.</summary>
public static class MultipleTesting
{
    /// <summary>The weight of <c>log2(N)</c>.</summary>
    public const double PerDoubling = 0.25;

    /// <summary>The factor for <paramref name="variants"/>; fewer than one counts as one.</summary>
    public static double Factor(int variants) => 1 + (PerDoubling * Math.Log2(Math.Max(1, variants)));

    /// <summary>The variant count a verdict uses: the real count, or one when the adjustment is off.</summary>
    public static int Effective(int variants, bool enabled) => enabled ? Math.Max(1, variants) : 1;
}
