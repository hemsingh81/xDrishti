using System.Globalization;
using Xunit.v3;

[assembly: Xunit.TestClassOrderer(typeof(XDrishti.Api.IntegrationTests.Infrastructure.SeededClassOrderer))]

#nullable disable // the xunit interface declares its generic elements as nullable, which an explicit implementation cannot restate

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Shuffles the test classes of a collection when the environment variable <c>XD_TEST_SEED</c> holds an integer, the same way for the same seed, so an order-dependent
/// test (data one class leaves behind for the next) can be found and replayed. Without the variable the default order is kept.
/// </summary>
public sealed class SeededClassOrderer : ITestClassOrderer
{
    public const string SeedVariable = "XD_TEST_SEED";

    IReadOnlyCollection<TTestClass> ITestClassOrderer.OrderTestClasses<TTestClass>(IReadOnlyCollection<TTestClass> testClasses)
    {
        var text = Environment.GetEnvironmentVariable(SeedVariable);
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
        {
            return testClasses;
        }

        var random = new Random(seed);
        return [.. testClasses.OrderBy(c => c.TestClassName, StringComparer.Ordinal).OrderBy(_ => random.Next())];
    }
}
