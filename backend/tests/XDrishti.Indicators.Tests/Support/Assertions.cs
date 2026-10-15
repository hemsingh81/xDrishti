namespace XDrishti.Indicators.Tests.Support;

internal static class Assertions
{
    public static void ShouldBeNaN(this double actual) => double.IsNaN(actual).ShouldBeTrue($"expected NaN but was {actual:R}");
}
