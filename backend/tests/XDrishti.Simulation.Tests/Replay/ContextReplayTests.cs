using System.Diagnostics;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Context;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Scanning;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>P3-lift (ADR 0145): a strategy that reads the market context runs on the stored days, point-in-time; a missing context is a named skip; <c>is_expiry_day</c> stays refused.</summary>
public sealed class ContextReplayTests
{
    private static readonly PreparedStrategy Calm = Rep.Prepare(TestStrategies.WithBuy(OrbV1.Create(), b => b with { Setup = TestStrategies.All("vix < 20") }));

    private static readonly PreparedStrategy Plain = Rep.Prepare(OrbV1.Create());

    /// <summary>Weekdays from 2026-02-16 to 2026-02-27 (the last session before D1), each with the given VIX close.</summary>
    private static List<ContextDayRecord> Days(double? vix, DateOnly? last = null)
    {
        var end = last ?? new DateOnly(2026, 2, 27);
        var list = new List<ContextDayRecord>();
        for (var d = end.AddDays(-13); d <= end; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            list.Add(new ContextDayRecord(d, 20_100, 19_900, 20_000, vix, 375, vix is null ? null : 375));
        }

        return list;
    }

    private static InstrumentReplay Replay(PreparedStrategy strategy, IReadOnlyList<ContextDayRecord>? days, string? note = null) =>
        InstrumentReplayer.Replay(strategy, Rep.Input(ReplayDays.F1(ReplayDays.D1), ReplayDays.D1, ReplayDays.D1) with { ContextDays = days, ContextNote = note });

    [Fact]
    public void A_strategy_that_reads_the_context_is_accepted_and_says_so()
    {
        var build = Simulator.Create(TestStrategies.WithBuy(OrbV1.Create(), b => b with { Setup = TestStrategies.All("vix < 20 and regime() == \"UP_LOW_VOL\"") }), Timeframe.M15);

        build.Simulator.ShouldNotBeNull(string.Join("; ", build.Problems.Select(p => p.Path + " " + p.Problem.Code)));
        build.Simulator.ReadsContext.ShouldBeTrue();
        build.Problems.ShouldNotContain(p => p.Problem.Code == "simulation.needs_context");
        Simulator.Create(OrbV1.Create(), Timeframe.M15).Simulator!.ReadsContext.ShouldBeFalse();
    }

    [Fact]
    public void The_expiry_days_stay_refused_and_the_message_says_why()
    {
        var build = Simulator.Create(TestStrategies.WithBuy(OrbV1.Create(), b => b with { Setup = TestStrategies.All("is_expiry_day == 0") }), Timeframe.M15);

        build.Simulator.ShouldBeNull();
        var refusal = build.Problems.Single(p => p.Problem.Code == "simulation.needs_expiry_days");
        refusal.Path.ShouldBe("buy");
        refusal.Problem.Message.ShouldContain("No expiry calendar is stored");
        refusal.Problem.Message.ShouldContain("P14");
    }

    [Fact]
    public void The_stored_day_before_the_decision_gates_the_entry()
    {
        var calm = Replay(Calm, Days(15));
        var stormy = Replay(Calm, Days(30));

        calm.Record.Skipped.ShouldBeNull();
        calm.Trades.Count.ShouldBe(Replay(Plain, null).Trades.Count);
        calm.Trades.Count.ShouldBe(1);
        stormy.Record.Skipped.ShouldBeNull();
        stormy.Trades.ShouldBeEmpty("the VIX of the day before is 30, the condition is false");
    }

    [Fact]
    public void A_context_day_dated_on_or_after_the_decision_date_is_never_read()
    {
        var before = Days(15);
        var withFuture = before.Concat(
        [
            new ContextDayRecord(ReplayDays.D1, 20_100, 19_900, 20_000, 40, 375, 375),
            new ContextDayRecord(ReplayDays.D2, 20_100, 19_900, 20_000, 45, 375, 375),
        ]).ToList();

        var without = Replay(Calm, before);
        var with = Replay(Calm, withFuture);

        with.Trades.Count.ShouldBe(1);
        RunText.Trade(with.Trades[0]).ShouldBe(RunText.Trade(without.Trades[0]));
        with.Record.Scans.Select(s => (s.WindowSteps, s.FullyReadySteps)).ShouldBe(without.Record.Scans.Select(s => (s.WindowSteps, s.FullyReadySteps)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Truncating_or_changing_every_day_from_the_decision_date_on_changes_nothing(int seed)
    {
        var random = new Random(seed);
        var past = Days(random.Next(0, 2) == 0 ? 12 + random.Next(0, 6) : 22 + random.Next(0, 20));
        var future = Enumerable.Range(0, 5).Select(i => new ContextDayRecord(ReplayDays.D1.AddDays(i), 20_100, 19_900, 20_000, random.Next(5, 60), 375, 375)).ToList();

        var truncated = Replay(Calm, past);
        var extended = Replay(Calm, [.. past, .. future]);

        extended.Trades.Select(RunText.Trade).ShouldBe(truncated.Trades.Select(RunText.Trade));
        extended.Record.Fates.ShouldBe(truncated.Record.Fates);
    }

    [Fact]
    public void No_stored_day_skips_the_instrument_with_the_reason_and_runs_nothing()
    {
        var none = Replay(Calm, null, "no_context");
        var empty = Replay(Calm, []);

        none.Record.Skipped.ShouldBe(SkipReason.ContextUnavailable);
        none.Record.SkipDetail.ShouldBe("no_context");
        none.Trades.ShouldBeEmpty();
        empty.Record.Skipped.ShouldBe(SkipReason.ContextUnavailable);
        empty.Record.SkipDetail.ShouldBe("no_context");
        empty.Record.BarsRead.ShouldBe(375, "the data facts of a skipped instrument are kept");
    }

    [Fact]
    public void A_set_the_library_refuses_is_context_invalid()
    {
        var bad = new List<ContextDayRecord> { new(new DateOnly(2026, 2, 27), 100, 200, 150, 15, 375, 375) };

        var replay = Replay(Calm, bad);

        replay.Record.Skipped.ShouldBe(SkipReason.ContextUnavailable);
        replay.Record.SkipDetail.ShouldBe("context_invalid");
    }

    [Fact]
    public void A_strategy_that_reads_no_context_ignores_the_days_and_the_skip()
    {
        var with = Replay(Plain, Days(99));
        var without = Replay(Plain, null);

        with.Record.Skipped.ShouldBeNull();
        with.Trades.Select(RunText.Trade).ShouldBe(without.Trades.Select(RunText.Trade));
    }

    [Fact]
    public void A_context_older_than_the_age_bound_reads_as_not_ready_and_makes_no_trade()
    {
        var stale = Days(15, new DateOnly(2026, 2, 20));   // D1 is 10 days after the last day, the bound is 7

        var replay = Replay(Calm, stale);

        replay.Trades.ShouldBeEmpty("the condition is unknown past the age bound, never a default");
        Replay(Calm, Days(15, new DateOnly(2026, 2, 23))).Trades.Count.ShouldBe(1, "at the bound (7 days) the context is still read");
    }

    [Fact]
    public void The_same_days_give_the_same_trades_and_the_context_is_built_once_per_list()
    {
        var days = Days(15);

        var first = Replay(Calm, days);
        var second = Replay(Calm, days);

        first.Trades.Select(RunText.Trade).ShouldBe(second.Trades.Select(RunText.Trade));
        ReferenceEquals(ContextReader.Of(days), ContextReader.Of(days)).ShouldBeTrue();
        ReferenceEquals(ContextReader.Of(days), ContextReader.Of([.. days])).ShouldBeFalse("another list instance is another build");
        ContextReader.Of(null).ShouldBeNull();
        ContextReader.Of([]).ShouldBeNull();
    }

    [Fact]
    public void A_comparison_and_an_explain_read_the_same_context_as_a_plain_replay()
    {
        var days = Days(15);
        var input = Rep.Input(ReplayDays.F1(ReplayDays.D1), ReplayDays.D1, ReplayDays.D1) with { ContextDays = days };

        var plain = InstrumentReplayer.Replay(Calm, input);
        var explained = InstrumentReplayer.Explain(Calm, input, []);

        explained.Replay.Trades.Select(RunText.Trade).ShouldBe(plain.Trades.Select(RunText.Trade));
    }

    [Fact]
    public void Running_a_context_side_with_no_context_given_is_a_programming_error_not_a_default()
    {
        var run = Simulator.Create(TestStrategies.WithBuy(OrbV1.Create(), b => b with { Setup = TestStrategies.All("vix < 20") }), Timeframe.M15).Simulator!
            .Run(new InstrumentRef(Rep.Id, "TEST"), ReplayDays.F1(ReplayDays.D1), Sim.Tick);

        run.IsFailure.ShouldBeTrue();
        run.Error.Code.ShouldBe("simulation.context_not_given");
    }

    [Fact]
    public void The_context_costs_little_next_to_the_replay_it_serves()
    {
        var bars = ReplayDays.Join([.. Enumerable.Range(0, 3).SelectMany(i => ReplayDays.F1(ReplayDays.D1.AddDays(i * 7)))]);
        var days = Days(15);
        Bar[] set = [.. bars];
        TimeSpan Best(PreparedStrategy strategy, IReadOnlyList<ContextDayRecord>? with)
        {
            var best = TimeSpan.MaxValue;
            for (var i = 0; i < 7; i++)
            {
                var watch = Stopwatch.StartNew();
                InstrumentReplayer.Replay(strategy, Rep.Input(set, ReplayDays.D1, ReplayDays.D1.AddDays(21)) with { ContextDays = with });
                best = watch.Elapsed < best ? watch.Elapsed : best;
            }

            return best;
        }

        Best(Calm, days);   // warm-up of both paths
        Best(Plain, null);
        var context = Best(Calm, days);
        var bare = Best(Plain, null);

        context.ShouldBeLessThan(bare * 3 + TimeSpan.FromMilliseconds(20), $"with the context {context.TotalMilliseconds:F1} ms, without {bare.TotalMilliseconds:F1} ms");
    }
}
