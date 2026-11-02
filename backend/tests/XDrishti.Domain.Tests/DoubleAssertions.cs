namespace XDrishti.Domain.Tests;

/// <summary>NaN assertion for the numbers of the extension features, where NaN means unknown (P15-05).</summary>
internal static class DoubleAssertions
{
    public static void ShouldBeNaN(this double value, string? customMessage = null)
    {
        if (!double.IsNaN(value))
        {
            throw new Shouldly.ShouldAssertException(customMessage ?? $"expected NaN but was {value:R}");
        }
    }
}
