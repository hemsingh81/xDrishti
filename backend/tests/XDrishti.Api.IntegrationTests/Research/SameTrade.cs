using System.Collections;
using System.Reflection;
using XDrishti.Domain.Research;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>Equality of trades for the round-trip tests: by value (the records' own equality, the base lists element by element) and, for every <c>double</c> reached through the properties, by bit pattern, so that <c>-0.0</c> must come back as <c>-0.0</c>.</summary>
internal static class SameTrade
{
    public static void ShouldBeSame(IReadOnlyList<RunTrade> actual, IReadOnlyList<RunTrade> expected, string what)
    {
        actual.Count.ShouldBe(expected.Count, what);
        for (var i = 0; i < expected.Count; i++)
        {
            ShouldBeSame(actual[i], expected[i], $"{what}[{i}]");
        }
    }

    public static void ShouldBeSame(RunTrade actual, RunTrade expected, string what)
    {
        actual.Trade.ShouldBe(expected.Trade, what);
        actual.BasketIds.ToArray().ShouldBe(expected.BasketIds.ToArray(), what + " basket ids");
        Bits(actual.Trade, expected.Trade, what);
    }

    private static void Bits(object? actual, object? expected, string path)
    {
        if (actual is null || expected is null)
        {
            (actual is null).ShouldBe(expected is null, path);
            return;
        }

        if (actual is double a)
        {
            BitConverter.DoubleToInt64Bits(a).ShouldBe(BitConverter.DoubleToInt64Bits((double)expected), path);
            return;
        }

        if (actual is IEnumerable and not string && actual.GetType().Namespace?.StartsWith("XDrishti.Domain", StringComparison.Ordinal) == true)
        {
            // the legs and the charges of a trade: element by element, so that a double of a leg is compared by its bits too
            var left = ((IEnumerable)actual).Cast<object?>().ToList();
            var right = ((IEnumerable)expected).Cast<object?>().ToList();
            left.Count.ShouldBe(right.Count, path);
            for (var i = 0; i < left.Count; i++)
            {
                Bits(left[i], right[i], path + "[" + i + "]");
            }

            return;
        }

        var type = actual.GetType();
        if (type.Namespace?.StartsWith("XDrishti.Domain", StringComparison.Ordinal) != true || type.IsEnum || actual is IEnumerable)
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && p.Name is not ("Key" or "Start")))
        {
            Bits(property.GetValue(actual), property.GetValue(expected), path + "." + property.Name);
        }
    }
}
