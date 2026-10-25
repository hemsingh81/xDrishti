using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>P8-07: the equal-weight, no-rebalancing price return of a set of instruments, worked by hand.</summary>
public sealed class BuyAndHoldTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 3, 31);
    private static readonly BuyAndHoldOptions Options = new(3, 7, 0.25);

    private static InstrumentSeries Series(string symbol, double first, double last, DateOnly? start = null, DateOnly? end = null) =>
        new(symbol, [new DailyClose(start ?? From, first), new DailyClose(end ?? To, last)]);

    private static InstrumentSeries Abc(string symbol, double first, double last) => Series(symbol, first, last);

    private static List<InstrumentSeries> Three() => [Abc("A", 100, 110), Abc("B", 50, 45), Abc("C", 200, 250)];

    [Fact]
    public void The_hold_is_the_mean_of_the_returns_of_the_included_instruments()
    {
        var result = BuyAndHold.Compute(Three(), From, To, Options);

        result.Return.State.ShouldBe(MetricState.Value);
        result.Return.Number!.Value.ShouldBe(25.0 / 3, 1e-9);
        result.Included.ShouldBe(3);
        result.Excluded.ShouldBeEmpty();
    }

    [Fact]
    public void Adding_an_included_instrument_changes_only_the_mean()
    {
        var series = Three();
        series.Add(Abc("D", 10, 12));

        BuyAndHold.Compute(series, From, To, Options).Return.Number!.Value.ShouldBe(11.25, 1e-9);
    }

    [Fact]
    public void The_order_of_the_instruments_does_not_matter()
    {
        var forward = BuyAndHold.Compute(Three(), From, To, Options).Return.Number;
        var backward = BuyAndHold.Compute([.. Three().AsEnumerable().Reverse()], From, To, Options).Return.Number;

        backward.ShouldBe(forward);
    }

    [Fact]
    public void Each_exclusion_reason_is_named_alone_and_the_rest_is_used()
    {
        var series = Three();
        series.Add(new InstrumentSeries("NOBARS", []));
        series.Add(Series("LATE", 10, 11, start: From.AddDays(8)));
        series.Add(Series("EARLY", 10, 11, end: To.AddDays(-8)));
        series.Add(new InstrumentSeries("SPLIT", [new DailyClose(From, 100), new DailyClose(From.AddDays(10), 50), new DailyClose(To, 55)]));

        var result = BuyAndHold.Compute(series, From, To, Options);

        result.Included.ShouldBe(3);
        result.Return.Number!.Value.ShouldBe(25.0 / 3, 1e-9);
        result.Excluded.ShouldBe(
        [
            new BuyAndHoldExclusion("EARLY", BuyAndHold.EndsEarly),
            new BuyAndHoldExclusion("LATE", BuyAndHold.StartsLate),
            new BuyAndHoldExclusion("NOBARS", BuyAndHold.NoBars),
            new BuyAndHoldExclusion("SPLIT", BuyAndHold.Discontinuity),
        ]);
    }

    [Fact]
    public void A_gap_equal_to_the_allowance_is_kept()
    {
        var series = Three();
        series.Add(Series("EDGE", 10, 11, start: From.AddDays(7), end: To.AddDays(-7)));

        BuyAndHold.Compute(series, From, To, Options).Included.ShouldBe(4);
    }

    [Fact]
    public void A_zero_first_close_is_a_bad_price_never_a_number()
    {
        var series = Three();
        series.Add(Series("ZERO", 0, 10));

        var result = BuyAndHold.Compute(series, From, To, Options);

        result.Excluded.ShouldHaveSingleItem().Reason.ShouldBe(BuyAndHold.BadPrice);
        result.Return.Number!.Value.ShouldBe(25.0 / 3, 1e-9);
    }

    [Fact]
    public void Fewer_than_the_minimum_gives_insufficient_without_a_number()
    {
        var result = BuyAndHold.Compute([Abc("A", 100, 110), Abc("B", 50, 45)], From, To, Options);

        result.Return.State.ShouldBe(MetricState.Insufficient);
        result.Return.Number.ShouldBeNull();
        result.Included.ShouldBe(2);
    }

    [Fact]
    public void None_included_is_empty()
    {
        var result = BuyAndHold.Compute([new InstrumentSeries("X", [])], From, To, Options);

        result.Return.State.ShouldBe(MetricState.Empty);
        result.Return.Number.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_window_that_does_not_start_before_it_ends_is_not_evaluable(int days)
    {
        var result = BuyAndHold.Compute(Three(), To, To.AddDays(days), Options);

        result.Return.State.ShouldBe(MetricState.NotEvaluable);
        result.Return.Number.ShouldBeNull();
    }

    [Fact]
    public void The_start_is_the_first_bar_on_or_after_the_window_start_and_the_end_the_last_on_or_before_the_window_end()
    {
        var inside = new InstrumentSeries("IN", [new DailyClose(From.AddDays(-5), 1), new DailyClose(From.AddDays(2), 100), new DailyClose(To.AddDays(-2), 120), new DailyClose(To.AddDays(5), 1)]);

        var result = BuyAndHold.Compute([inside], From, To, Options with { MinMembers = 1 });

        result.Return.Number!.Value.ShouldBe(20, 1e-9);
    }

    [Fact]
    public void A_figure_is_a_number_only_when_its_state_is_value()
    {
        foreach (var count in new[] { 0, 1, 2, 3, 5 })
        {
            var series = Enumerable.Range(0, count).Select(i => Abc("S" + i, 100, 100 + i)).ToList();

            var metric = BuyAndHold.Compute(series, From, To, Options).Return;

            (metric.Number is not null).ShouldBe(metric.State == MetricState.Value);
            metric.Number.ShouldNotBe(double.NaN);
        }
    }
}
