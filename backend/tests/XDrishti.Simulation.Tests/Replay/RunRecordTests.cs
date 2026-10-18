using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>Criterion 12 of P3-04: the record of an instrument says what the replay did, the counters are those of an independent frame and scanner, and the fates add up.</summary>
public sealed class RunRecordTests
{
    private static readonly PreparedStrategy Bare = Rep.Prepare(OrbV1.Create());

    private static Bar[] F1WithoutTenOhSeven(DateOnly date) => Day.P(date).B().Set("10:05 100.50/100.50/99.50/100.50", "10:30 100.50/104.50/100.50/104.00", "11:10 104.00/105.50/103.50/105.00").Absent("10:07").Bars();

    [Fact]
    public void The_record_holds_the_facts_of_the_replay_for_the_day_with_a_missing_minute()
    {
        var followed = ReplayDays.Join(F1WithoutTenOhSeven(ReplayDays.D1), ReplayDays.Flat(ReplayDays.D2, 105.00));

        var replay = Rep.Replay(Bare, followed, ReplayDays.D1, ReplayDays.D2);
        var record = replay.Record;

        record.InstrumentId.ShouldBe(Rep.Id);
        record.Symbol.ShouldBe("TEST");
        record.Skipped.ShouldBeNull();
        record.FirstBar.ShouldBe(ReplayDays.D1);
        record.LastBar.ShouldBe(ReplayDays.D2);
        record.ReplayFrom.ShouldBe(ReplayDays.D1);
        record.CountersCover.ShouldBe(new DateSpan(ReplayDays.D1, ReplayDays.D2));
        record.BarsRead.ShouldBe(374 + 375);
        record.BarsFed.ShouldBe(374 + 375);
        record.BarsExcluded.ShouldBe(0);
        record.BarsSha256.ShouldBe(BarStreamHash.Compute(followed));
        record.TickSize.ShouldBe(0.05m);
        record.TickFromFallback.ShouldBeFalse();
        record.Adjustments.ShouldBeEmpty();
        record.Sessions.ShouldBe(new SessionCounts(2, 0, 0, 0, 0));
        record.Notes.ShouldBeEmpty();
        record.Discontinuities.ShouldBeEmpty();
        Bare.Record.Id.ShouldBe("ORB");
        Bare.Record.DefinitionSha256.Length.ShouldBe(64);
        Bare.Record.Parameters.ShouldBeEmpty("the test prepared the strategy without parameters");
        ReplayIdentities.ShouldHold(replay);
    }

    [Fact]
    public void Bars_and_ignored_bars_of_each_side_are_those_of_a_frame_stepped_over_the_same_fed_bars_and_the_rest_are_the_scanners()
    {
        var bars = ReplayDays.Join(F1WithoutTenOhSeven(ReplayDays.D1), ReplayDays.F2(ReplayDays.D2));
        var definition = OrbV1.Create();

        var record = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D2).Record;
        var scanners = Scan.Both(definition, bars);

        record.Scans.Count.ShouldBe(2);
        for (var i = 0; i < 2; i++)
        {
            var side = Scan.Compile(definition, scanners[i].Counters.Side);
            var frame = FeatureFrame.Create(side.Features, new FrameOptions(Rep.DefaultCalendar, Math.Max(side.HistoryDepth, 1))).Value;
            foreach (var bar in bars)
            {
                frame.Step(bar);
            }

            record.Scans[i].Bars.ShouldBe(frame.Steps);
            record.Scans[i].IgnoredBars.ShouldBe(frame.IgnoredBars);
            record.Scans[i].IgnoredBars.ShouldBe(0);
            record.Scans[i].IncompleteBarsConsumed.ShouldBe(frame.IncompleteBarsConsumed);
            record.Scans[i].IncompleteBarsConsumed.ShouldBeGreaterThan(0, "the missing minute makes a bucket incomplete");
            record.Scans[i].LostDecisions.ShouldBe(scanners[i].Counters.LostDecisions);
            Render.Counters(record.Scans[i]).ShouldBe(Render.Counters(scanners[i].Counters), "the condition counts and every other figure are the scanner's");
        }
    }

    [Fact]
    public void An_excluded_bar_is_never_counted_as_ignored()
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.Minutes(ReplayDays.D2, "18:00", 60));

        var record = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D2).Record;

        record.BarsExcluded.ShouldBe(60);
        record.Scans.ShouldAllBe(s => s.IgnoredBars == 0);
        record.Scans.ShouldAllBe(s => s.Bars == 375);
    }

    [Fact]
    public void A_list_that_is_not_strictly_ascending_throws_and_names_the_instrument()
    {
        var day = ReplayDays.Flat(ReplayDays.D1, 99.00);
        var equal = day.Take(10).Append(day[9]).ToArray();
        var descending = day.Take(10).Append(day[5]).ToArray();

        foreach (var bars in new[] { equal, descending })
        {
            var error = Should.Throw<InvalidOperationException>(() => Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D1, symbol: "RELIANCE"));

            error.Message.ShouldContain("RELIANCE");
            error.Message.ShouldContain("ascending");
        }
    }

    [Fact]
    public void A_bar_dated_after_to_is_a_caller_error()
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.Flat(ReplayDays.D2, 99.00));

        Should.Throw<ArgumentException>(() => Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D1)).Message.ShouldContain("after To");
    }

    [Fact]
    public void Options_out_of_range_and_a_window_the_wrong_way_round_are_caller_errors()
    {
        var bars = ReplayDays.Flat(ReplayDays.D1, 99.00);

        Should.Throw<ArgumentException>(() => Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D1, options: new ReplayOptions(0.0)));
        Should.Throw<ArgumentException>(() => Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D1, options: new ReplayOptions(1.01)));
        Should.Throw<ArgumentException>(() => Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D1, options: new ReplayOptions(double.NaN)));
        Should.Throw<ArgumentException>(() => Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D1, options: new ReplayOptions(0.9, 0)));
        Should.Throw<ArgumentException>(() => Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D1, options: new ReplayOptions(0.9, double.PositiveInfinity)));
        Should.Throw<ArgumentException>(() => Rep.Replay(Bare, bars, ReplayDays.D2, ReplayDays.D1));
        Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D1, options: new ReplayOptions(1.0, 1e-300)).Record.Sessions.Ran.ShouldBe(1);
    }

    [Fact]
    public void An_instrument_with_one_bar_from_after_the_last_bar_and_a_to_before_the_first_bar_are_skips_not_errors()
    {
        var oneBar = ReplayDays.Minutes(ReplayDays.D1, "09:15", 1);

        Rep.Replay(Bare, oneBar, ReplayDays.D1, ReplayDays.D1).Record.Skipped.ShouldBe(SkipReason.NoRunnableSessions);
        Rep.Replay(Bare, ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.D3, ReplayDays.D4).Record.Skipped.ShouldBe(SkipReason.NoSessionsInWindow);
        Rep.Replay(Bare, [], ReplayDays.D1, ReplayDays.D1).Record.Skipped.ShouldBe(SkipReason.NoBars);
    }

    [Fact]
    public void The_candidates_and_trades_in_the_window_equal_a_count_of_the_lists_and_a_rule_that_fires_only_before_from_is_visible()
    {
        var bars = ReplayDays.Join(ReplayDays.F2(ReplayDays.D1), ReplayDays.F1(ReplayDays.D2), ReplayDays.Flat(ReplayDays.D3, 105.00));

        var wide = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D3);
        var narrow = Rep.Replay(Bare, bars, ReplayDays.D3, ReplayDays.D3);
        var middle = Rep.Replay(Bare, bars, ReplayDays.D2, ReplayDays.D3);

        (wide.Record.CandidatesInWindow, wide.Record.TradesInWindow).ShouldBe((2, 2));
        (middle.Record.CandidatesInWindow, middle.Record.TradesInWindow).ShouldBe((1, 1));
        (narrow.Record.CandidatesInWindow, narrow.Record.TradesInWindow).ShouldBe((0, 0), "the rule fired only before From: the counters say it did, the window figures say nothing fired in it");
        narrow.Record.Fates.TradesBeforeWindow.ShouldBe(2);
        narrow.Record.Scans.Sum(s => s.Candidates).ShouldBe(2);
        foreach (var replay in new[] { wide, narrow, middle })
        {
            ReplayIdentities.ShouldHold(replay);
        }
    }

    [Fact]
    public void The_fates_add_up_on_a_series_with_every_fate_in_it()
    {
        // d1 F1 (a trade), d2 the F6 day (open at the end of its data), d3 F2 at twice the level (a flagged day: suppressed), d4 F1 at that level (a trade); From is d1.
        var bars = ReplayDays.Join(
            ReplayDays.F1(ReplayDays.D1),
            ReplayDays.F6(ReplayDays.D2),
            ReplayDays.Scaled(ReplayDays.F2(ReplayDays.D3), 2.0),
            ReplayDays.Scaled(ReplayDays.F1(ReplayDays.D4), 2.0));

        var replay = Rep.Replay(Bare, bars, ReplayDays.D2, ReplayDays.D4);

        var fates = replay.Record.Fates;
        fates.TradesBeforeWindow.ShouldBe(1);
        fates.EntriesSuppressed.ShouldBe(1);
        fates.Total.ShouldBe(replay.Record.Scans.Sum(s => s.Candidates));
        ReplayIdentities.ShouldHold(replay);
    }

    [Fact]
    public void Two_replays_of_one_input_are_equal_field_for_field_in_a_canonical_text()
    {
        var bars = ReplayDays.Join(ReplayDays.F2(ReplayDays.D1), ReplayDays.F1(ReplayDays.D2), ReplayDays.F6(ReplayDays.D3));

        var a = RunText.Replay(Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D3));
        var b = RunText.Replay(Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D3));

        a.ShouldBe(b);
        RunText.HasNegativeZero(a).ShouldBeFalse("a negative zero is written 0");
    }

    [Fact]
    public void The_replay_does_not_change_the_list_it_is_given()
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 100.00), ReplayDays.Flat(ReplayDays.D2, 50.00));
        var copy = (Bar[])bars.Clone();

        Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D2, factors: Rep.Split(ReplayDays.D2, 1, 2));

        bars.ShouldBe(copy);
    }
}
