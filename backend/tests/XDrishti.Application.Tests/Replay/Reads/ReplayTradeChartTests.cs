#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Reads;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Quality;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Tests.Replay.Reads;

/// <summary>SR-02, AC 7: the chart of a replay trade reads its window through the hold-out clamp, is coherent when the first bar opens at the entry, is cut at the clamp with a limit, and says no_bars for a symbol with no instrument.</summary>
public sealed class ReplayTradeChartTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Entry = new(2026, 3, 10); // a Tuesday
    private static readonly Guid Instrument = Guid.NewGuid();

    private sealed class Rig
    {
        public Rig(DateOnly last, bool inUniverse = true, ReplayTradeRow? trade = null)
        {
            var p = new ReplayParams(Entry.AddDays(-30), Entry.AddDays(10), new ReplayScope([Guid.NewGuid()], []), 1_000_000m, ReplayRiskProfile.Standard, null, ReplayVersionRule.Start, ReplayMode.Frozen);
            var run = new ReplayRunRow(RunId, null, p.From, p.To, "frozen", ReplayJson.Canonical(p), "{}", "{}", null, new string('a', 64), new string('b', 64), null, "completed", p.To, 5, 5, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
            Reads.GetRunAsync(RunId, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<ReplayRunRow?>(run));
            Reads.TradesAsync(RunId, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<ReplayTradeRow>>([trade ?? Trade()]));
            Reads.LabelsAsync(RunId, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<string>>([]));
            Facts.CalendarAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new TradingCalendar()));
            Facts.LastReadableSessionAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(last));
            Facts.UniverseAsync(Arg.Any<ReplayParams>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new ReplayUniverse(inUniverse ? [new ReplayInstrument(Instrument, "AAA")] : [], false)));
            Facts.MinuteBarsAsync(Instrument, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(c => ValueTask.FromResult<IReadOnlyList<Bar>>(Bars((DateOnly)c[1])));
        }

        public static readonly Guid RunId = Guid.NewGuid();

        public IReplayReads Reads { get; } = Substitute.For<IReplayReads>();

        public IReplayFacts Facts { get; } = Substitute.For<IReplayFacts>();

        public ValueTask<XDrishti.Domain.Common.Result<ReplayTradeChartView>> Ask(long tradeNo = 1, string? timeframe = "5m", int? before = null, int? after = null) =>
            new GetReplayTradeChartHandler(Reads, Facts, Substitute.For<IInstrumentQueries>()).HandleAsync(new GetReplayTradeChartQuery(RunId, tradeNo, timeframe, before, after), Ct);
    }

    private static ReplayTradeRow Trade(string side = "BUY") => new(
        1, "AAA", side, "S1|1D|BUY|swing|FIXED_RR{rr=2}", 100, Entry, DateTimeOffset.FromUnixTimeSeconds(MinuteAggregator.MidnightOf(Entry) + (555 * 60)), 100m, 98m, true, "k", Entry.AddDays(1), DateTimeOffset.FromUnixTimeSeconds(MinuteAggregator.MidnightOf(Entry.AddDays(1)) + (800 * 60)), 400m, 20m, 380m, null, 2.0);

    /// <summary>Weekday minute bars of the 4 days before the cut, each opening at 100 on the entry day, from 09:15 to 09:45.</summary>
    private static List<Bar> Bars(DateOnly cut)
    {
        var list = new List<Bar>();
        for (var d = cut.AddDays(-6); d <= cut; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            for (var m = 555; m < 585; m++)
            {
                list.Add(new Bar(MinuteAggregator.MidnightOf(d) + (m * 60), 100, 101, 99, 100, 10));
            }
        }

        return list;
    }

    [Fact]
    public async Task A_trade_with_bars_returns_the_window_markers_a_derived_exit_and_an_ok_coherence()
    {
        var result = await new Rig(Entry.AddDays(30)).Ask();

        result.IsSuccess.ShouldBeTrue();
        var v = result.Value;
        v.Chart.BarCount.ShouldBeGreaterThan(0);
        v.Chart.BarCount.ShouldBeLessThanOrEqualTo(5000);
        v.Chart.Coherence.State.ShouldBe("ok");
        (v.ExitPrice, v.ExitPriceDerived).ShouldBe((104m, true)); // 100 + 400 / 100
        v.Markers.Select(m => m.Kind).ShouldBe(["entry", "stop", "exit"]);
        v.Hindsight.Select(l => l.Code).ShouldContain(HindsightLabels.SettingsNow);
    }

    [Fact]
    public async Task A_window_past_the_clamp_is_cut_there_and_says_so_in_the_limits()
    {
        var rig = new Rig(Entry); // the last readable session is the entry day: the window may not reach the day after the exit
        var result = await rig.Ask(timeframe: "1D", after: 5);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Limits.ShouldContain(l => l.Contains("hold-out clamp"));
        await rig.Facts.Received().MinuteBarsAsync(Instrument, Entry, Arg.Any<CancellationToken>());
        await rig.Facts.DidNotReceive().MinuteBarsAsync(Instrument, Arg.Is<DateOnly>(d => d > Entry), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_trade_is_not_found_and_a_symbol_without_an_instrument_has_no_bars()
    {
        (await new Rig(Entry.AddDays(30)).Ask(tradeNo: 99)).Error.Code.ShouldBe("replay.not_found");

        var none = await new Rig(Entry.AddDays(30), inUniverse: false).Ask();

        none.IsSuccess.ShouldBeTrue();
        none.Value.Chart.Coherence.State.ShouldBe("no_bars");
        none.Value.Chart.Bars.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_bad_timeframe_is_refused()
    {
        (await new Rig(Entry.AddDays(30)).Ask(timeframe: "3m")).Error.Code.ShouldBe("replay.invalid");
    }
}
