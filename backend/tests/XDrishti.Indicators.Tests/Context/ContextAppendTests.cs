using XDrishti.Domain.Common;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Context;

/// <summary>
/// Criterion 12: <c>Append</c> and <c>Create</c> refuse a date that is not after the last one, a high below the low, a close outside the high / low, a non-positive or non-finite index price or VIX and a breadth
/// outside 0 to 100 — with problem codes, never with an exception, and leaving the context exactly as it was. <c>Create</c> returns the first problem and names the record; bad options are named field by field.
/// </summary>
public sealed class ContextAppendTests
{
    private static readonly DateOnly First = ContextSeriesGenerator.Weekday(0);

    private static readonly DateOnly Second = ContextSeriesGenerator.Weekday(1);

    /// <summary>A record after <see cref="First"/> (a flat day around 100) that the context must refuse: why, the record, the problem code and the field.</summary>
    private static readonly (string Reason, ContextDay? Day, string Code, string Field)[] RefusedRows =
    [
        ("same date as the last", Day(First), MarketContext.DateNotIncreasingCode, "day.date"),
        ("date before the last", Day(First.AddDays(-1)), MarketContext.DateNotIncreasingCode, "day.date"),
        ("high below the low", Day(Second, high: 98, low: 99, close: 99), MarketContext.HighBelowLowCode, "day.indexHigh"),
        ("close above the high", Day(Second, close: 101.5), MarketContext.CloseOutsideRangeCode, "day.indexClose"),
        ("close below the low", Day(Second, close: 98.5), MarketContext.CloseOutsideRangeCode, "day.indexClose"),
        ("zero high", Day(Second, high: 0, low: 0, close: 0), MarketContext.PriceNotPositiveCode, "day.indexHigh"),
        ("zero low", Day(Second, low: 0), MarketContext.PriceNotPositiveCode, "day.indexLow"),
        ("negative low", Day(Second, low: -1), MarketContext.PriceNotPositiveCode, "day.indexLow"),
        ("negative close", Day(Second, close: -100), MarketContext.PriceNotPositiveCode, "day.indexClose"),
        ("NaN high", Day(Second, high: double.NaN), MarketContext.PriceNotPositiveCode, "day.indexHigh"),
        ("NaN close", Day(Second, close: double.NaN), MarketContext.PriceNotPositiveCode, "day.indexClose"),
        ("infinite high", Day(Second, high: double.PositiveInfinity), MarketContext.PriceNotPositiveCode, "day.indexHigh"),
        ("negative infinite low", Day(Second, low: double.NegativeInfinity), MarketContext.PriceNotPositiveCode, "day.indexLow"),
        ("zero VIX", Day(Second, vix: 0), MarketContext.VixNotPositiveCode, "day.vixClose"),
        ("negative VIX", Day(Second, vix: -12), MarketContext.VixNotPositiveCode, "day.vixClose"),
        ("NaN VIX", Day(Second, vix: double.NaN), MarketContext.VixNotPositiveCode, "day.vixClose"),
        ("infinite VIX", Day(Second, vix: double.PositiveInfinity), MarketContext.VixNotPositiveCode, "day.vixClose"),
        ("breadth below 0", Day(Second, breadth: -0.1), MarketContext.BreadthOutOfRangeCode, "day.breadth"),
        ("breadth above 100", Day(Second, breadth: 100.1), MarketContext.BreadthOutOfRangeCode, "day.breadth"),
        ("NaN breadth", Day(Second, breadth: double.NaN), MarketContext.BreadthOutOfRangeCode, "day.breadth"),
        ("infinite breadth", Day(Second, breadth: double.PositiveInfinity), MarketContext.BreadthOutOfRangeCode, "day.breadth"),
        ("no record", null, MarketContext.MissingDayCode, "day"),
    ];

    public static TheoryData<string> Reasons { get; } = [.. RefusedRows.Select(r => r.Reason)];

    private static ContextDay Day(DateOnly date, double high = 101, double low = 99, double close = 100, double? vix = null, double? breadth = null) => new(date, high, low, close, vix, breadth);

    [Theory]
    [MemberData(nameof(Reasons))]
    public void Append_refuses_the_record_with_a_problem_code_and_names_the_field(string reason)
    {
        var (_, day, code, field) = RefusedRows.Single(r => r.Reason == reason);
        var context = MarketContext.Create([Day(First)]).Value;

        var result = context.Append(day!);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Fields.ShouldNotBeNull().Select(f => f.Field).ToList().ShouldBe([field]);
        result.Error.Message.ShouldNotBeNullOrWhiteSpace();
        context.Count.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Reasons))]
    public void Create_refuses_the_same_record_with_the_same_code_and_names_it_in_the_sequence(string reason)
    {
        var (_, day, code, field) = RefusedRows.Single(r => r.Reason == reason);

        var result = MarketContext.Create([Day(First), day!]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Fields.ShouldNotBeNull().Select(f => f.Field).ToList().ShouldBe([field.Replace("day", "days[1]", StringComparison.Ordinal)]);
    }

    [Fact]
    public void A_refused_record_changes_nothing_so_the_following_good_records_give_what_they_would_have_given_anyway()
    {
        var series = ContextSeriesGenerator.Random(5, 120, 120);
        var control = ContextRead.Days(MarketContext.Create(series, Small).Value, series);
        var last = series[59];
        var next = last with { Date = last.Date.AddDays(1) };
        var refused = new (string Reason, ContextDay? Day)[]
        {
            ("same date", last),
            ("earlier date", last with { Date = last.Date.AddDays(-1) }),
            ("high below low", next with { IndexHigh = 1, IndexLow = 2, IndexClose = 1.5 }),
            ("close above high", next with { IndexClose = last.IndexHigh * 2 }),
            ("zero price", next with { IndexLow = 0 }),
            ("bad VIX", next with { VixClose = -1 }),
            ("bad breadth", next with { Breadth = 101 }),
            ("no record", null),
        };

        foreach (var (reason, day) in refused)
        {
            var context = MarketContext.Create(series.Take(60), Small).Value;
            context.Append(day!).IsFailure.ShouldBeTrue(reason);
            context.Count.ShouldBe(60, reason);
            foreach (var good in series.Skip(60))
            {
                context.Append(good).IsSuccess.ShouldBeTrue(reason);
            }

            var actual = ContextRead.Days(context, series);
            for (var i = 0; i < series.Count; i++)
            {
                ContextRead.FirstDifference(control[i], actual[i]).ShouldBeNull($"{reason}: day {i}");
            }
        }
    }

    [Fact]
    public void Append_accepts_a_later_date_after_a_gap_and_counts_the_day()
    {
        var context = MarketContext.Create([Day(First)]).Value;

        context.Append(Day(First.AddDays(10))).IsSuccess.ShouldBeTrue();

        context.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(100, 100, 100)]
    [InlineData(101, 99, 101)]
    [InlineData(101, 99, 99)]
    [InlineData(1e-9, 1e-9, 1e-9)]
    public void A_flat_day_and_a_close_on_the_high_or_the_low_are_possible_days(double high, double low, double close)
    {
        var context = MarketContext.Create([Day(First)]).Value;

        context.Append(Day(Second, high, low, close)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(50.5)]
    public void A_breadth_of_zero_and_of_one_hundred_is_inside_the_range(double breadth)
    {
        var context = MarketContext.Create([Day(First)]).Value;

        context.Append(Day(Second, breadth: breadth)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_tiny_positive_vix_is_accepted()
    {
        var context = MarketContext.Create([Day(First)]).Value;

        context.Append(Day(Second, vix: double.Epsilon)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_record_with_several_problems_reports_the_first_in_the_order_date_prices_range_close_vix_breadth()
    {
        var context = MarketContext.Create([Day(First)]).Value;

        context.Append(Day(First, high: -1, vix: -1, breadth: 200)).Error.Code.ShouldBe(MarketContext.DateNotIncreasingCode);
        context.Append(Day(Second, high: -1, vix: -1, breadth: 200)).Error.Code.ShouldBe(MarketContext.PriceNotPositiveCode);
        context.Append(Day(Second, high: 98, low: 99, close: 150, vix: -1, breadth: 200)).Error.Code.ShouldBe(MarketContext.HighBelowLowCode);
        context.Append(Day(Second, close: 150, vix: -1, breadth: 200)).Error.Code.ShouldBe(MarketContext.CloseOutsideRangeCode);
        context.Append(Day(Second, vix: -1, breadth: 200)).Error.Code.ShouldBe(MarketContext.VixNotPositiveCode);
        context.Append(Day(Second, breadth: 200)).Error.Code.ShouldBe(MarketContext.BreadthOutOfRangeCode);
    }

    [Fact]
    public void Create_returns_the_first_problem_and_names_the_record_and_stops_reading()
    {
        var read = 0;
        IEnumerable<ContextDay> Days()
        {
            foreach (var day in new[] { Day(First), Day(Second), Day(ContextSeriesGenerator.Weekday(2), close: 500), Day(ContextSeriesGenerator.Weekday(3), vix: -3), Day(ContextSeriesGenerator.Weekday(4)) })
            {
                read++;
                yield return day;
            }
        }

        var result = MarketContext.Create(Days());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(MarketContext.CloseOutsideRangeCode);
        result.Error.Fields.ShouldNotBeNull().Select(f => f.Field).ToList().ShouldBe(["days[2].indexClose"]);
        read.ShouldBe(3);
    }

    [Fact]
    public void Create_refuses_dates_that_do_not_increase_across_the_sequence()
    {
        var result = MarketContext.Create([Day(First), Day(Second), Day(Second)]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(MarketContext.DateNotIncreasingCode);
        result.Error.Fields.ShouldNotBeNull().Select(f => f.Field).ToList().ShouldBe(["days[2].date"]);
    }

    [Fact]
    public void Create_refuses_a_missing_sequence_and_a_missing_record_and_accepts_an_empty_one()
    {
        MarketContext.Create(null!).Error.Code.ShouldBe(MarketContext.MissingDayCode);

        var withNull = MarketContext.Create([Day(First), null!]);
        withNull.Error.Code.ShouldBe(MarketContext.MissingDayCode);
        withNull.Error.Fields.ShouldNotBeNull().Select(f => f.Field).ToList().ShouldBe(["days[1]"]);

        var empty = MarketContext.Create([]);
        empty.IsSuccess.ShouldBeTrue();
        empty.Value.Count.ShouldBe(0);
        empty.Value.TryAsOf(First, out _).ShouldBeFalse();
    }

    private static readonly ContextOptions Small = new(TrendSmaPeriod: 8, TrendSlopeDays: 3, AdxPeriod: 5, AdxMin: 20, AtrPeriod: 5, PercentileWindow: 20);

    private static readonly (string Reason, ContextOptions Options, string Field)[] BadOptionRows =
    [
        ("average of 0 days", new ContextOptions(TrendSmaPeriod: 0), "options.trendSmaPeriod"),
        ("negative average", new ContextOptions(TrendSmaPeriod: -3), "options.trendSmaPeriod"),
        ("average above the limit", new ContextOptions(TrendSmaPeriod: IndicatorCatalog.MaxPeriod + 1), "options.trendSmaPeriod"),
        ("slope of 0 days", new ContextOptions(TrendSlopeDays: 0), "options.trendSlopeDays"),
        ("ADX period 0", new ContextOptions(AdxPeriod: 0), "options.adxPeriod"),
        ("ADX limit NaN", new ContextOptions(AdxMin: double.NaN), "options.adxMin"),
        ("ADX limit negative", new ContextOptions(AdxMin: -1), "options.adxMin"),
        ("ADX limit above 100", new ContextOptions(AdxMin: 100.5), "options.adxMin"),
        ("ATR period 0", new ContextOptions(AtrPeriod: 0), "options.atrPeriod"),
        ("window of 0", new ContextOptions(PercentileWindow: 0), "options.percentileWindow"),
        ("window above the limit", new ContextOptions(PercentileWindow: IndicatorCatalog.MaxPeriod + 1), "options.percentileWindow"),
        ("low limit negative", new ContextOptions(LowVolBelow: -1), "options.lowVolBelow"),
        ("low limit above 100", new ContextOptions(LowVolBelow: 101, HighVolFrom: 100), "options.lowVolBelow"),
        ("high limit infinite", new ContextOptions(HighVolFrom: double.PositiveInfinity), "options.highVolFrom"),
        ("low limit above the high limit", new ContextOptions(LowVolBelow: 70, HighVolFrom: 30), "options.lowVolBelow"),
    ];

    public static TheoryData<string> BadOptionReasons { get; } = [.. BadOptionRows.Select(r => r.Reason)];

    [Theory]
    [MemberData(nameof(BadOptionReasons))]
    public void Create_refuses_options_that_break_a_limit_and_names_the_field(string reason)
    {
        var (_, options, field) = BadOptionRows.Single(r => r.Reason == reason);

        var result = MarketContext.Create([Day(First)], options);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(MarketContext.InvalidOptionsCode);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Fields.ShouldNotBeNull().Select(f => f.Field).ToList().ShouldBe([field]);
    }

    [Fact]
    public void Create_names_every_bad_option_at_once()
    {
        var result = MarketContext.Create([], new ContextOptions(TrendSmaPeriod: 0, AdxPeriod: -1, PercentileWindow: 0));

        result.Error.Fields.ShouldNotBeNull().Select(f => f.Field).ToList().ShouldBe(["options.trendSmaPeriod", "options.adxPeriod", "options.percentileWindow"]);
    }

    [Fact]
    public void The_options_are_checked_before_the_days_are_read()
    {
        var result = MarketContext.Create(null!, new ContextOptions(AtrPeriod: 0));

        result.Error.Code.ShouldBe(MarketContext.InvalidOptionsCode);
    }

    [Fact]
    public void The_largest_periods_and_the_edge_limits_are_accepted()
    {
        var largest = new ContextOptions(
            IndicatorCatalog.MaxPeriod, IndicatorCatalog.MaxPeriod, IndicatorCatalog.MaxPeriod, AdxMin: 100, AtrPeriod: IndicatorCatalog.MaxPeriod, PercentileWindow: IndicatorCatalog.MaxPeriod, LowVolBelow: 0, HighVolFrom: 0);

        MarketContext.Create([Day(First)], largest).IsSuccess.ShouldBeTrue();
        MarketContext.Create([Day(First)], new ContextOptions(1, 1, 1, 0, 1, 1, 100, 100)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_defaults_are_those_of_the_spec_and_the_context_remembers_its_options()
    {
        var defaults = MarketContext.Create([]).Value.Options;

        defaults.ShouldBe(new ContextOptions());
        defaults.TrendSmaPeriod.ShouldBe(50);
        defaults.TrendSlopeDays.ShouldBe(5);
        defaults.AdxPeriod.ShouldBe(14);
        defaults.AdxMin.ShouldBe(20);
        defaults.AtrPeriod.ShouldBe(14);
        defaults.PercentileWindow.ShouldBe(252);
        defaults.LowVolBelow.ShouldBe(100.0 / 3);
        defaults.HighVolFrom.ShouldBe(200.0 / 3);
        MarketContext.Create([], new ContextOptions(TrendSmaPeriod: 7)).Value.Options.TrendSmaPeriod.ShouldBe(7);
    }
}
