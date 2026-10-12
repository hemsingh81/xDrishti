using XDrishti.Application.Market.Quality;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Quality;

namespace XDrishti.Application.Tests.Market.Quality;

public sealed class QualityRowsTests
{
    private static readonly DateOnly Day = new(2026, 10, 9);

    private static QualityChecks Clean() => Checks();

    private static QualityChecks Checks(
        CompletenessCheck? completeness = null, SanityCheck? sanity = null, SpikeCheck? spikes = null, AggregatesCheck? aggregates = null, FreshnessCheck? freshness = null) =>
        new(
            completeness ?? new CompletenessCheck(CheckStatus.Pass, 1875, 1875, 0, 0, 0, []),
            sanity ?? new SanityCheck(CheckStatus.Pass, 1875, 0, []),
            spikes ?? new SpikeCheck(CheckStatus.Pass, 0, 0, 0.01, 1, []),
            aggregates ?? new AggregatesCheck(CheckStatus.Pass, 600, 0, Timeframes.Aggregated.ToDictionary(t => t.ToCode(), _ => new TimeframeCheck(CheckStatus.Pass, 10, 0))),
            freshness ?? new FreshnessCheck(CheckStatus.Pass, new DateTimeOffset(2026, 10, 9, 15, 29, 0, TimeSpan.FromMinutes(330)), Day, 0),
            OfficialDailyCheck.NotAvailable,
            null);

    private static QualityInstrumentRow Row(QualityChecks? checks, string? status = "pass") =>
        QualityRows.From(Guid.NewGuid(), "NSE:ABB", "ABB Ltd", "share", "dhan", status, 100, 1875, 0, checks, new DateTimeOffset(2026, 10, 12, 4, 50, 0, TimeSpan.Zero), Guid.NewGuid());

    [Fact]
    public void An_instrument_never_checked_is_not_checked_with_nothing_else()
    {
        var row = Row(null, null);

        (row.Status, row.Score, row.Coverage, row.Checks, row.Aggregates, row.CheckedAt).ShouldBe(("notChecked", null, null, null, null, null));
        row.Issues.ShouldBeEmpty();
        row.Source.ShouldBe("dhan");
    }

    [Fact]
    public void A_clean_result_has_full_coverage_every_timeframe_and_no_issues()
    {
        var row = Row(Clean());

        (row.Status, row.Coverage, row.BarsChecked, row.MissingMinutes).ShouldBe(("pass", 100.0, 1875, 0));
        row.Aggregates!.Keys.ShouldBe(["5m", "10m", "15m", "30m", "1h", "1D", "1W", "1M"]);
        row.Aggregates.Values.ShouldAllBe(s => s == "pass");
        row.Checks!.OfficialDaily.ShouldBe("notAvailable");
        row.Issues.ShouldBeEmpty();
        row.CheckedAt!.Value.Offset.ShouldBe(TimeSpan.FromMinutes(330));
    }

    [Fact]
    public void Coverage_is_the_share_of_expected_minutes_that_is_stored()
    {
        var row = Row(Checks(completeness: new CompletenessCheck(CheckStatus.Warning, 1875, 1856, 19, 1.01, 0, [])), "warning");

        row.Coverage.ShouldBe(98.99);
    }

    [Fact]
    public void Each_trouble_is_said_in_words()
    {
        var checks = Checks(
            completeness: new CompletenessCheck(CheckStatus.Warning, 1875, 1800, 75, 4, 20, []),
            sanity: new SanityCheck(CheckStatus.Fail, 1800, 2, []),
            spikes: new SpikeCheck(CheckStatus.Warning, 1, 0, 0.01, 1, []),
            aggregates: new AggregatesCheck(CheckStatus.Fail, 600, 3, new Dictionary<string, TimeframeCheck>()),
            freshness: new FreshnessCheck(CheckStatus.Fail, new DateTimeOffset(2026, 10, 7, 15, 29, 0, TimeSpan.FromMinutes(330)), Day, 2));

        QualityRows.Issues(checks).ShouldBe(
        [
            "75 missing minutes (4 %), 20 of them could not be fetched",
            "2 bad bars",
            "1 price spikes",
            "3 aggregate bars differ from the 1-minute bars",
            "2 trading days behind",
        ]);
    }

    [Fact]
    public void An_instrument_without_any_bar_is_said_to_have_none()
    {
        QualityRows.Issues(Checks(freshness: new FreshnessCheck(CheckStatus.Fail, null, Day, 0))).ShouldBe(["no bars"]);
    }

    [Fact]
    public void The_timeframes_come_in_timeframe_order_whatever_order_the_database_returned_them_in()
    {
        var shuffled = Timeframes.Aggregated.Select(t => t.ToCode()).OrderBy(code => code, StringComparer.Ordinal).ToDictionary(c => c, _ => new TimeframeCheck(CheckStatus.Pass, 1, 0));
        var checks = Checks(aggregates: new AggregatesCheck(CheckStatus.Pass, 8, 0, shuffled));

        Row(checks).Aggregates!.Keys.ShouldBe(["5m", "10m", "15m", "30m", "1h", "1D", "1W", "1M"]);
        QualityRows.InOrder(checks.Aggregates).ByTimeframe.Keys.ShouldBe(["5m", "10m", "15m", "30m", "1h", "1D", "1W", "1M"]);
    }
}
