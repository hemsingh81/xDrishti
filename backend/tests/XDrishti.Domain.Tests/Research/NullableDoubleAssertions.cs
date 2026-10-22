namespace XDrishti.Domain.Tests.Research;

/// <summary>Shouldly compares a nullable double only to another nullable; the tables of the comparison report are checked to a tolerance.</summary>
internal static class NullableDoubleAssertions
{
    public static void ShouldBe(this double? actual, double expected, double tolerance)
    {
        actual.ShouldNotBeNull();
        actual.Value.ShouldBe(expected, tolerance);
    }
}
