using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>Criteria 5 to 7 of P3-04: the discontinuity table, the restart at a flagged day with no entry, and a split that makes the series continuous.</summary>
public sealed class DiscontinuityTests
{
    private static readonly PreparedStrategy Bare = Rep.Prepare(OrbV1.Create());

    private static readonly PreparedStrategy WithEma = Rep.Prepare(Rep.OrbWithEma10());

    public static TheoryData<double, double, bool, bool> Table => new()
    {
        { 100.00, 90.00, false, false },
        { 100.00, 110.00, false, false },
        { 100.00, 89.99, false, true },
        { 100.00, 110.01, false, true },
        { 100.00, 80.00, false, true },
        { 100.00, 120.00, false, true },
        { 100.00, 79.99, true, true },
        { 100.00, 120.01, true, true },
        { 12.00, 9.60, false, true },
        { 12.00, 14.40, false, true },
        { 12.00, 9.59, true, true },
        { 12.00, 14.41, true, true },
        { 1.50, 1.20, false, true },
        { 775.00, 320.00, true, true },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void A_day_is_flagged_with_its_ratio_exactly_where_the_table_says_for_the_default_threshold_and_for_ten_percent(double previousClose, double open, bool flaggedByDefault, bool flaggedAtTenPercent)
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, previousClose), ReplayDays.Flat(ReplayDays.D2, open));

        var byDefault = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D2).Record;
        var atTen = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D2, options: new ReplayOptions(0.90, 0.10)).Record;

        (byDefault.Discontinuities.Count == 1).ShouldBe(flaggedByDefault);
        (atTen.Discontinuities.Count == 1).ShouldBe(flaggedAtTenPercent);
        foreach (var record in new[] { byDefault, atTen }.Where(r => r.Discontinuities.Count == 1))
        {
            record.Discontinuities[0].ShouldBe(new DiscontinuityNote(ReplayDays.D2, open / previousClose));
        }
    }

    [Fact]
    public void The_first_session_that_runs_is_never_flagged()
    {
        var bars = ReplayDays.Flat(ReplayDays.D1, 5000.00);

        Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D1).Record.Discontinuities.ShouldBeEmpty();
    }

    [Fact]
    public void A_flagged_day_restarts_the_replay_and_gets_no_entry_and_the_counters_are_those_of_the_two_separate_replays()
    {
        var d1 = ReplayDays.Flat(ReplayDays.D1, 99.00);
        var d2 = ReplayDays.Scaled(ReplayDays.F1(ReplayDays.D2), 2.0);
        var d3 = ReplayDays.Flat(ReplayDays.D3, 210.00);

        var whole = Rep.Replay(Bare, ReplayDays.Join(d1, d2, d3), ReplayDays.D1, ReplayDays.D3);
        var first = Rep.Replay(Bare, d1, ReplayDays.D1, ReplayDays.D1);
        var rest = Rep.Replay(Bare, ReplayDays.Join(d2, d3), ReplayDays.D2, ReplayDays.D3);

        whole.Record.Discontinuities.ShouldBe([new DiscontinuityNote(ReplayDays.D2, 2.0)]);
        whole.Record.Fates.EntriesSuppressed.ShouldBe(1);
        whole.Trades.ShouldBeEmpty();
        rest.Trades.Count.ShouldBe(1, "the trade of d2 is real when d2 is the first session");
        whole.Record.Scans.Count.ShouldBe(first.Record.Scans.Count);
        for (var side = 0; side < whole.Record.Scans.Count; side++)
        {
            whole.Record.Scans[side].ShouldBe(first.Record.Scans[side].Add(rest.Record.Scans[side]));
        }

        whole.Record.Walk.ShouldBe(first.Record.Walk.Add(rest.Record.Walk));
        whole.Record.Walk.Candidates.ShouldBe(1, "the walker's counters include the outcome the flagged day dropped");
        whole.Record.Fates.Total.ShouldBe(whole.Record.Scans.Sum(s => s.Candidates));
    }

    [Fact]
    public void The_indicators_restart_at_a_flagged_day_so_the_old_price_level_is_not_carried_over()
    {
        var flat1 = ReplayDays.Flat(ReplayDays.D1, 99.00);
        var restarted = ReplayDays.Join(flat1, ReplayDays.Scaled(ReplayDays.F1(ReplayDays.D2), 2.0), ReplayDays.Flat(ReplayDays.D3, 210.00));
        var carried = ReplayDays.Join(flat1, ReplayDays.F1(ReplayDays.D2), ReplayDays.Flat(ReplayDays.D3, 105.00));

        var flagged = Rep.Replay(WithEma, restarted, ReplayDays.D1, ReplayDays.D3).Record;
        var continuous = Rep.Replay(WithEma, carried, ReplayDays.D1, ReplayDays.D3).Record;

        flagged.Discontinuities.Count.ShouldBe(1);
        continuous.Discontinuities.ShouldBeEmpty();
        Identities.Of(flagged.Scans[0], "buy.setup[0]").NotReady.ShouldBe(18, "9 window steps of d1 and 9 of d2: ema(10) restarts at d2");
        Identities.Of(continuous.Scans[0], "buy.setup[0]").NotReady.ShouldBe(9, "ema(10) is ready from the tenth closed bar of d1 on");
    }

    [Fact]
    public void A_split_makes_the_series_continuous_and_without_the_action_the_day_is_flagged()
    {
        var d1 = ReplayDays.Flat(ReplayDays.D1, 100.00);
        var d2 = ReplayDays.Flat(ReplayDays.D2, 50.05);
        var bars = ReplayDays.Join(d1, d2);
        IReadOnlyList<Bar>? fed = null;

        var withSplit = InstrumentReplayer.ReplayCore(Bare, Rep.Input(bars, ReplayDays.D1, ReplayDays.D2, factors: Rep.Split(ReplayDays.D2, 1, 2)), (list, _) => fed = list).Record;
        var tiny = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D2, factors: Rep.Split(ReplayDays.D2, 1, 2), options: new ReplayOptions(0.90, 0.0005)).Record;
        var without = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D2).Record;

        withSplit.Discontinuities.ShouldBeEmpty();
        fed.ShouldNotBeNull()[374].Close.ShouldBe(50.00);
        tiny.Discontinuities.ShouldBe([new DiscontinuityNote(ReplayDays.D2, 50.05 / 50.00)]);
        without.Discontinuities.ShouldBe([new DiscontinuityNote(ReplayDays.D2, 50.05 / 100.00)]);
    }

    [Fact]
    public void A_flagged_day_that_is_the_last_day_of_the_data_is_flagged_and_its_outcome_is_dropped()
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 775.00), ReplayDays.Scaled(ReplayDays.F1(ReplayDays.D2), 3.2));

        var replay = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D2);

        replay.Record.Discontinuities.Select(d => d.Date).ShouldBe([ReplayDays.D2]);
        replay.Trades.ShouldBeEmpty();
        replay.Record.Fates.EntriesSuppressed.ShouldBe(1);
    }

    [Fact]
    public void Two_flagged_days_in_a_row_make_two_restarts()
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 100.00), ReplayDays.Flat(ReplayDays.D2, 50.00), ReplayDays.Flat(ReplayDays.D3, 100.00));

        var record = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D3).Record;

        record.Discontinuities.ShouldBe([new DiscontinuityNote(ReplayDays.D2, 0.5), new DiscontinuityNote(ReplayDays.D3, 2.0)]);
    }

    [Fact]
    public void A_split_and_a_flagged_jump_on_one_day_are_judged_on_the_adjusted_series()
    {
        // Raw 100.00 then 30.00 with a 1:2 split on the second day: adjusted, 50.00 then 30.00 is still a 40 % fall.
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 100.00), ReplayDays.Flat(ReplayDays.D2, 30.00));

        var record = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D2, factors: Rep.Split(ReplayDays.D2, 1, 2)).Record;

        record.Discontinuities.ShouldBe([new DiscontinuityNote(ReplayDays.D2, 30.00 / 50.00)]);
    }

    [Fact]
    public void A_tiny_threshold_flags_any_move_and_a_threshold_of_one_flags_a_fall_to_zero_only_at_more_than_the_close()
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 100.00), ReplayDays.Flat(ReplayDays.D2, 100.05));

        Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D2, options: new ReplayOptions(0.90, 1e-9)).Record.Discontinuities.Count.ShouldBe(1);
        Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D2, options: new ReplayOptions(1.0, 1.0)).Record.Discontinuities.ShouldBeEmpty();
    }

    [Fact]
    public void A_threshold_beyond_any_price_move_and_prices_beyond_a_decimal_do_not_throw()
    {
        var large = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 100.00), ReplayDays.Flat(ReplayDays.D2, 1e6));
        var absurd = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 100.00), ReplayDays.Flat(ReplayDays.D2, 1e20));

        Rep.Replay(Bare, large, ReplayDays.D1, ReplayDays.D2, options: new ReplayOptions(0.90, 1e300)).Record.Discontinuities.ShouldBeEmpty("a price move cannot exceed the clamped threshold");
        Rep.Replay(Bare, absurd, ReplayDays.D1, ReplayDays.D2).Record.Discontinuities.Count.ShouldBe(1, "an absurd price cannot be compared and fails closed");
    }
}
