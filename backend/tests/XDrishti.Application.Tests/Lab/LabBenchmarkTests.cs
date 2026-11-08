#pragma warning disable CA2012 // NSubstitute verification calls on ValueTask members are awaited where they are asserted
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Lab;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Lab;

/// <summary>
/// CF-11 row 13 (ADR 0227): the daily Sharpe of the out-of-sample net R against the equal-weight hold of the run's universe over the same days, from stored day bars. Everything that stops the comparison reads unavailable (a fail on the scorecard), a failed read does not stop the run
/// and a cancel propagates.
/// </summary>
public sealed class LabBenchmarkTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static readonly DateOnly Start = new(2026, 1, 5); // a Monday

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IBarQueries _bars = Substitute.For<IBarQueries>();

    private LabBenchmark Benchmark() => new(_bars, new ReportBenchmarkOptions(), NullLogger<LabBenchmark>.Instance);

    /// <summary>The weekdays from <see cref="Start"/>.</summary>
    private static List<DateOnly> Sessions(int count)
    {
        var days = new List<DateOnly>();
        for (var d = Start; days.Count < count; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(d);
            }
        }

        return days;
    }

    /// <summary>The return of the hold on session <paramref name="i"/>: a steady wobble around +0.1 %.</summary>
    private static double HoldReturn(int i) => 0.001 + (0.004 * (((i * 7) % 5) - 2) / 2);

    /// <summary>The net R the strategy makes on session <paramref name="i"/>: a wobble of another size and shape.</summary>
    private static double StrategyR(int i) => ((i * 3) % 4) switch { 0 => 1.0, 1 => -0.4, 2 => 0.3, _ => 0.5 };

    private static BarRow Row(DateOnly date, double close) => new(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), Ist), close, close, close, close, 1000, 375, true, null);

    /// <summary>Every member of the universe has the same closes, so the hold's daily return is exactly <see cref="HoldReturn"/>.</summary>
    private void Bars(IReadOnlyList<LabInstrument> members, int sessions)
    {
        var days = Sessions(sessions);
        var close = 100.0;
        var rows = new List<BarRow>();
        for (var i = 0; i < days.Count; i++)
        {
            close *= 1 + (i == 0 ? 0 : HoldReturn(i));
            rows.Add(Row(days[i], close));
        }

        foreach (var m in members)
        {
            _bars.ReadDailyBarsAsync(m.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(call => (IReadOnlyList<BarRow>)[.. rows.Where(r => DateOnly.FromDateTime(r.Ts.DateTime) >= call.ArgAt<DateOnly>(1) && DateOnly.FromDateTime(r.Ts.DateTime) < call.ArgAt<DateOnly>(2))]);
        }
    }

    private static List<LabInstrument> Members(int count) => [.. Enumerable.Range(1, count).Select(i => new LabInstrument(Guid.Parse($"00000000-0000-0000-0000-{i:D12}"), $"S{i}"))];

    private static LabUniverse Universe(List<LabInstrument> members, bool sampled = false) => new("instrumentlist", null, "symbols as listed", members.Count, members, sampled, null);

    /// <summary>One trade a session from session <paramref name="from"/> to <paramref name="to"/> (inclusive), exiting on that session.</summary>
    private static List<SelectionTrade> Trades(int from, int to, Func<int, double>? r = null)
    {
        var days = Sessions(to + 1);
        return [.. Enumerable.Range(from, to - from + 1).Select(i => new SelectionTrade("c", days[i], days[i], (r ?? StrategyR)(i)))];
    }

    private static double Sharpe(IEnumerable<double> values)
    {
        var v = values.ToList();
        var mean = v.Average();
        return mean / Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / (v.Count - 1));
    }

    [Fact]
    public async Task The_two_daily_sharpes_are_measured_over_the_days_of_the_exits()
    {
        var members = Members(3);
        Bars(members, 90);

        var view = await Benchmark().ComputeAsync(Universe(members), Trades(10, 79), Ct);

        view.Reason.ShouldBeEmpty();
        view.Days.ShouldBe(70);
        view.Members.ShouldBe(3);
        view.StrategySharpe.ShouldNotBeNull().ShouldBe(Sharpe(Enumerable.Range(10, 70).Select(StrategyR)), 1e-9);
        view.HoldSharpe.ShouldNotBeNull().ShouldBe(Sharpe(Enumerable.Range(10, 70).Select(HoldReturn)), 1e-6);
        view.Basis.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_quiet_session_counts_as_zero_for_the_strategy()
    {
        var members = Members(3);
        Bars(members, 90);
        var trades = Trades(10, 79).Where(t => t.Exit != Sessions(80)[40]).ToList(); // no exit on session 40

        var view = await Benchmark().ComputeAsync(Universe(members), trades, Ct);

        view.Days.ShouldBe(70);
        view.StrategySharpe.ShouldNotBeNull().ShouldBe(Sharpe(Enumerable.Range(10, 70).Select(i => i == 40 ? 0.0 : StrategyR(i))), 1e-9);
    }

    [Fact]
    public async Task Too_few_days_is_unavailable()
    {
        var members = Members(3);
        Bars(members, 90);

        var view = await Benchmark().ComputeAsync(Universe(members), Trades(10, 40), Ct);

        view.StrategySharpe.ShouldBeNull();
        view.HoldSharpe.ShouldBeNull();
        view.Reason.ShouldContain("at least 60");
    }

    [Fact]
    public async Task A_strategy_that_makes_the_same_every_day_has_no_variance_and_is_unavailable()
    {
        var members = Members(3);
        Bars(members, 90);

        var view = await Benchmark().ComputeAsync(Universe(members), Trades(10, 79, _ => 0.5), Ct);

        view.StrategySharpe.ShouldBeNull();
        view.Reason.ShouldContain("strategy");
    }

    [Fact]
    public async Task A_member_with_no_bars_is_left_out_by_the_hold_so_it_is_not_the_same_universe()
    {
        var members = Members(3);
        Bars(members[..2], 90);
        _bars.ReadDailyBarsAsync(members[2].Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<BarRow>());

        var view = await Benchmark().ComputeAsync(Universe(members), Trades(10, 79), Ct);

        view.StrategySharpe.ShouldBeNull();
        view.Reason.ShouldContain("S3");
        view.Reason.ShouldContain("same-universe");
    }

    [Fact]
    public async Task Fewer_members_than_the_minimum_is_unavailable()
    {
        var members = Members(2);
        Bars(members, 90);

        (await Benchmark().ComputeAsync(Universe(members), Trades(10, 79), Ct)).HoldSharpe.ShouldBeNull();
    }

    [Fact]
    public async Task A_sampled_universe_is_not_the_universe_and_nothing_is_read()
    {
        var members = Members(3);

        var view = await Benchmark().ComputeAsync(Universe(members, sampled: true), Trades(10, 79), Ct);

        view.Reason.ShouldContain("sample");
        view.StrategySharpe.ShouldBeNull();
        await _bars.DidNotReceive().ReadDailyBarsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_out_of_sample_trade_is_unavailable_and_nothing_is_read()
    {
        var view = await Benchmark().ComputeAsync(Universe(Members(3)), [], Ct);

        view.StrategySharpe.ShouldBeNull();
        view.Reason.ShouldContain("no out-of-sample trade");
        await _bars.DidNotReceive().ReadDailyBarsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_trade_that_exits_on_a_session_a_member_did_not_trade_is_unavailable()
    {
        var members = Members(3);
        Bars(members[..2], 90);
        var days = Sessions(90);
        var close = 100.0;
        _bars.ReadDailyBarsAsync(members[2].Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyList<BarRow>)[.. days.Select((d, i) => (d, i)).Where(x => x.i != 40).Select(x => Row(x.d, close *= 1 + (x.i == 0 ? 0 : HoldReturn(x.i))))
                .Where(r => DateOnly.FromDateTime(r.Ts.DateTime) >= call.ArgAt<DateOnly>(1) && DateOnly.FromDateTime(r.Ts.DateTime) < call.ArgAt<DateOnly>(2))]);

        var view = await Benchmark().ComputeAsync(Universe(members), Trades(10, 79), Ct);

        view.StrategySharpe.ShouldBeNull();
        view.Reason.ShouldContain("not every member has a close");
    }

    [Fact]
    public async Task A_failed_read_is_unavailable_and_does_not_throw()
    {
        var members = Members(3);
        _bars.ReadDailyBarsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));

        var view = await Benchmark().ComputeAsync(Universe(members), Trades(10, 79), Ct);

        view.StrategySharpe.ShouldBeNull();
        view.HoldSharpe.ShouldBeNull();
        view.Reason.ShouldContain("could not be read");
        view.Reason.ShouldNotContain("db down", Case.Insensitive, "the message of the exception is not repeated");
    }

    [Fact]
    public async Task A_cancel_requested_on_the_token_propagates_and_is_not_an_unavailable_block()
    {
        var members = Members(3);
        using var cts = new CancellationTokenSource();
        _bars.ReadDailyBarsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<BarRow>>(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        await Should.ThrowAsync<OperationCanceledException>(async () => await Benchmark().ComputeAsync(Universe(members), Trades(10, 79), cts.Token));
    }

    [Fact]
    public async Task A_timeout_of_the_driver_is_an_unavailable_block_when_nobody_cancelled()
    {
        var members = Members(3);
        _bars.ReadDailyBarsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException("command timeout"));

        var view = await Benchmark().ComputeAsync(Universe(members), Trades(10, 79), Ct);

        view.StrategySharpe.ShouldBeNull();
        view.Reason.ShouldContain("could not be read");
    }
}
