using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>Criterion 11 of P3-04 and the preset skips of P3-04a: why an instrument has no trades, in the order of precedence.</summary>
public sealed class SkipReasonTests
{
    /// <summary>The test-only variant of the criterion: both sides need <c>close &gt; ema(30)</c>, which 15m bars have from the thirtieth closed bar on.</summary>
    private static StrategyDefinition BothNeedEma30() => OrbV1.Create() with
    {
        Buy = OrbV1.Create().Buy! with { Setup = TestStrategies.All("close > ema(30)") },
        Sell = OrbV1.Create().Sell! with { Setup = TestStrategies.All("close > ema(30)") },
    };

    private static readonly PreparedStrategy Ema30 = Rep.Prepare(BothNeedEma30());

    private static InstrumentRun Run(IReadOnlyList<XDrishti.Domain.Market.Bars.Bar> bars, DateOnly from, DateOnly to, PreparedStrategy? strategy = null) =>
        Rep.Replay(strategy ?? Ema30, bars, from, to).Record;

    [Fact]
    public void A_has_three_runnable_sessions_in_the_window_and_runs_with_the_features_ready_from_the_second_day()
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.Flat(ReplayDays.D2, 99.00), ReplayDays.Flat(ReplayDays.D3, 99.00));

        var record = Run(bars, ReplayDays.D1, ReplayDays.D3);

        record.Skipped.ShouldBeNull();
        record.Sessions.Ran.ShouldBe(3);
        record.Scans.ShouldAllBe(s => s.FullyReadySteps > 0);
        record.Scans[0].FullyReadySteps.ShouldBeLessThan(record.Scans[0].WindowSteps);
    }

    [Fact]
    public void B_has_no_bars()
    {
        var record = Run([], ReplayDays.D1, ReplayDays.D3);

        record.Skipped.ShouldBe(SkipReason.NoBars);
        record.BarsRead.ShouldBe(0);
        record.BarsSha256.ShouldBe("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        record.FirstBar.ShouldBeNull();
    }

    [Fact]
    public void C_has_only_the_evening_session_and_no_session_runs()
    {
        var record = Run(ReplayDays.Minutes(ReplayDays.D2, "18:00", 60), ReplayDays.D1, ReplayDays.D3);

        record.Skipped.ShouldBe(SkipReason.NoRunnableSessions);
        record.Sessions.ShortSession.ShouldBe(1);
        record.Scans.ShouldBeEmpty();
    }

    [Fact]
    public void D_has_runnable_sessions_only_before_from()
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.Flat(ReplayDays.D2, 99.00));

        var record = Run(bars, ReplayDays.D4, ReplayDays.D5);

        record.Skipped.ShouldBe(SkipReason.NoSessionsInWindow);
        record.Sessions.Ran.ShouldBe(2);
        record.Scans[0].WindowSteps.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void E_has_one_runnable_session_of_25_closed_bars_fewer_than_ema30_needs()
    {
        var record = Run(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.D1, ReplayDays.D1);

        record.Skipped.ShouldBe(SkipReason.FeaturesNotReady);
        record.Scans.Sum(s => s.WindowSteps).ShouldBeGreaterThan(0);
        record.Scans.ShouldAllBe(s => s.FullyReadySteps == 0);
    }

    [Fact]
    public void G_gives_no_window_step_because_no_15_minute_bar_closes_between_the_first_and_the_last_entry()
    {
        var narrow = Rep.Prepare(BothNeedEma30() with { TimeRules = new TimeRules(new TimeOnly(15, 5), new TimeOnly(15, 10), new TimeOnly(15, 15), null) });
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.Flat(ReplayDays.D2, 99.00));

        var record = Run(bars, ReplayDays.D1, ReplayDays.D2, narrow);

        record.Skipped.ShouldBe(SkipReason.NoRunnableSessions);
        record.Sessions.Ran.ShouldBe(2);
        record.Scans.Sum(s => s.WindowSteps).ShouldBe(0);
    }

    [Fact]
    public void A_skipped_instrument_that_was_read_keeps_its_data_facts_and_has_no_trades()
    {
        var bare = Rep.Prepare(OrbV1.Create());

        var replay = Rep.Replay(bare, ReplayDays.F1(ReplayDays.D1), ReplayDays.D3, ReplayDays.D3);
        var kept = replay.Record;

        replay.Trades.ShouldBeEmpty();
        replay.OpenAtEnd.ShouldBeEmpty();
        kept.Skipped.ShouldBe(SkipReason.NoSessionsInWindow);
        kept.BarsRead.ShouldBe(375);
        kept.BarsSha256.ShouldBe(XDrishti.Domain.Research.BarStreamHash.Compute(ReplayDays.F1(ReplayDays.D1)));
        kept.Sessions.Ran.ShouldBe(1);
        kept.Fates.TradesBeforeWindow.ShouldBe(1, "the counters of the scan are kept");
    }

    [Theory]
    [InlineData(SkipReason.CsvAdjustedTwice)]
    [InlineData(SkipReason.TickSizeInvalid)]
    public void A_preset_skip_classifies_the_sessions_and_hashes_the_bars_without_scanning(SkipReason preset)
    {
        var bars = ReplayDays.Join(ReplayDays.F1(ReplayDays.D1), ReplayDays.Minutes(ReplayDays.D2, "18:00", 60), ReplayDays.F1(ReplayDays.D3));
        var bare = Rep.Prepare(OrbV1.Create());

        var replay = InstrumentReplayer.Replay(bare, Rep.Input(bars, ReplayDays.D1, ReplayDays.D3, preset: preset, detail: "because"));
        var record = replay.Record;

        record.Skipped.ShouldBe(preset);
        record.SkipDetail.ShouldBe("because");
        replay.Trades.ShouldBeEmpty("a scan of F1 would give a trade");
        record.Scans.ShouldBeEmpty();
        record.Walk.ShouldBe(WalkCounters.Empty);
        record.Fates.ShouldBe(RunFates.Empty);
        record.ReplayFrom.ShouldBeNull();
        record.BarsRead.ShouldBe(810);
        record.BarsSha256.ShouldBe(BarStreamHash.Compute(bars));
        record.Sessions.Ran.ShouldBe(2);
        record.Sessions.ShortSession.ShouldBe(1);
        record.Notes.ShouldHaveSingleItem();
        (record.BarsFed + record.BarsExcluded).ShouldBe(record.BarsRead);
        record.FirstBar.ShouldBe(ReplayDays.D1);
        record.LastBar.ShouldBe(ReplayDays.D3);
    }

    [Fact]
    public void A_preset_skip_of_a_tick_size_has_no_tick_and_other_skips_have_the_tick_that_was_given()
    {
        var bars = ReplayDays.Flat(ReplayDays.D1, 99.00);
        var bare = Rep.Prepare(OrbV1.Create());

        var invalid = InstrumentReplayer.Replay(bare, Rep.Input(bars, ReplayDays.D1, ReplayDays.D1, preset: SkipReason.TickSizeInvalid)).Record;
        var csv = InstrumentReplayer.Replay(bare, Rep.Input(bars, ReplayDays.D1, ReplayDays.D1, preset: SkipReason.CsvAdjustedTwice, tick: TickSpec.FromMasterPaise(null).Value)).Record;

        invalid.TickSize.ShouldBeNull();
        invalid.TickFromFallback.ShouldBeFalse();
        csv.TickSize.ShouldBe(0.05m);
        csv.TickFromFallback.ShouldBeTrue();
    }

    [Theory]
    [InlineData(SkipReason.CsvAdjustedTwice)]
    [InlineData(SkipReason.TickSizeInvalid)]
    public void An_empty_list_gives_no_bars_whatever_the_preset_says(SkipReason preset)
    {
        var bare = Rep.Prepare(OrbV1.Create());

        var record = InstrumentReplayer.Replay(bare, Rep.Input([], ReplayDays.D1, ReplayDays.D1, preset: preset)).Record;

        record.Skipped.ShouldBe(SkipReason.NoBars);
    }

    [Fact]
    public void Precedence_puts_the_preset_before_no_runnable_sessions()
    {
        var bare = Rep.Prepare(OrbV1.Create());

        var record = InstrumentReplayer.Replay(bare, Rep.Input(ReplayDays.Minutes(ReplayDays.D2, "18:00", 60), ReplayDays.D1, ReplayDays.D3, preset: SkipReason.CsvAdjustedTwice)).Record;

        record.Skipped.ShouldBe(SkipReason.CsvAdjustedTwice);
    }

    [Fact]
    public void A_preset_that_the_runner_does_not_decide_is_a_caller_error_and_so_is_a_missing_tick_without_any_preset_skip()
    {
        var bare = Rep.Prepare(OrbV1.Create());
        var bars = ReplayDays.Flat(ReplayDays.D1, 99.00);

        Should.Throw<ArgumentException>(() => InstrumentReplayer.Replay(bare, Rep.Input(bars, ReplayDays.D1, ReplayDays.D1) with { PresetSkip = SkipReason.NoRunnableSessions }));
        Should.Throw<ArgumentException>(() => InstrumentReplayer.Replay(bare, Rep.Input(bars, ReplayDays.D1, ReplayDays.D1) with { Tick = null }));
    }

    [Fact]
    public void A_double_adjustment_with_a_refused_master_tick_is_skipped_as_the_double_adjustment_and_the_record_has_no_tick()
    {
        var bare = Rep.Prepare(OrbV1.Create());
        var bars = ReplayDays.Flat(ReplayDays.D1, 99.00);
        var input = Rep.Input(bars, ReplayDays.D1, ReplayDays.D1) with { PresetSkip = SkipReason.CsvAdjustedTwice, Tick = null, SkipDetail = "csv adjusted twice; the tick size is invalid" };

        var record = InstrumentReplayer.Replay(bare, input).Record;

        record.Skipped.ShouldBe(SkipReason.CsvAdjustedTwice);
        record.TickSize.ShouldBeNull();
        record.TickFromFallback.ShouldBeFalse();
        record.SkipDetail.ShouldNotBeNull().ShouldContain("tick");
        record.Sessions.Ran.ShouldBe(1);
    }
}
