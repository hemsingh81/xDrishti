using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Quality;

namespace XDrishti.Domain.Tests.Options.Quality;

public sealed class CoverageChecksTests
{
    private static readonly OptionQualityThresholds T = new();

    private static readonly Dictionary<string, int> NoOutcomes = [];

    [Theory]
    [InlineData(96, 100, QualityStatus.Pass)]
    [InlineData(95, 100, QualityStatus.Pass)]
    [InlineData(94, 100, QualityStatus.Warn)]
    [InlineData(80, 100, QualityStatus.Warn)]
    [InlineData(79, 100, QualityStatus.Fail)]
    [InlineData(0, 100, QualityStatus.Fail)]
    public void Chain_coverage_is_the_share_of_ok_requests(int ok, int planned, QualityStatus expected)
    {
        var check = ChainCoverageCheck.Evaluate(true, planned, ok, new Dictionary<string, int> { ["ok"] = ok, ["rate_limited"] = planned - ok }, T);

        check.Status.ShouldBe(expected);
        check.Value.ShouldBe((double)ok / planned);
        check.Threshold.ShouldBe(0.95);
    }

    [Fact]
    public void Chain_gaps_are_listed_by_outcome()
    {
        var check = ChainCoverageCheck.Evaluate(true, 10, 7, new Dictionary<string, int> { ["ok"] = 7, ["timeout"] = 2, ["rate_limited"] = 1 }, T);

        check.Detail.ShouldBe("{\"ok\":7,\"rate_limited\":1,\"timeout\":2}");
        check.Reason.ShouldBe("gap");
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(true, 0)]
    public void A_day_that_was_not_recorded_is_unavailable_never_a_fail(bool recorded, int planned)
    {
        var check = ChainCoverageCheck.Evaluate(recorded, planned, 0, NoOutcomes, T);

        check.Status.ShouldBe(QualityStatus.Unavailable);
        check.Reason.ShouldBe("not_recorded");
    }

    [Theory]
    [InlineData(5, 75, 75, QualityStatus.Pass)]
    [InlineData(5, 72, 75, QualityStatus.Pass)]
    [InlineData(5, 62, 75, QualityStatus.Warn)]
    [InlineData(5, 50, 75, QualityStatus.Fail)]
    [InlineData(15, 25, 25, QualityStatus.Pass)]
    [InlineData(15, 20, 25, QualityStatus.Warn)]
    [InlineData(15, 10, 25, QualityStatus.Fail)]
    public void Rolling_coverage_is_the_stored_bars_over_the_bars_of_a_session(int interval, int stored, int expected, QualityStatus status)
    {
        RollingCoverageCheck.ExpectedBars(interval).ShouldBe(expected);
        RollingCoverageCheck.Evaluate(stored, interval, "ok", false, T).Status.ShouldBe(status);
    }

    [Fact]
    public void A_key_with_no_bar_that_the_log_calls_empty_inside_an_out_of_era_period_is_unavailable()
    {
        var check = RollingCoverageCheck.Evaluate(0, 5, "empty", true, T);

        check.Status.ShouldBe(QualityStatus.Unavailable);
        check.Reason.ShouldBe("out_of_era");
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("no_data")]
    public void The_same_empty_window_outside_such_a_period_is_a_fail(string outcome)
    {
        RollingCoverageCheck.Evaluate(0, 5, outcome, false, T).Status.ShouldBe(QualityStatus.Fail);
    }

    [Fact]
    public void An_out_of_era_series_that_has_bars_is_judged_by_its_bars()
    {
        RollingCoverageCheck.Evaluate(75, 5, "ok", true, T).Status.ShouldBe(QualityStatus.Pass);
    }

    private static readonly MarketEpoch[] Epochs = [new("weekly_index_one_per_exchange", new DateOnly(2024, 11, 20), "one weekly", null, false)];

    [Theory]
    [InlineData("NSE:BANKNIFTY", 'W', "2024-11-20", true)]
    [InlineData("NSE:BANKNIFTY", 'W', "2024-11-19", false)]
    [InlineData("NSE:BANKNIFTY", 'M', "2025-01-01", false)]
    [InlineData("NSE:NIFTY", 'W', "2025-01-01", false)]
    [InlineData("NSE:FINNIFTY", 'W', "2025-01-01", true)]
    public void Only_the_weekly_series_of_an_index_other_than_nifty_is_out_of_era_from_the_epoch(string symbol, char flag, string date, bool expected)
    {
        OutOfEra.IsOutOfEra(symbol, flag, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), Epochs).ShouldBe(expected);
    }

    [Fact]
    public void Without_the_epoch_row_nothing_is_out_of_era()
    {
        OutOfEra.IsOutOfEra("NSE:BANKNIFTY", 'W', new DateOnly(2025, 1, 1), []).ShouldBeFalse();
    }
}
