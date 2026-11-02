using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>P15-05 criterion 6: the two results-event features of the frame read the caller's per-date series, are NaN (unknown) on a date it does not hold, and are refused at <c>Create</c> without it, as <c>is_expiry_day</c> is.</summary>
public sealed class FrameResultsDaysTests
{
    private static readonly DateOnly Mon = new(2026, 8, 3);

    private static readonly DateOnly Tue = new(2026, 8, 4);

    private static readonly DateOnly Fri = new(2026, 8, 7);

    private static FeatureFrame Frame(IReadOnlyDictionary<DateOnly, ResultsDayValues> series) =>
        FeatureFrame.Create([new FeatureSpec("days_to_results"), new FeatureSpec("days_since_results")], new FrameOptions(ResultsDays: series)).Value;

    private static readonly Dictionary<DateOnly, ResultsDayValues> Series = new()
    {
        [Mon] = new ResultsDayValues(double.NaN, double.NaN),
        [Tue] = new ResultsDayValues(6, double.NaN),
        [Fri] = new ResultsDayValues(3, 2),
    };

    [Fact]
    public void The_frame_reads_the_series_of_the_date_of_the_step_for_the_whole_date()
    {
        var frame = Frame(Series);

        frame.Step(Minutes.Close(Fri, Minutes.Open, 100));
        frame[0].ShouldBe(3.0);
        frame[1].ShouldBe(2.0);

        frame.Step(Minutes.Close(Fri, Minutes.Open + 200, 101));
        frame[0].ShouldBe(3.0, "the value is that of the date and does not change inside it");
    }

    [Fact]
    public void A_date_the_series_does_not_hold_and_a_nan_in_it_are_unknown_never_zero()
    {
        var frame = Frame(Series);

        frame.Step(Minutes.Close(Mon, Minutes.Open, 100));
        double.IsNaN(frame[0]).ShouldBeTrue("the series holds NaN for the third");
        double.IsNaN(frame[1]).ShouldBeTrue();

        frame.Step(Minutes.Close(Tue, Minutes.Open, 100));
        frame[0].ShouldBe(6.0);
        double.IsNaN(frame[1]).ShouldBeTrue();

        frame.Step(Minutes.Close(new DateOnly(2026, 8, 10), Minutes.Open, 100));
        double.IsNaN(frame[0]).ShouldBeTrue("a date outside the series is unknown");
    }

    [Fact]
    public void Without_a_series_the_frame_is_refused_on_the_option_and_an_empty_series_is_allowed()
    {
        var refused = FeatureFrame.Create([new FeatureSpec("days_to_results")]);

        refused.IsFailure.ShouldBeTrue();
        var field = refused.Error.Fields.ShouldNotBeNull().Single();
        field.Field.ShouldBe("options.resultsDays");
        FeatureFrame.Create([new FeatureSpec("days_since_results")], new FrameOptions(ResultsDays: new Dictionary<DateOnly, ResultsDayValues>())).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_two_features_are_catalogued_as_date_driven_one_minute_features_that_do_not_read_the_context()
    {
        foreach (var name in new[] { "days_to_results", "days_since_results" })
        {
            IndicatorCatalog.TryGet(name, out var info).ShouldBeTrue(name);
            info.DateDriven.ShouldBeTrue(name);
            info.RequiresResultsDays.ShouldBeTrue(name);
            info.RequiresContext.ShouldBeFalse(name);
            info.RequiresExpiryDays.ShouldBeFalse(name);
            info.SupportedTimeframes.ShouldBe([Timeframe.M1], name);
        }
    }
}
