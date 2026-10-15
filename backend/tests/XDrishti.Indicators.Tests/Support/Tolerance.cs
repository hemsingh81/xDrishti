namespace XDrishti.Indicators.Tests.Support;

/// <summary>1e-9 relative, 1e-9 absolute near zero; NaN equals NaN (not ready on both sides).</summary>
internal static class Tolerance
{
    public const double Epsilon = 1e-9;

    public static bool Same(double expected, double actual, double epsilon = Epsilon)
    {
        if (double.IsNaN(expected) || double.IsNaN(actual))
        {
            return double.IsNaN(expected) && double.IsNaN(actual);
        }

        return Math.Abs(expected - actual) <= epsilon * Math.Max(1.0, Math.Max(Math.Abs(expected), Math.Abs(actual)));
    }

    /// <summary>The same bits (determinism and the prefix property demand exact equality, not a tolerance).</summary>
    public static bool Identical(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
}
