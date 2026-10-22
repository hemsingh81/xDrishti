using System.Diagnostics;
using XDrishti.Strategies.File.Tests.Support;

namespace XDrishti.Strategies.File.Tests;

/// <summary>Criterion 10 of P3-02c, explicit (not part of <c>check.sh</c>): the median of 1,000 reads of the example, read plus map, is at most 1 ms. Run with <c>dotnet test --project tests/XDrishti.Strategies.File.Tests -c Release --filter-method "*Benchmark*" --explicit on</c>.</summary>
public sealed class BenchmarkTests
{
    [Fact(Explicit = true)]
    public void The_median_read_of_the_example_is_at_most_one_millisecond()
    {
        StrategyFile.Read(Example.Text);
        var times = new double[1000];
        for (var i = 0; i < times.Length; i++)
        {
            var start = Stopwatch.GetTimestamp();
            StrategyFile.Read(Example.Text);
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(times);
        times[500].ShouldBeLessThanOrEqualTo(1.0);
    }
}
