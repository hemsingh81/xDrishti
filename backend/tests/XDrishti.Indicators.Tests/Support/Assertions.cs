namespace XDrishti.Indicators.Tests.Support;

internal static class Assertions
{
    public static void ShouldBeNaN(this double actual, string? because = null) => double.IsNaN(actual).ShouldBeTrue($"expected NaN but was {actual:R}" + (because is null ? string.Empty : " (" + because + ")"));
}
