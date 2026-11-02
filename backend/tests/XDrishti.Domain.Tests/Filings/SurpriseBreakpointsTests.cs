using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Calendar;
using static XDrishti.Domain.Tests.Filings.CalendarSamples;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>
/// P15-09 criterion 4: the quintile breakpoints of a season by the nearest-rank rule with ties to the higher quintile, NaN under 100 values, and the point-in-time property of the inputs: a filing broadcast after the date changes no quintile at it, while a visible filing of the previous
/// season can.
/// </summary>
public sealed class SurpriseBreakpointsTests
{
    private static readonly TradingCalendar Plain = new();

    private static readonly DateOnly PreviousQuarterEnd = new(2026, 3, 31);

    private static readonly DateOnly CurrentQuarterEnd = new(2026, 6, 30);

    [Fact]
    public void Values_one_to_two_hundred_give_the_breakpoints_forty_eighty_one_twenty_and_one_sixty()
    {
        var breakpoints = SurpriseBreakpoints.Of(Enumerable.Range(1, 200).Select(i => (double)i));

        breakpoints.ShouldBe([40.0, 80.0, 120.0, 160.0]);
    }

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(39.9, 1)]
    [InlineData(40, 2)]
    [InlineData(79, 2)]
    [InlineData(80, 3)]
    [InlineData(120, 4)]
    [InlineData(159, 4)]
    [InlineData(160, 5)]
    [InlineData(1000, 5)]
    public void A_value_equal_to_a_breakpoint_goes_to_the_higher_quintile(double value, int quintile)
    {
        var breakpoints = SurpriseBreakpoints.Of(Enumerable.Range(1, 200).Select(i => (double)i));

        SurpriseBreakpoints.QuintileOf(value, breakpoints).ShouldBe(quintile);
    }

    [Fact]
    public void Fewer_than_a_hundred_finite_values_give_nan_breakpoints_and_a_nan_quintile()
    {
        var ninetyNine = SurpriseBreakpoints.Of(Enumerable.Range(1, 99).Select(i => (double)i));
        var withNan = SurpriseBreakpoints.Of(Enumerable.Range(1, 99).Select(i => (double)i).Append(double.NaN).Append(double.PositiveInfinity));

        ninetyNine.ShouldAllBe(x => double.IsNaN(x));
        withNan.ShouldAllBe(x => double.IsNaN(x), "a NaN or an infinity is not a value of the season");
        SurpriseBreakpoints.QuintileOf(5, ninetyNine).ShouldBeNaN();
        SurpriseBreakpoints.Of(Enumerable.Range(1, 100).Select(i => (double)i)).ShouldAllBe(x => double.IsFinite(x));
    }

    [Fact]
    public void A_nan_value_has_no_quintile()
    {
        SurpriseBreakpoints.QuintileOf(double.NaN, [40, 80, 120, 160]).ShouldBeNaN();
    }

    private static SurpriseObservation Previous(int n) =>
        new(Guid.CreateVersion7(), PreviousQuarterEnd, new DateTimeOffset(2026, 4, 1, 16, 0, 0, Ist).AddDays(n % 30), n, n * 2.0);

    private static SurpriseObservation Current(Guid instrument, DateTimeOffset broadcast, double sue = 170, double car3 = 50) =>
        new(instrument, CurrentQuarterEnd, broadcast, sue, car3);

    private static SurpriseInputs Inputs(IEnumerable<SurpriseObservation> observations) => SurpriseInputs.Create(observations, Lag, Plain);

    [Fact]
    public void The_quintile_of_a_filing_is_taken_against_the_previous_season_only()
    {
        var instrument = Guid.CreateVersion7();
        var current = Current(instrument, new DateTimeOffset(2026, 7, 24, 16, 12, 0, Ist));
        var inputs = Inputs(Enumerable.Range(1, 200).Select(Previous).Append(current));

        var (sue, car3) = inputs.QuintilesAt(inputs.Find(instrument, CurrentQuarterEnd, current.BroadcastTs)!, new DateOnly(2026, 7, 27));

        sue.ShouldBe(5, "170 is above the 160 breakpoint of the previous season");
        car3.ShouldBe(1, "50 is below the 80 breakpoint of 2 x 1..200");
    }

    [Fact]
    public void A_filing_broadcast_after_the_date_in_any_season_changes_no_quintile_at_the_date()
    {
        var instrument = Guid.CreateVersion7();
        var current = Current(instrument, new DateTimeOffset(2026, 7, 24, 16, 12, 0, Ist));
        var date = new DateOnly(2026, 7, 27);
        var baseline = Inputs(Enumerable.Range(1, 200).Select(Previous).Append(current));
        var expected = baseline.QuintilesAt(baseline.Find(instrument, CurrentQuarterEnd, current.BroadcastTs)!, date);

        // a hundred late filings of the previous season (broadcast and reaction after the date) and a hundred very low ones of the current season
        var late = Enumerable.Range(0, 100).Select(i => new SurpriseObservation(Guid.CreateVersion7(), PreviousQuarterEnd, new DateTimeOffset(2026, 8, 3, 16, 0, 0, Ist).AddMinutes(i), -1000 - i, -1000 - i));
        var sameSeason = Enumerable.Range(0, 100).Select(i => new SurpriseObservation(Guid.CreateVersion7(), CurrentQuarterEnd, new DateTimeOffset(2026, 7, 30, 16, 0, 0, Ist).AddMinutes(i), -1000 - i, -1000 - i));
        var other = Inputs(Enumerable.Range(1, 200).Select(Previous).Append(current).Concat(late).Concat(sameSeason));

        other.QuintilesAt(other.Find(instrument, CurrentQuarterEnd, current.BroadcastTs)!, date).ShouldBe(expected);
    }

    [Fact]
    public void A_visible_filing_of_the_previous_season_can_change_the_quintile_as_the_control()
    {
        var instrument = Guid.CreateVersion7();
        var current = Current(instrument, new DateTimeOffset(2026, 7, 24, 16, 12, 0, Ist), sue: 161);
        var date = new DateOnly(2026, 7, 27);
        var baseline = Inputs(Enumerable.Range(1, 200).Select(Previous).Append(current));
        var visible = Enumerable.Range(0, 100).Select(i => new SurpriseObservation(Guid.CreateVersion7(), PreviousQuarterEnd, new DateTimeOffset(2026, 5, 4, 16, 0, 0, Ist).AddMinutes(i), 10_000 + i, 10_000 + i));
        var other = Inputs(Enumerable.Range(1, 200).Select(Previous).Append(current).Concat(visible));

        baseline.QuintilesAt(baseline.Find(instrument, CurrentQuarterEnd, current.BroadcastTs)!, date).Sue.ShouldBe(5);
        other.QuintilesAt(other.Find(instrument, CurrentQuarterEnd, current.BroadcastTs)!, date).Sue.ShouldBeLessThan(5, "a hundred higher values moved the breakpoints up");
    }

    [Fact]
    public void A_previous_season_that_is_still_reporting_has_fewer_than_a_hundred_visible_values_and_ranks_nothing()
    {
        var instrument = Guid.CreateVersion7();
        var current = Current(instrument, new DateTimeOffset(2026, 4, 20, 16, 12, 0, Ist));
        var inputs = Inputs(Enumerable.Range(1, 200).Select(Previous).Append(current with { PeriodEnd = PreviousQuarterEnd.AddMonths(3) }));

        // on 2026-04-10 only the previous-season filings broadcast by 2026-04-08 are visible: about sixty
        var (sue, car3) = inputs.QuintilesAt(inputs.Find(instrument, CurrentQuarterEnd, current.BroadcastTs)!, new DateOnly(2026, 4, 10));

        sue.ShouldBeNaN();
        car3.ShouldBeNaN();
    }

    [Fact]
    public void The_series_carries_the_quintiles_of_the_newest_visible_filing_and_none_before_it_is_visible()
    {
        var instrument = Guid.CreateVersion7();
        var broadcast = new DateTimeOffset(2026, 7, 24, 16, 12, 0, Ist);
        var current = Current(instrument, broadcast);
        var inputs = Inputs(Enumerable.Range(1, 200).Select(Previous).Append(current));
        var filing = new ResultsFilingTime(instrument, CurrentQuarterEnd, broadcast);

        var points = ResultsDaySeries.Build([], [filing], Lag, Plain, new DateOnly(2026, 7, 20), new DateOnly(2026, 8, 5), inputs).Points;

        points[new DateOnly(2026, 7, 24)].SueQuintile.ShouldBeNaN("not visible at the start of Friday");
        points[new DateOnly(2026, 7, 27)].DaysSinceResults.ShouldBe(0);
        points[new DateOnly(2026, 7, 27)].SueQuintile.ShouldBe(5, "the reaction session itself: the quintile as of its close");
        points[new DateOnly(2026, 7, 28)].SueQuintile.ShouldBe(5);
        ResultsDaySeries.Build([], [filing], Lag, Plain, new DateOnly(2026, 7, 20), new DateOnly(2026, 8, 5)).Points[new DateOnly(2026, 7, 27)].SueQuintile.ShouldBeNaN("without surprise inputs there is no quintile");
    }

    [Fact]
    public void The_hash_of_the_inputs_changes_with_any_number_and_not_with_the_order()
    {
        var a = Enumerable.Range(1, 5).Select(Previous).ToList();

        Inputs(a).Sha256.ShouldBe(Inputs(a.AsEnumerable().Reverse()).Sha256);
        Inputs(a).Sha256.ShouldNotBe(Inputs([.. a.Take(4), a[4] with { Sue = 99 }]).Sha256);
        Inputs([.. a.Take(4), a[4] with { Unverified = true }]).UnverifiedRows.ShouldBe(1);
        Inputs(a).Rows.ShouldBe(5);
    }

    [Fact]
    public void A_revision_of_the_same_quarter_counts_once_and_the_earliest_filing_is_the_one_ranked()
    {
        var instrument = Guid.CreateVersion7();
        var first = Current(instrument, new DateTimeOffset(2026, 7, 24, 16, 12, 0, Ist), sue: 170);
        var revision = Current(instrument, new DateTimeOffset(2026, 8, 3, 16, 0, 0, Ist), sue: 5);
        var inputs = Inputs(Enumerable.Range(1, 200).Select(Previous).Append(revision).Append(first));

        inputs.Rows.ShouldBe(201, "one filing for the instrument and the season");
        inputs.Find(instrument, CurrentQuarterEnd, revision.BroadcastTs)!.Sue.ShouldBe(170, "a later revision reads the numbers of the first");
        var baseList = Enumerable.Range(1, 200).Select(Previous).ToList();
        var previousAgain = Inputs(baseList.Concat(baseList.Select(o => o with { BroadcastTs = o.BroadcastTs.AddDays(1), Sue = -o.Sue })));
        previousAgain.Rows.ShouldBe(200, "a revision of each previous-season filing does not double the sample");
    }

    [Fact]
    public void Seasons_with_fewer_than_a_hundred_finite_values_are_counted_as_thin()
    {
        var thin = Enumerable.Range(1, 50).Select(n => new SurpriseObservation(Guid.CreateVersion7(), CurrentQuarterEnd, new DateTimeOffset(2026, 7, 24, 16, 0, 0, Ist).AddMinutes(n), n, n));
        var inputs = Inputs(Enumerable.Range(1, 200).Select(Previous).Concat(thin));

        (inputs.Seasons, inputs.ThinSeasons).ShouldBe((2, 1));
    }

    [Fact]
    public void A_filing_broadcast_before_the_open_of_its_reaction_session_is_counted_as_missed()
    {
        var instrument = Guid.CreateVersion7();
        var evening = new ResultsFilingTime(instrument, CurrentQuarterEnd, new DateTimeOffset(2026, 7, 24, 16, 12, 0, Ist));
        var preOpen = new ResultsFilingTime(instrument, PreviousQuarterEnd, new DateTimeOffset(2026, 4, 27, 8, 0, 0, Ist));
        var noPeriod = new ResultsFilingTime(instrument, null, new DateTimeOffset(2026, 5, 4, 8, 0, 0, Ist));
        var inputs = ResultsDayInputs.Create(null, [evening, preOpen, noPeriod], 5);

        inputs.CountMissedReactionDays(Plain, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).ShouldBe((1, 2));
        inputs.CountMissedReactionDays(Plain, new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31)).ShouldBe((0, 1));
    }
}
