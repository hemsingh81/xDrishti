using System.Diagnostics;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Context;

/// <summary>
/// Criterion 13 for the context itself: building a <see cref="MarketContext"/> of 5,000 days takes at most 50 ms (the spec target; it measures a few milliseconds, so the bound is not a close call), appending the same
/// days one by one costs no more than ten times that (the per-day work is the same; only the sizing of the storage differs), and a read by date neither allocates nor needs more than a binary search. The tests run
/// alone (<see cref="ExclusiveRun"/>) so that no parallel test lands in the measurement window; each timing is taken after a warm-up and the best of five runs counts.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class ContextPerformanceTests
{
    private const int Days = 5_000;

    private const int Repeats = 5;

    private static readonly Lazy<List<ContextDay>> LazyDays = new(() => ContextSeriesGenerator.Random(7, Days, Days));

    [Fact]
    public void Building_five_thousand_days_with_the_default_options_takes_at_most_fifty_milliseconds()
    {
        var days = LazyDays.Value;
        days.Count.ShouldBe(Days);
        MarketContext.Create(days).IsSuccess.ShouldBeTrue();   // warm-up: the JIT compiles the paths first

        var best = BestMilliseconds(() =>
        {
            var result = MarketContext.Create(days);
            result.IsSuccess.ShouldBeTrue();
            result.Value.Count.ShouldBe(Days);
        });

        TestContext.Current.TestOutputHelper?.WriteLine($"MarketContext.Create of {Days} days: best of {Repeats} = {best:F2} ms");
        best.ShouldBeLessThanOrEqualTo(50, $"target: at most 50 ms for {Days} days (measured {best:F2} ms)");
    }

    [Fact]
    public void Appending_day_by_day_costs_at_most_ten_times_what_building_in_one_call_costs()
    {
        var days = LazyDays.Value;
        MarketContext.Create(days).IsSuccess.ShouldBeTrue();   // warm-up of both paths, so that no JIT time is in either figure
        AppendAll(days).Count.ShouldBe(Days);

        var built = BestMilliseconds(() => MarketContext.Create(days).IsSuccess.ShouldBeTrue());
        var appended = BestMilliseconds(() => AppendAll(days).Count.ShouldBe(Days));

        TestContext.Current.TestOutputHelper?.WriteLine($"MarketContext of {Days} days, best of {Repeats}: Create {built:F2} ms, Append one by one {appended:F2} ms");
        appended.ShouldBeLessThanOrEqualTo(10 * built, $"Append one by one measured {appended:F2} ms against {built:F2} ms for Create; both run the same per-day work, only the sizing of the storage differs");
    }

    [Fact]
    public void Reading_by_date_allocates_nothing_with_or_without_a_cursor()
    {
        var days = LazyDays.Value;
        var context = MarketContext.Create(days).Value;
        var from = days[0].Date;
        var cursor = 0;

        for (var warm = 0; warm < 2; warm++)
        {
            Read(context, from, ref cursor);
        }

        var sum = 0;
        AllocationProbe.ShouldAllocateNothing(window =>
        {
            window.Begin();
            sum = Read(context, from, ref cursor);
            window.End();
        });

        sum.ShouldBeGreaterThan(0);
    }

    private static MarketContext AppendAll(List<ContextDay> days)
    {
        var context = MarketContext.Create([]).Value;
        foreach (var day in days)
        {
            context.Append(day).IsSuccess.ShouldBeTrue();
        }

        return context;
    }

    /// <summary>The fastest of <see cref="Repeats"/> runs, in milliseconds.</summary>
    private static double BestMilliseconds(Action run)
    {
        var best = double.MaxValue;
        for (var i = 0; i < Repeats; i++)
        {
            var started = Stopwatch.GetTimestamp();
            run();
            best = Math.Min(best, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        return best;
    }

    private static int Read(MarketContext context, DateOnly from, ref int cursor)
    {
        var found = 0;
        for (var day = 0; day < 8_000; day++)
        {
            if (context.TryAsOf(from.AddDays(day), ref cursor, out _))
            {
                found++;
            }

            if (context.TryAsOf(from.AddDays(day), out _))
            {
                found++;
            }
        }

        return found;
    }
}
