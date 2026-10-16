using System.Runtime;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// Self-test of <see cref="AllocationProbe"/>, the measurement under every "allocates nothing" test. It must see every allocation (nothing is tolerated), must not be fooled by a garbage collection that another thread
/// starts inside the window (the thread allocation counter then jumps by up to one allocation quantum without any allocation of the measured code: this was what turned the weekly and monthly allocation tests red at
/// random), and must always leave the GC in its normal mode. The tests run alone (<see cref="ExclusiveRun"/>): they count collections and start no-GC regions.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class AllocationProbeTests
{
    private const int Steps = 100_000;

    private const int WarmUpSteps = 20_000;

    /// <summary>Gap-free, time-ordered bars: 330 sessions of 375 minutes, enough for warm-up plus a window of 100,000 steps.</summary>
    private static readonly Bar[] Bars = [.. SeriesGenerator.Sessions(99, 330)];

    [Fact]
    public void A_window_that_allocates_nothing_reports_zero_at_the_first_attempt()
    {
        var sum = 0L;

        var (allocated, _, attempts) = AllocationProbe.Measure(window =>
        {
            window.Begin();
            for (var i = 0; i < Steps; i++)
            {
                sum += i;
            }

            window.End();
        });

        allocated.ShouldBe(0);
        attempts.ShouldBe(1, "zero bytes are final at once, with or without a collection");
        sum.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_window_that_allocates_in_every_step_reports_the_bytes()
    {
        object? keep = null;
        var runs = 0;

        var (allocated, disturbed, attempts) = AllocationProbe.Measure(window =>
        {
            runs++;
            window.Begin();
            for (var i = 0; i < 1_000; i++)
            {
                keep = new byte[16];
            }

            window.End();
        });

        allocated.ShouldBeGreaterThanOrEqualTo(16_000);
        attempts.ShouldBe(runs);
        (!disturbed || attempts == AllocationProbe.MaxAttempts).ShouldBeTrue("an attempt without a collection is final at once; a disturbed one is final only after the last attempt");
        GC.KeepAlive(keep);
    }

    [Fact]
    public void One_object_in_a_hundred_thousand_steps_is_reported_and_fails_the_assertion()
    {
        object? keep = null;
        var sum = 0L;
        void Scenario(AllocationWindow window)
        {
            window.Begin();
            for (var i = 0; i < Steps; i++)
            {
                sum += i;
                if (i == Steps / 2)
                {
                    keep = new byte[24];
                }
            }

            window.End();
        }

        var (allocated, _, _) = AllocationProbe.Measure(Scenario);

        allocated.ShouldBeGreaterThanOrEqualTo(24, "no tolerance: a single object is seen");
        Should.Throw<ShouldAssertException>(() => AllocationProbe.ShouldAllocateNothing(Scenario, "one object")).Message.ShouldContain("one object");
        GC.KeepAlive(keep);
    }

    [Fact]
    public void An_attempt_with_bytes_and_a_collection_is_run_again_and_the_clean_attempt_counts()
    {
        object? keep = null;
        var runs = 0;

        var (allocated, _, attempts) = AllocationProbe.Measure(window =>
        {
            runs++;
            var first = runs == 1;
            window.Begin();
            if (first)
            {
                keep = new byte[100];
                GC.Collect();
            }

            window.End();
        });

        allocated.ShouldBe(0);
        attempts.ShouldBeGreaterThanOrEqualTo(2, "the disturbed first attempt is not final (a third one only if a neighbour's collection also landed in the clean attempt)");
        runs.ShouldBe(attempts);
        GC.KeepAlive(keep);
    }

    [Fact]
    public void A_window_that_shows_bytes_and_a_collection_in_every_attempt_is_not_reported_as_clean()
    {
        object? keep = null;
        var runs = 0;
        void Scenario(AllocationWindow window)
        {
            runs++;
            window.Begin();
            keep = new byte[100];
            GC.Collect();
            window.End();
        }

        var (allocated, disturbed, attempts) = AllocationProbe.Measure(Scenario);

        allocated.ShouldBeGreaterThan(0);
        disturbed.ShouldBeTrue();
        attempts.ShouldBe(AllocationProbe.MaxAttempts);
        runs.ShouldBe(AllocationProbe.MaxAttempts);
        Should.Throw<ShouldAssertException>(() => AllocationProbe.ShouldAllocateNothing(Scenario, "always disturbed")).Message.ShouldContain("garbage collection");
        GC.KeepAlive(keep);
    }

    [Fact]
    public void A_window_that_a_collection_ran_in_is_flagged_even_without_bytes()
    {
        var window = new AllocationWindow();

        window.Begin();
        GC.Collect();
        window.End();

        window.Disturbed.ShouldBeTrue();
        window.Completed.ShouldBeTrue();
    }

    [Fact]
    public void A_scenario_that_throws_inside_the_window_leaves_the_garbage_collector_in_its_normal_mode()
    {
        Should.Throw<InvalidOperationException>(() => AllocationProbe.Measure(window =>
        {
            window.Begin();
            throw new InvalidOperationException("thrown inside the window");
        }));

        GCSettings.LatencyMode.ShouldNotBe(GCLatencyMode.NoGCRegion);
        Should.NotThrow(() => AllocationProbe.Measure(window =>
        {
            window.Begin();
            window.End();
        }));
        GCSettings.LatencyMode.ShouldNotBe(GCLatencyMode.NoGCRegion);
    }

    [Fact]
    public void A_scenario_that_does_not_close_its_window_is_refused()
    {
        Should.Throw<InvalidOperationException>(() => AllocationProbe.Measure(_ => { })).Message.ShouldContain("Begin and End");
        Should.Throw<InvalidOperationException>(() => AllocationProbe.Measure(window => window.End())).Message.ShouldContain("not begun");
        Should.Throw<InvalidOperationException>(() => AllocationProbe.Measure(window =>
        {
            window.Begin();
            window.Begin();
        })).Message.ShouldContain("open already");
        GCSettings.LatencyMode.ShouldNotBe(GCLatencyMode.NoGCRegion);
    }

    [Fact]
    public void The_weekly_and_monthly_builders_stay_green_while_another_thread_makes_the_garbage_collector_run()
    {
        using var stop = new CancellationTokenSource();
        object? garbage = null;
        var neighbour = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                garbage = new byte[20_000];
            }
        })
        { IsBackground = true };
        neighbour.Start();
        try
        {
            for (var round = 0; round < 12; round++)
            {
                foreach (var timeframe in new[] { Timeframe.W1, Timeframe.Mo1 })
                {
                    AllocationProbe.ShouldAllocateNothing(window =>
                    {
                        var builder = new TimeframeBuilder(timeframe);
                        for (var i = 0; i < WarmUpSteps; i++)
                        {
                            builder.Push(Bars[i], out _, out _);
                        }

                        window.Begin();
                        for (var i = WarmUpSteps; i < WarmUpSteps + Steps; i++)
                        {
                            builder.Push(Bars[i], out _, out _);
                        }

                        window.End();
                    }, $"{timeframe.ToCode()}, round {round}");
                }
            }
        }
        finally
        {
            stop.Cancel();
            neighbour.Join();
        }

        GC.KeepAlive(garbage);
    }
}
