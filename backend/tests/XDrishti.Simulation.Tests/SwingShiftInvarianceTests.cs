using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 3 of P3-03d, the metamorphic test: for every row of ADR 0038 whose exit is a stop or a target and whose fill minute is not the exit minute, everything after the fill minute is moved to the next date at the same
/// times of day (the rest of the fill day repeats the fill minute's close, the next day opens at that close and is flat until the listed minutes, a hole stays at its times). A night inserted between the entry and the exit, with
/// no gap, changes nothing but the sessions held.
/// </summary>
public sealed class SwingShiftInvarianceTests
{
    private static readonly string[] Names = ["H1", "H3", "H4", "H5", "H6", "H7", "H9", "H12", "H13", "H15", "H17", "H18", "H20", "H21", "H22", "H23", "H25"];

    public static TheoryData<string> Rows => [.. Names];

    private static (Bar[] Bars, int FillIndex) Shifted(Hand hand)
    {
        var fillRow = hand.ListedRows.First(r => !r.EndsWith(" v0", StringComparison.Ordinal) && Day.Minute(r[..5]) >= Day.Minute(hand.Decision));
        var fillMinute = Day.Minute(fillRow[..5]);
        var fillClose = double.Parse(fillRow.Split(' ')[1].Split('/')[3], System.Globalization.CultureInfo.InvariantCulture);
        var first = Day.Dense(Mon).Start(hand.TriggerClose);
        var second = Day.Dense(Tue).Start(fillClose);
        foreach (var row in hand.ListedRows)
        {
            var target = Day.Minute(row[..5]) <= fillMinute ? first : second;
            if (row.EndsWith(" v0", StringComparison.Ordinal))
            {
                target.SetEmpty(row[..^3]);
            }
            else
            {
                target.Set(row);
            }
        }

        foreach (var (from, to) in hand.AbsentRanges)
        {
            second.Absent(from, to);
        }

        var bars = Day.Join(first, second);
        return (bars, Array.FindIndex(bars, b => b.EpochSeconds == Day.Epoch(Mon, fillMinute)));
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_same_trade_with_a_night_inserted_has_the_same_prices_ratios_excursions_and_counters_and_two_sessions(string name)
    {
        var (hand, _) = HandCases.Get(name);
        var original = hand.Walk(FillRules.Default);
        var (bars, fillIndex) = Shifted(hand);
        var rules = new WalkRules(hand.RewardRisk, WalkRules.NoSquareOff, FillRules.Default, TickSpec.FromRupees(hand.Tick).Value, SwingHold.Create(5, null).Value);

        var result = TradeWalker.Walk(hand.Candidate(bars), bars, rules);

        result.IsSuccess.ShouldBeTrue(name + ": " + (result.IsFailure ? result.Error.Code : string.Empty));
        var swing = result.Value;
        var was = original.Trade.ShouldNotBeNull(name);
        var now = swing.Trade.ShouldNotBeNull(name);
        now.ExitReason.ShouldBe(was.ExitReason, name);
        now.ExitPrice.ShouldBe(was.ExitPrice, name);
        now.ExitFilledPrice.ShouldBe(was.ExitFilledPrice, name);
        now.EntryPrice.ShouldBe(was.EntryPrice, name);
        now.EntryFilledPrice.ShouldBe(was.EntryFilledPrice, name);
        now.EntryFill.ShouldBe(was.EntryFill, name);
        now.StopPrice.ShouldBe(was.StopPrice, name);
        now.TargetPrice.ShouldBe(was.TargetPrice, name);
        now.RExact.ShouldBe(was.RExact, name);
        now.RFilled.ShouldBe(was.RFilled, name);
        now.SlippageR.ShouldBe(was.SlippageR, name);
        now.MaeR.ShouldBe(was.MaeR, name);
        now.MfeR.ShouldBe(was.MfeR, name);
        now.ExitEpochSeconds.ShouldBe(was.ExitEpochSeconds + 86_400, name + ": the same time of day, one date later");
        (now.SessionsHeld, now.SessionsUnseen).ShouldBe((2, 0), name);
        swing.OvernightStopGap.ShouldBeFalse(name);
        swing.OvernightTargetGap.ShouldBeFalse(name);
        swing.ToCounters().OvernightStopGaps.ShouldBe(0, name);
        swing.ToCounters().OvernightTargetGaps.ShouldBe(0, name);

        var a = original.ToCounters();
        var b = swing.ToCounters();
        (b.EntriesAtOpen, b.EntriesAtLimit, b.EntriesAfterMissingMinutes, b.StopGapExits, b.TargetGapExits, b.AmbiguousMinutes, b.LimitNotReached, b.RiskUnderFourTicks, b.ZeroVolumeMinutes, b.ExitsStop, b.ExitsTarget)
            .ShouldBe((a.EntriesAtOpen, a.EntriesAtLimit, a.EntriesAfterMissingMinutes, a.StopGapExits, a.TargetGapExits, a.AmbiguousMinutes, a.LimitNotReached, a.RiskUnderFourTicks, a.ZeroVolumeMinutes, a.ExitsStop, a.ExitsTarget), name);

        // BarsExamined cannot be equal (the trade spans two days): it is the minutes that hold a trade from the fill minute to 15:29 on day 1 plus those from 09:15 to the exit minute on day 2, counted from the bars.
        var expected = bars.Skip(fillIndex).Count(x => SessionClock.DateOf(x.EpochSeconds) == Mon && x.Volume > 0)
            + bars.Count(x => SessionClock.DateOf(x.EpochSeconds) == Tue && x.EpochSeconds <= now.ExitEpochSeconds && x.Volume > 0);
        swing.BarsExamined.ShouldBe(expected, name);
    }

    [Fact]
    public void The_rows_that_end_at_the_square_off_or_in_the_fill_minute_are_left_out_and_the_others_are_all_there()
    {
        var left = new[] { "H2", "H8", "H10", "H11", "H14", "H16", "H19", "H24" };

        Names.Concat(left).Order().ShouldBe(HandCases.All.Select(c => c.Hand.Name).Order());
        Names.Length.ShouldBe(17);
    }

    [Fact]
    public void A_gap_inserted_between_the_days_does_change_the_result_so_the_test_is_not_vacuous()
    {
        // H3 shifted with the next day opening through the stop: the exit is at the open and counted as an overnight gap.
        var (hand, _) = HandCases.Get("H3");
        var bars = Day.Join(Day.Dense(Mon).Start(hand.TriggerClose).Set("10:00 100.00/100.30/99.80/100.20"), Day.Dense(Tue).Start(100.20).Set("09:15 98.60/98.90/98.40/98.50"));
        var rules = new WalkRules(hand.RewardRisk, WalkRules.NoSquareOff, FillRules.Default, TickSpec.FromRupees(hand.Tick).Value, SwingHold.Create(5, null).Value);

        var outcome = TradeWalker.Walk(hand.Candidate(bars), bars, rules).Value;

        outcome.OvernightStopGap.ShouldBeTrue();
        outcome.Trade!.ExitPrice.ShouldBe(98.60);
    }
}
