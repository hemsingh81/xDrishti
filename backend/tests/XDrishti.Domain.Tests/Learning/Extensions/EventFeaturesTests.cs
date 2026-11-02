using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Tests.Filings;
using static XDrishti.Domain.Tests.Filings.CalendarSamples;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>P15-05 criteria 2 and 3 for <c>ev-1</c>: days to and since results, <c>results_car3</c> on hand values (to 1e-12), a split inside the window, a holiday, every NaN with its closed reason.</summary>
public sealed class EventFeaturesTests
{
    private const double Tolerance = 1e-12;

    private static readonly Guid Who = FilingSamples.Instrument;

    private static readonly DateOnly Wed22 = new(2026, 7, 22);

    private static readonly DateOnly Mon27 = new(2026, 7, 27);

    private static readonly DateTimeOffset FridayEvening = new(2026, 7, 24, 16, 12, 0, Ist);

    private static ResultsFilingTime Filing(DateTimeOffset at, DateOnly? period = null) => new(Who, period ?? Q1, at.ToUniversalTime());

    private static EventFeatureInput Input(
        DateTimeOffset at,
        IReadOnlyList<ResultsFilingTime>? filings = null,
        IReadOnlyList<CalendarRow>? rows = null,
        TradingCalendar? calendar = null,
        Dictionary<DateOnly, double>? stock = null,
        Dictionary<DateOnly, double>? index = null,
        IReadOnlyList<AppliedAction>? actions = null) =>
        new(at, Lag, calendar ?? new TradingCalendar(), rows ?? [], filings ?? [], stock ?? [], index, actions ?? []);

    private static Dictionary<DateOnly, double> Closes(double start, double end) => new() { [Wed22] = start, [Mon27] = end };

    [Fact]
    public void Days_to_results_is_the_sessions_to_the_event_and_the_p15_06_window_table_row_for_the_seventh_of_august()
    {
        var rows = new[] { Row(1, new DateOnly(2026, 8, 12), At(8, 3, 11, 0)) };

        var cells = EventFeatures.Compute(Input(At(8, 7, 10, 30), rows: rows));

        cells[0].ShouldBe(FeatureCell.Known(3));
        EventFeatures.Compute(Input(At(8, 12, 10, 30), rows: rows))[0].ShouldBe(FeatureCell.Known(0));
    }

    [Fact]
    public void A_meeting_not_yet_intimated_is_unknown_with_the_calendar_reason_and_never_zero()
    {
        var rows = new[] { Row(1, new DateOnly(2026, 8, 12), At(8, 3, 11, 0)) };

        EventFeatures.Compute(Input(At(8, 3, 10, 59), rows: rows))[0].ShouldBe(FeatureCell.Unknown(FeatureReasons.NoCalendar));
        EventFeatures.Compute(Input(At(8, 3, 11, 4), rows: rows))[0].Reason.ShouldBe(FeatureReasons.NoCalendar, "intimation + 5 minutes lag is 11:05");
        EventFeatures.Compute(Input(At(8, 3, 11, 5), rows: rows))[0].Value.ShouldBe(7);
        EventFeatures.Compute(Input(At(8, 20), rows: rows))[0].Reason.ShouldBe(FeatureReasons.NoCalendar, "the meeting is past and nothing is upcoming");
    }

    [Fact]
    public void Days_since_results_counts_sessions_from_the_reaction_session_and_is_zero_on_it()
    {
        var filings = new[] { Filing(FridayEvening) };

        EventFeatures.Compute(Input(new DateTimeOffset(2026, 7, 27, 9, 20, 0, Ist), filings))[1].ShouldBe(FeatureCell.Known(0));
        EventFeatures.Compute(Input(new DateTimeOffset(2026, 7, 29, 10, 0, 0, Ist), filings))[1].ShouldBe(FeatureCell.Known(2));
        EventFeatures.Compute(Input(new DateTimeOffset(2026, 8, 3, 10, 0, 0, Ist), filings))[1].ShouldBe(FeatureCell.Known(5));
    }

    [Fact]
    public void Days_since_results_is_unknown_before_the_reaction_session_and_with_no_filing_visible()
    {
        var filings = new[] { Filing(FridayEvening) };

        EventFeatures.Compute(Input(new DateTimeOffset(2026, 7, 24, 18, 0, 0, Ist), filings))[1].ShouldBe(FeatureCell.Unknown(FeatureReasons.NoFiling), "R is Monday and today is Friday");
        EventFeatures.Compute(Input(new DateTimeOffset(2026, 7, 24, 16, 14, 0, Ist), filings))[1].Reason.ShouldBe(FeatureReasons.NoFiling, "16:12 + 5 minutes is 16:17: not yet visible");
        EventFeatures.Compute(Input(new DateTimeOffset(2026, 7, 29, 10, 0, 0, Ist)))[1].ShouldBe(FeatureCell.Unknown(FeatureReasons.NoFiling));
    }

    [Fact]
    public void A_holiday_reaction_session_moves_to_the_next_session_in_both_counts()
    {
        var holiday = new TradingCalendar([TradingDay.Holiday(Mon27, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)]);
        var filings = new[] { Filing(FridayEvening) };

        // R is Tuesday 07-28; Thursday is 2 sessions after it
        EventFeatures.Compute(Input(new DateTimeOffset(2026, 7, 30, 10, 0, 0, Ist), filings, calendar: holiday))[1].ShouldBe(FeatureCell.Known(2));
    }

    [Fact]
    public void Results_car3_is_the_stock_return_over_the_three_sessions_ending_at_the_reaction_close_minus_the_index_return()
    {
        // R = Monday 07-27; R-3 = Wednesday 07-22; stock 100 -> 110 (+10 %), index 20000 -> 20400 (+2 %)
        var input = Input(new DateTimeOffset(2026, 7, 27, 16, 0, 0, Ist), [Filing(FridayEvening)], stock: Closes(100, 110), index: Closes(20000, 20400));

        EventFeatures.Compute(input)[2].Value.ShouldBe(0.10 - 0.02, Tolerance);
    }

    [Fact]
    public void Results_car3_is_unknown_until_the_reaction_close_has_passed()
    {
        var before = Input(new DateTimeOffset(2026, 7, 27, 15, 29, 0, Ist), [Filing(FridayEvening)], stock: Closes(100, 110), index: Closes(20000, 20400));
        var at = before with { At = new DateTimeOffset(2026, 7, 27, 15, 30, 0, Ist) };

        EventFeatures.Compute(before)[2].ShouldBe(FeatureCell.Unknown(FeatureReasons.NoFiling));
        EventFeatures.Compute(at)[2].IsKnown.ShouldBeTrue("the close at 15:30 is at or before t");
    }

    [Fact]
    public void A_split_inside_the_window_is_taken_out_of_the_ratio()
    {
        // 1:2 split with the ex-date Friday 07-24: the raw close after it is 55 for the same stock that closed at 100 before it and 110 adjusted
        var split = new AppliedAction(new DateOnly(2026, 7, 24), CorporateActionKind.Split, 1, 2, 0.5m);
        var input = Input(new DateTimeOffset(2026, 7, 27, 16, 0, 0, Ist), [Filing(FridayEvening)], stock: Closes(100, 55), index: Closes(20000, 20400), actions: [split]);

        EventFeatures.Compute(input)[2].Value.ShouldBe(0.10 - 0.02, Tolerance);
    }

    [Fact]
    public void The_newest_filing_whose_reaction_close_has_passed_decides_the_car_while_days_since_follows_the_newest_filing()
    {
        var second = Filing(new DateTimeOffset(2026, 7, 27, 11, 0, 0, Ist), new DateOnly(2026, 9, 30));
        var input = Input(new DateTimeOffset(2026, 7, 27, 16, 0, 0, Ist), [Filing(FridayEvening), second], stock: Closes(100, 110), index: Closes(20000, 20400));

        var cells = EventFeatures.Compute(input);

        cells[1].ShouldBe(FeatureCell.Unknown(FeatureReasons.NoFiling), "the newest filing reacts tomorrow");
        cells[2].Value.ShouldBe(0.08, Tolerance, "the older filing's reaction close has passed");
    }

    [Fact]
    public void Missing_closes_say_which_input_is_missing_and_nothing_is_filled()
    {
        var at = new DateTimeOffset(2026, 7, 27, 16, 0, 0, Ist);
        var filings = new[] { Filing(FridayEvening) };

        EventFeatures.Compute(Input(at, filings, stock: new Dictionary<DateOnly, double> { [Mon27] = 110 }, index: Closes(20000, 20400)))[2].ShouldBe(FeatureCell.Unknown(FeatureReasons.NoBars));
        EventFeatures.Compute(Input(at, filings, stock: Closes(100, 110), index: new Dictionary<DateOnly, double> { [Mon27] = 20400 }))[2].ShouldBe(FeatureCell.Unknown(FeatureReasons.NoContext));
        EventFeatures.Compute(Input(at, filings, stock: Closes(100, 110), index: null))[2].ShouldBe(FeatureCell.Unknown(FeatureReasons.NoContext));
        EventFeatures.Compute(Input(at, filings, stock: Closes(0, 110), index: Closes(20000, 20400)))[2].ShouldBe(FeatureCell.Unknown(FeatureReasons.NoBars));
        EventFeatures.Compute(Input(at, [], stock: Closes(100, 110), index: Closes(20000, 20400)))[2].ShouldBe(FeatureCell.Unknown(FeatureReasons.NoFiling));
    }

    [Fact]
    public void The_catalog_names_are_the_columns_in_order()
    {
        EventFeatureCatalog.Names.ShouldBe(["days_to_results", "days_since_results", "results_car3"]);
        EventFeatureCatalog.Group.Version.ShouldBe("ev-1");
        EventFeatureCatalog.Group.Problem().ShouldBeNull();
        EventFeatures.Compute(Input(At(8, 7))).Length.ShouldBe(EventFeatureCatalog.Names.Count);
    }
}
