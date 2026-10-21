using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>Criterion 13 of P3-04: the assembly of a run (totals, order, calendar exceptions) and its run key. The pre-image of the fixture and its SHA-256 were written out and hashed with an independent tool (Python 3 <c>hashlib</c>) on 2026-10-18.</summary>
public sealed class RunAssemblerTests
{
    private static readonly Guid IdA = new("00000000-0000-0000-0000-000000000001");

    private static readonly Guid IdB = new("00000000-0000-0000-0000-000000000002");

    private static readonly DateOnly Monday = new(2026, 3, 2);

    private const string Fills = "{\"entry_limit_percent\":0.1,\"slippage\":{\"ticks\":1,\"bar_range_frac\":0.05,\"stop_exit_extra_ticks\":2,\"gap_fill\":\"at_open\"}}";

    /// <summary>The SHA-256 of <see cref="PinnedKeyText"/> (UTF-8, as written, each line ending in a line feed).</summary>
    private const string PinnedKey = "af3cc569a62514558bcd9cd50600ccb0895df9dcd4d27be79ccf86dea5a53b75";

    private const string PinnedKeyText =
        "xd-run 6\n"
        + "engine=sim-3\n"
        + "strategy=1111111111111111111111111111111111111111111111111111111111111111\n"
        + "params=or_min=15,rr=2\n"
        + "timeframe=15m\n"
        + "window=2026-03-02..2026-03-04\n"
        + "options=0.9,0.2\n"
        + "ack=0\n"
        + "fills={\"entry_limit_percent\":0.1,\"slippage\":{\"ticks\":1,\"bar_range_frac\":0.05,\"stop_exit_extra_ticks\":2,\"gap_fill\":\"at_open\"}}\n"
        + "costs=none\n"
        + "universe=instruments as listed\n"
        + "i=00000000-0000-0000-0000-000000000001|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|-|2026-03-03:Split:1:2|0.05|0\n"
        + "i=00000000-0000-0000-0000-000000000002|bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb|no_sessions_in_window||0.1|1\n"
        + "cal=2026-03-03:holiday,2026-03-04:825-885\n";

    private static TradingCalendar Calendar(params TradingDay[] days) => new(days);

    private static TradingDay Holiday(DateOnly date) => TradingDay.Holiday(date, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch);

    private static TradingDay Special(DateOnly date, int from, int to) => TradingDay.Special(date, from, to, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch);

    private static StrategyRecord Strategy() => new(
        "ORB",
        new Dictionary<string, double> { ["rr"] = 2, ["or_min"] = 15 },
        "{}",
        new string('1', 64),
        Timeframe.M15,
        [Side.Buy, Side.Sell]);

    private static InstrumentRun Instrument(Guid id, string symbol, string hash, DateOnly first, DateOnly last, SkipReason? skip = null, IReadOnlyList<AppliedAdjustment>? adjustments = null, decimal tick = 0.05m, bool fallback = false) => new(
        id, symbol, skip, null,
        first, last, skip is null ? first : null, skip is null ? new DateSpan(first, last) : null,
        750, 750, 0, 0, hash,
        tick, fallback,
        adjustments ?? [], new SessionCounts(2, 0, 0, 0, 0), [], false, [],
        [], WalkCounters.Empty, RunFates.Empty,
        0, 0);

    private static InstrumentReplay Replay(InstrumentRun run, IReadOnlyList<SimTrade>? trades = null, IReadOnlyList<SimTrade>? openAtEnd = null) => new(run, trades ?? [], openAtEnd ?? []);

    private static SimTrade Trade(Guid instrument, Side side, long entryEpoch, ExitReason exit = ExitReason.Target) => new(
        4, "ORB", instrument, "X", side, Monday, entryEpoch - 60, Timeframe.M15, new Bar(entryEpoch - 960, 99, 100.5, 99, 100.5, 15000), 15, true,
        entryEpoch, 100.5, 98, 105.5, entryEpoch + 600, 105.5, exit, 2, 0, 2, FillBasis.Exact, CostBasis.None, null,
        2, 0, 100.5, 105.5, EntryFill.AtOpen, false, 1, 0);

    private static UniverseRecord List(params Guid[] ids) =>
        new(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [.. ids.Select((id, i) => new UniverseMember(id, "S" + i))]);

    private static RunHeader Header(UniverseRecord universe, TradingCalendar? calendar = null, DateOnly? to = null) => new(
        Strategy(), "sim-3", Monday, to ?? new DateOnly(2026, 3, 4), ReplayOptions.Default, false, Fills, universe, calendar ?? Calendar());

    private static (RunHeader Header, InstrumentReplay[] Replays) PinnedFixture()
    {
        var a = Instrument(IdA, "A", new string('a', 64), Monday, new DateOnly(2026, 3, 4), adjustments: [new AppliedAdjustment(new DateOnly(2026, 3, 3), CorporateActionKind.Split, 1, 2, 0.5m, false)]);
        var b = Instrument(IdB, "B", new string('b', 64), new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 4), SkipReason.NoSessionsInWindow, tick: 0.10m, fallback: true);
        var calendar = Calendar(Holiday(new DateOnly(2026, 3, 3)), Special(new DateOnly(2026, 3, 4), 13 * 60 + 45, 14 * 60 + 45), Special(new DateOnly(2026, 3, 7), 9 * 60 + 15, 12 * 60));
        return (Header(List(IdA, IdB), calendar), [Replay(a), Replay(b)]);
    }

    [Fact]
    public void The_key_pre_image_of_a_two_instrument_fixture_equals_the_pinned_text_and_its_sha256_a_known_answer()
    {
        var (header, replays) = PinnedFixture();

        var run = RunAssembler.Assemble(header, replays);

        RunAssembler.KeyText(header, run.Instruments, run.CalendarExceptions).ShouldBe(PinnedKeyText);
        run.RunKey.ShouldBe(PinnedKey);
        run.RunKey.ShouldBe(CanonicalText.Sha256Hex(PinnedKeyText));
        run.SchemaVersion.ShouldBe(6);
    }

    [Fact]
    public void Two_assemblies_of_one_input_give_the_same_key_and_equal_parts()
    {
        var (header, replays) = PinnedFixture();

        var a = RunAssembler.Assemble(header, replays);
        var b = RunAssembler.Assemble(header, replays);

        a.RunKey.ShouldBe(b.RunKey);
        a.CalendarExceptions.ShouldBe(b.CalendarExceptions);
        a.Totals.ShouldBe(b.Totals with { Scans = a.Totals.Scans, Skipped = a.Totals.Skipped });
        a.Totals.Skipped.ShouldBe(b.Totals.Skipped);
    }

    public static TheoryData<string> Changes => ["close", "option completeness", "option discontinuity", "parameter", "window from", "window to", "acknowledgement", "universe label", "applied action", "calendar exception", "fills", "tick", "fallback", "engine", "strategy hash", "skip", "timeframe", "universe order"];

    [Theory]
    [MemberData(nameof(Changes))]
    public void The_key_changes_when_anything_that_can_change_a_trade_changes(string change)
    {
        var (header, replays) = PinnedFixture();
        var baseline = RunAssembler.Assemble(header, replays).RunKey;
        var a = replays[0].Record;
        var b = replays[1].Record;

        var (changedHeader, changedReplays) = change switch
        {
            "close" => (header, new[] { Replay(a with { BarsSha256 = BarStreamHash.Compute([new Bar(1, 1, 1, 1, 1.05, 1)]) }), replays[1] }),
            "option completeness" => (header with { Options = new ReplayOptions(0.95, 0.20) }, replays),
            "option discontinuity" => (header with { Options = new ReplayOptions(0.90, 0.10) }, replays),
            "parameter" => (header with { Strategy = header.Strategy with { Parameters = new Dictionary<string, double> { ["rr"] = 2, ["or_min"] = 30 } } }, replays),
            "window from" => (header with { From = new DateOnly(2026, 3, 3) }, replays),
            "window to" => (header with { To = new DateOnly(2026, 3, 5) }, replays),
            "acknowledgement" => (header with { AcknowledgeCsvAdjusted = true }, replays),
            "universe label" => (header with { Universe = header.Universe with { Label = UniverseLabels.MembersAsOfToday } }, replays),
            "applied action" => (header, new[] { Replay(a with { Adjustments = [.. a.Adjustments, new AppliedAdjustment(new DateOnly(2026, 3, 4), CorporateActionKind.Bonus, 1, 1, 0.5m, false)] }), replays[1] }),
            "calendar exception" => (header with { Calendar = Calendar(Holiday(new DateOnly(2026, 3, 3)), Special(new DateOnly(2026, 3, 4), 13 * 60 + 45, 14 * 60 + 45), Holiday(new DateOnly(2026, 3, 2))) }, replays),
            "fills" => (header with { Fills = Fills.Replace("0.1", "0.2", StringComparison.Ordinal) }, replays),
            "tick" => (header, new[] { replays[0], Replay(b with { TickSize = 0.01m }) }),
            "fallback" => (header, new[] { replays[0], Replay(b with { TickFromFallback = false }) }),
            "engine" => (header with { EngineVersion = "sim-4" }, replays),
            "strategy hash" => (header with { Strategy = header.Strategy with { DefinitionSha256 = new string('2', 64) } }, replays),
            "skip" => (header, new[] { replays[0], Replay(b with { Skipped = SkipReason.FeaturesNotReady }) }),
            "timeframe" => (header with { Strategy = header.Strategy with { EvaluationTimeframe = Timeframe.M5 } }, replays),
            "universe order" => (header with { Universe = List(IdB, IdA) }, new[] { replays[1], replays[0] }),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        RunAssembler.Assemble(changedHeader, changedReplays).RunKey.ShouldNotBe(baseline, change);
    }

    [Fact]
    public void A_calendar_exception_outside_the_range_of_the_data_does_not_change_the_key()
    {
        var (header, replays) = PinnedFixture();
        var baseline = RunAssembler.Assemble(header, replays).RunKey;

        var after = RunAssembler.Assemble(header with { Calendar = Calendar(Holiday(new DateOnly(2026, 3, 3)), Special(new DateOnly(2026, 3, 4), 13 * 60 + 45, 14 * 60 + 45), Holiday(new DateOnly(2026, 3, 11))) }, replays).RunKey;
        var before = RunAssembler.Assemble(header with { Calendar = Calendar(Holiday(new DateOnly(2026, 3, 3)), Special(new DateOnly(2026, 3, 4), 13 * 60 + 45, 14 * 60 + 45), Holiday(new DateOnly(2026, 2, 25))) }, replays).RunKey;

        after.ShouldBe(baseline);
        before.ShouldBe(baseline);
    }

    [Fact]
    public void The_key_does_not_depend_on_the_order_the_parameters_are_given_in_nor_on_the_scale_of_the_tick_nor_a_negative_zero()
    {
        var (header, replays) = PinnedFixture();
        var baseline = RunAssembler.Assemble(header, replays).RunKey;

        var reordered = header with { Strategy = header.Strategy with { Parameters = new Dictionary<string, double> { ["or_min"] = 15, ["rr"] = 2 } } };
        var scale = new[] { Replay(replays[0].Record with { TickSize = 0.0500m }), replays[1] };
        var options = header with { Options = new ReplayOptions(0.90, 0.20) };

        RunAssembler.Assemble(reordered, replays).RunKey.ShouldBe(baseline);
        RunAssembler.Assemble(header, scale).RunKey.ShouldBe(baseline);
        RunAssembler.Assemble(options, replays).RunKey.ShouldBe(baseline);
        RunAssembler.KeyText(header with { Strategy = header.Strategy with { Parameters = new Dictionary<string, double> { ["x"] = -0.0 } } }, [], []).ShouldContain("params=x=0\n");
    }

    [Fact]
    public void Trades_and_open_at_end_are_ordered_by_entry_time_then_place_in_the_universe_then_buy_before_sell()
    {
        var t = 1_772_500_000L;
        var a = Instrument(IdA, "A", new string('a', 64), Monday, Monday);
        var b = Instrument(IdB, "B", new string('b', 64), Monday, Monday);
        var replays = new[]
        {
            Replay(a, [Trade(IdA, Side.Sell, t), Trade(IdA, Side.Buy, t + 600)], [Trade(IdA, Side.Sell, t, ExitReason.EndOfData)]),
            Replay(b, [Trade(IdB, Side.Buy, t), Trade(IdB, Side.Sell, t)], [Trade(IdB, Side.Buy, t - 60, ExitReason.EndOfData), Trade(IdB, Side.Buy, t, ExitReason.EndOfData)]),
        };

        var run = RunAssembler.Assemble(Header(List(IdA, IdB)), replays);

        run.Trades.Select(x => (x.Trade.InstrumentId, x.Trade.Side, x.Trade.EntryEpochSeconds - t)).ShouldBe(
        [
            (IdA, Side.Sell, 0L), (IdB, Side.Buy, 0L), (IdB, Side.Sell, 0L), (IdA, Side.Buy, 600L),
        ]);
        run.OpenAtEnd.Select(x => (x.Trade.InstrumentId, x.Trade.EntryEpochSeconds - t)).ShouldBe([(IdB, -60L), (IdA, 0L), (IdB, 0L)]);
    }

    [Fact]
    public void Both_sides_of_one_instrument_at_one_entry_time_come_buy_first()
    {
        var t = 1_772_500_000L;
        var a = Instrument(IdA, "A", new string('a', 64), Monday, Monday);

        var run = RunAssembler.Assemble(Header(List(IdA)), [Replay(a, [Trade(IdA, Side.Sell, t), Trade(IdA, Side.Buy, t)])]);

        run.Trades.Select(x => x.Trade.Side).ShouldBe([Side.Buy, Side.Sell]);
    }

    [Fact]
    public void Every_trade_carries_the_basket_ids_of_the_universe_none_for_a_list()
    {
        var t = 1_772_500_000L;
        var basket = Guid.NewGuid();
        var a = Instrument(IdA, "A", new string('a', 64), Monday, Monday);
        var replay = Replay(a, [Trade(IdA, Side.Buy, t)], [Trade(IdA, Side.Sell, t, ExitReason.EndOfData)]);
        var asBasket = new UniverseRecord(UniverseKind.Basket, basket, "Nifty", UniverseLabels.MembersAsOfToday, [new UniverseMember(IdA, "A")]);

        var list = RunAssembler.Assemble(Header(List(IdA)), [replay]);
        var fromBasket = RunAssembler.Assemble(Header(asBasket), [replay]);

        list.Trades.ShouldAllBe(x => x.BasketIds.Count == 0);
        list.OpenAtEnd.ShouldAllBe(x => x.BasketIds.Count == 0);
        fromBasket.Trades.ShouldAllBe(x => x.BasketIds.SequenceEqual(new[] { basket }));
        fromBasket.OpenAtEnd.ShouldAllBe(x => x.BasketIds.SequenceEqual(new[] { basket }));
    }

    [Fact]
    public void The_totals_are_the_sums_of_the_instruments()
    {
        var scanA = ScanCounters.Empty(Side.Buy) with { Bars = 375, WindowSteps = 9, Candidates = 1, Conditions = [new ConditionCounter("buy.trigger[0]", 1, 8, 0, 0)] };
        var scanB = ScanCounters.Empty(Side.Buy) with { Bars = 750, WindowSteps = 18, Candidates = 2, Conditions = [new ConditionCounter("buy.trigger[0]", 2, 16, 0, 0)] };
        var sell = ScanCounters.Empty(Side.Sell) with { Bars = 375 };
        var a = Instrument(IdA, "A", new string('a', 64), Monday, Monday) with
        {
            BarsRead = 400,
            BarsFed = 375,
            BarsExcluded = 25,
            BarsOutsideSession = 5,
            Sessions = new SessionCounts(1, 1, 2, 3, 4),
            Discontinuities = [new DiscontinuityNote(Monday, 2.0)],
            Scans = [scanA, sell],
            Walk = WalkCounters.Empty with { Candidates = 1, Trades = 1, LimitNotReached = 3 },
            Fates = new RunFates(1, 2, 3, 4, 5, 6, 7, 8, 9, 10),
            CandidatesInWindow = 5,
            TradesInWindow = 6,
        };
        var b = Instrument(IdB, "B", new string('b', 64), Monday, Monday, SkipReason.NoBars) with
        {
            BarsRead = 100,
            BarsFed = 80,
            BarsExcluded = 20,
            Scans = [scanB],
            Walk = WalkCounters.Empty with { Candidates = 2, LimitNotReached = 1 },
            Fates = new RunFates(10, 10, 10, 10, 10, 10, 10, 10, 10, 10),
            CandidatesInWindow = 1,
            TradesInWindow = 2,
            Sessions = new SessionCounts(1, 0, 0, 0, 1),
        };
        var missing = InstrumentRun.NotFound(Guid.NewGuid());
        var universe = new UniverseRecord(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [new UniverseMember(IdA, "A"), new UniverseMember(IdB, "B"), new UniverseMember(missing.InstrumentId, "?")]);

        var totals = RunAssembler.Assemble(Header(universe), [Replay(a), Replay(b), Replay(missing)]).Totals;

        totals.Instruments.ShouldBe(3);
        totals.InstrumentsRun.ShouldBe(1);
        totals.Skipped.ShouldBe([new SkipCount(SkipReason.InstrumentNotFound, 1), new SkipCount(SkipReason.NoBars, 1)]);
        (totals.BarsRead, totals.BarsFed, totals.BarsExcluded, totals.BarsOutsideSession).ShouldBe((500, 455, 45, 5));
        totals.Sessions.ShouldBe(new SessionCounts(2, 1, 2, 3, 5));
        totals.DiscontinuitiesFlagged.ShouldBe(1);
        totals.Walk.Candidates.ShouldBe(3);
        totals.Walk.LimitNotReached.ShouldBe(4);
        totals.Fates.ShouldBe(new RunFates(11, 12, 13, 14, 15, 16, 17, 18, 19, 20));
        totals.OpenAtEnd.ShouldBe(13);
        totals.EntriesSuppressed.ShouldBe(11);
        (totals.CandidatesInWindow, totals.TradesInWindow).ShouldBe((6, 8));
        totals.Scans.Select(s => (s.Side, s.Bars, s.WindowSteps, s.Candidates)).ShouldBe([(Side.Buy, 1125L, 27L, 3L), (Side.Sell, 375L, 0L, 0L)]);
        totals.Scans[0].Conditions.ShouldBe([new ConditionCounter("buy.trigger[0]", 3, 24, 0, 0)]);
    }

    [Fact]
    public void The_calendar_exceptions_are_the_holidays_the_special_sessions_and_the_weekend_sessions_between_the_earliest_first_bar_and_to()
    {
        var saturday = new DateOnly(2026, 3, 7);
        var sunday = new DateOnly(2026, 3, 8);
        var calendar = Calendar(
            Holiday(new DateOnly(2026, 2, 27)),                       // before the earliest first bar
            Holiday(new DateOnly(2026, 3, 3)),                        // a weekday holiday
            Special(new DateOnly(2026, 3, 4), 13 * 60 + 45, 14 * 60 + 45), // a special session on a weekday
            Special(saturday, 9 * 60 + 15, 15 * 60 + 30),             // a regular window on a weekend: still an exception
            Special(sunday, 9 * 60 + 15, 12 * 60),                    // a short window on a weekend
            Holiday(new DateOnly(2026, 3, 14)),                       // a holiday on a weekend: the default week has no session there, so nothing departs from it
            Special(new DateOnly(2026, 3, 9), 9 * 60 + 15, 15 * 60 + 30), // a regular window on a weekday: the default week already has it
            Holiday(new DateOnly(2026, 3, 20)));                      // after To
        var a = Instrument(IdA, "A", new string('a', 64), new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 13));
        var b = Instrument(IdB, "B", new string('b', 64), new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 13));

        var run = RunAssembler.Assemble(Header(List(IdA, IdB), calendar, new DateOnly(2026, 3, 13)), [Replay(a), Replay(b)]);

        run.CalendarExceptions.ShouldBe(
        [
            new CalendarExceptionDay(new DateOnly(2026, 3, 3), true, 0, 0),
            new CalendarExceptionDay(new DateOnly(2026, 3, 4), false, 825, 885),
            new CalendarExceptionDay(saturday, false, 555, 930),
            new CalendarExceptionDay(sunday, false, 555, 720),
        ]);
    }

    [Fact]
    public void A_run_with_no_data_has_no_calendar_exceptions()
    {
        var universe = new UniverseRecord(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [new UniverseMember(IdA, "A")]);
        var run = RunAssembler.Assemble(Header(universe, Calendar(Holiday(new DateOnly(2026, 3, 3)))), [Replay(InstrumentRun.NotFound(IdA))]);

        run.CalendarExceptions.ShouldBeEmpty();
        RunAssembler.KeyText(Header(universe), run.Instruments, run.CalendarExceptions).ShouldEndWith("i=" + IdA + "|-|instrument_not_found||-|0\ncal=\n");
    }

    [Fact]
    public void Replays_that_are_not_one_per_member_of_the_universe_in_its_order_are_refused()
    {
        var a = Replay(Instrument(IdA, "A", new string('a', 64), Monday, Monday));
        var b = Replay(Instrument(IdB, "B", new string('b', 64), Monday, Monday));

        Should.Throw<ArgumentException>(() => RunAssembler.Assemble(Header(List(IdA, IdB)), [a]));
        Should.Throw<ArgumentException>(() => RunAssembler.Assemble(Header(List(IdA, IdB)), [b, a]));
        Should.Throw<ArgumentNullException>(() => RunAssembler.Assemble(null!, [a]));
        Should.Throw<ArgumentNullException>(() => RunAssembler.Assemble(Header(List(IdA)), null!));
    }

    [Fact]
    public void The_run_carries_the_header_as_given()
    {
        var (header, replays) = PinnedFixture();

        var run = RunAssembler.Assemble(header, replays);

        run.EngineVersion.ShouldBe("sim-3");
        run.Strategy.ShouldBe(header.Strategy);
        (run.From, run.To, run.Options, run.AcknowledgeCsvAdjusted, run.Fills, run.Universe).ShouldBe((header.From, header.To, header.Options, false, Fills, header.Universe));
        run.Instruments.Select(i => i.InstrumentId).ShouldBe([IdA, IdB]);
    }
}
