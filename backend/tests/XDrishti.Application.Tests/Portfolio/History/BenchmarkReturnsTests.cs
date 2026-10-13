using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Returns;
using static XDrishti.Application.Tests.Portfolio.History.HistoryData;

namespace XDrishti.Application.Tests.Portfolio.History;

/// <summary>
/// Benchmark XIRR and alpha (acceptance criterion 3 of P2-07). The expected rates of <see cref="Reference"/> were computed for this test with a separate implementation of the definition: the same cash flows
/// are simulated in the index (a purchase buys units at that day's close, a sale sells units at most up to what is held, what is left is the closing inflow at the last close on or before the valuation
/// date) and the rate is found by plain bisection of Σ amount / (1 + r)^(days from the first flow / 365) = 0 with 60-digit decimal arithmetic in Python 3 (<c>python3 -I</c>), sharing no code with the solver under test.
/// </summary>
public sealed class BenchmarkReturnsTests
{
    private static readonly DateOnly Valuation = new(2026, 10, 11);

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static DailyClose C(int year, int month, int day, decimal close) => new(D(year, month, day), close);

    private static HoldingData Holding(params LotData[] lots) => HoldingFixtures.Holding(100m, "SYM", "open", Alpha, lots);

    public static TheoryData<string, LotData[], DailyClose[], double> Reference => new()
    {
        {
            "single lot, index 21,500 to 25,000",
            [HoldingFixtures.Open(100, 50m, D(2024, 1, 2))],
            [C(2024, 1, 2, 21500m), C(2025, 6, 2, 24000m), C(2026, 10, 9, 25000m)],
            0.055847629110
        },
        {
            "two lots (one on a Saturday) and a closed lot",
            [
                HoldingFixtures.Closed(100, 100m, D(2023, 3, 15), D(2024, 8, 5), 130m),
                HoldingFixtures.Open(50, 200m, D(2024, 1, 13)),
                HoldingFixtures.Open(30, 350m, D(2025, 2, 10)),
            ],
            [C(2023, 3, 15, 17000m), C(2023, 12, 29, 21700m), C(2024, 1, 12, 21500m), C(2024, 8, 5, 24400m), C(2025, 2, 10, 23000m), C(2026, 10, 9, 25000m)],
            0.116232382764
        },
        {
            "a sale worth more index units than were bought",
            [HoldingFixtures.Closed(10, 100m, D(2024, 1, 2), D(2024, 7, 1), 400m), HoldingFixtures.Open(10, 100m, D(2024, 3, 1))],
            [C(2024, 1, 2, 21500m), C(2024, 3, 1, 22000m), C(2024, 7, 1, 23500m), C(2026, 10, 9, 25000m)],
            0.205009912822
        },
        {
            "a falling index",
            [HoldingFixtures.Open(20, 500m, D(2022, 1, 3)), HoldingFixtures.Open(20, 480m, D(2022, 9, 1))],
            [C(2022, 1, 3, 17600m), C(2022, 9, 1, 17500m), C(2023, 3, 1, 17300m), C(2026, 10, 9, 16900m)],
            -0.008458641698
        },
    };

    [Theory]
    [MemberData(nameof(Reference))]
    public void The_rate_matches_the_reference_within_a_millionth(string name, LotData[] lots, DailyClose[] index, double expected)
    {
        var result = BenchmarkReturns.Compute([Holding(lots)], Valuation, index, Weekdays);

        result.State.ShouldBe(BenchmarkState.Ok, name);
        result.Rate.ShouldNotBeNull().ShouldBe(expected, 1e-6, name);
    }

    [Fact]
    public void An_index_without_bars_is_unavailable_not_zero()
    {
        var result = BenchmarkReturns.Compute([Holding(HoldingFixtures.Open(1, 100m, D(2024, 1, 2)))], Valuation, [], Weekdays);

        result.ShouldBe(new BenchmarkXirrResult(BenchmarkState.Unavailable, null, BenchmarkAvailability.NoBars));
    }

    [Fact]
    public void An_index_that_starts_after_the_first_flow_is_unavailable()
    {
        var result = BenchmarkReturns.Compute([Holding(HoldingFixtures.Open(1, 100m, D(2024, 1, 2)))], Valuation, [C(2024, 1, 3, 21500m), C(2026, 10, 9, 25000m)], Weekdays);

        result.ShouldBe(new BenchmarkXirrResult(BenchmarkState.Unavailable, null, BenchmarkAvailability.StartsAfterFirstFlow));
    }

    [Fact]
    public void Without_cash_flows_there_is_nothing_to_compare()
    {
        BenchmarkReturns.Compute([], Valuation, [C(2024, 1, 2, 21500m)], Weekdays).State.ShouldBe(BenchmarkState.Insufficient);
    }

    [Fact]
    public void A_span_under_thirty_days_is_insufficient_like_the_xirr()
    {
        var result = BenchmarkReturns.Compute([Holding(HoldingFixtures.Open(1, 100m, D(2026, 10, 1)))], Valuation, [C(2026, 9, 30, 25000m), C(2026, 10, 9, 25100m)], Weekdays);

        result.State.ShouldBe(BenchmarkState.Insufficient);
        result.Rate.ShouldBeNull();
    }

    [Fact]
    public void A_rate_beyond_a_thousand_percent_is_not_meaningful()
    {
        var result = BenchmarkReturns.Compute([Holding(HoldingFixtures.Open(1, 1000m, D(2026, 9, 11)))], Valuation, [C(2026, 9, 11, 1000m), C(2026, 10, 9, 5000m)], Weekdays);

        result.State.ShouldBe(BenchmarkState.NotMeaningful);
        BenchmarkReturns.Percent(result).ShouldBeNull();
    }

    [Fact]
    public void The_percent_is_a_percent_number_with_four_decimals()
    {
        var result = BenchmarkReturns.Compute([Holding(HoldingFixtures.Open(100, 50m, D(2024, 1, 2)))], Valuation, [C(2024, 1, 2, 21500m), C(2026, 10, 9, 25000m)], Weekdays);

        BenchmarkReturns.Percent(result).ShouldBe(5.5848m);
    }

    [Fact]
    public void The_closing_inflow_is_valued_at_the_last_close_and_the_result_says_which_day_that_is()
    {
        var result = BenchmarkReturns.Compute([Holding(HoldingFixtures.Open(100, 50m, D(2024, 1, 2)))], Valuation, [C(2024, 1, 2, 21500m), C(2026, 10, 9, 25000m)], Weekdays);

        result.AsOf.ShouldBe(D(2026, 10, 9)); // the valuation date is a Sunday: the close of the Friday before
    }

    [Fact]
    public void A_live_day_leaves_the_benchmark_at_the_previous_close()
    {
        // Monday 12 Oct is today, the index has no bar for it yet: the benchmark is as of Friday 9 Oct
        var result = BenchmarkReturns.Compute([Holding(HoldingFixtures.Open(100, 50m, D(2024, 1, 2)))], D(2026, 10, 12), [C(2024, 1, 2, 21500m), C(2026, 10, 9, 25000m)], Weekdays);

        result.AsOf.ShouldBe(D(2026, 10, 9));
        result.Rate.ShouldNotBeNull();
    }

    [Fact]
    public void A_lot_on_a_weekend_buys_at_the_close_of_the_next_trading_day_like_the_chart()
    {
        // bought on Saturday 13 Jan 2024: the units are bought at the close of Monday 15 Jan (21,800), not at Friday's 21,500; the cash flow keeps its Saturday date
        var index = new[] { C(2024, 1, 12, 21500m), C(2024, 1, 15, 21800m), C(2026, 10, 9, 25000m) };
        var weekend = BenchmarkReturns.Compute([Holding(HoldingFixtures.Open(100, 50m, D(2024, 1, 13)))], Valuation, index, Weekdays);
        var monday = BenchmarkReturns.Compute([Holding(HoldingFixtures.Open(100, 50m, D(2024, 1, 15)))], Valuation, index, Weekdays);

        weekend.Rate.ShouldNotBeNull();
        monday.Rate.ShouldNotBeNull();

        // the same units (bought at Monday's 21,800), but the Saturday cash flow is dated two days earlier, so the same growth took longer: a lower rate (Friday's 21,500 would have given a higher one)
        weekend.Rate!.Value.ShouldBeLessThan(monday.Rate!.Value);
    }

    [Fact]
    public void A_lot_bought_and_sold_before_a_trading_day_comes_leaves_no_flows()
    {
        var lot = HoldingFixtures.Closed(10, 100m, D(2024, 1, 13), D(2024, 1, 14), 120m); // Saturday to Sunday

        var result = BenchmarkReturns.Compute([Holding(lot)], Valuation, [C(2024, 1, 12, 21500m), C(2026, 10, 9, 25000m)], Weekdays);

        result.ShouldBe(new BenchmarkXirrResult(BenchmarkState.Insufficient, null, BenchmarkAvailability.NoFlows));
    }

    [Fact]
    public void An_index_whose_bars_start_after_the_first_trading_day_of_a_weekend_lot_is_unavailable()
    {
        // the lot is dated Saturday 13 Jan, takes part on Monday 15 Jan; an index that starts on Tuesday 16 Jan has no close for it
        var result = BenchmarkReturns.Compute([Holding(HoldingFixtures.Open(1, 100m, D(2024, 1, 13)))], Valuation, [C(2024, 1, 16, 21500m), C(2026, 10, 9, 25000m)], Weekdays);

        result.Gap.ShouldBe(BenchmarkAvailability.StartsAfterFirstFlow);
    }

    [Fact]
    public void Alpha_is_the_difference_of_the_rates_in_percentage_points()
    {
        var portfolio = new XirrResult(XirrState.Ok, 0.161);
        var benchmark = new BenchmarkXirrResult(BenchmarkState.Ok, 0.1, BenchmarkAvailability.Available);

        BenchmarkReturns.Alpha(portfolio, benchmark).ShouldBe(6.1m);
        BenchmarkReturns.Alpha(portfolio with { Rate = 0.05 }, benchmark).ShouldBe(-5m);
    }

    [Theory]
    [InlineData(XirrState.Insufficient, BenchmarkState.Ok)]
    [InlineData(XirrState.NotMeaningful, BenchmarkState.Ok)]
    [InlineData(XirrState.NoPrice, BenchmarkState.Ok)]
    [InlineData(XirrState.Ok, BenchmarkState.Unavailable)]
    [InlineData(XirrState.Ok, BenchmarkState.Insufficient)]
    [InlineData(XirrState.Ok, BenchmarkState.NotMeaningful)]
    public void Alpha_needs_both_rates_to_be_meaningful(XirrState own, BenchmarkState other)
    {
        var portfolio = new XirrResult(own, own is XirrState.Ok or XirrState.NotMeaningful ? 0.2 : null);
        var benchmark = new BenchmarkXirrResult(other, other is BenchmarkState.Ok or BenchmarkState.NotMeaningful ? 0.1 : null, BenchmarkAvailability.Available);

        BenchmarkReturns.Alpha(portfolio, benchmark).ShouldBeNull();
    }
}
