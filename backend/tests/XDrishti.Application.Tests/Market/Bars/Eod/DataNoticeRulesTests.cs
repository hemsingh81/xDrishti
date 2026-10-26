using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Market.Quality;
using XDrishti.Application.Tests.Market.Actions;
using XDrishti.Application.Tests.Market.Calendar;
using XDrishti.Application.Tests.Market.Context;
using XDrishti.Application.Tests.Market.Quality;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Tests.Market.Bars.Eod;

/// <summary>The notices of the bell about the data: sessions left with missing bars, failing data quality, corporate actions to review. (A failed scheduled run is the notice of the end-of-day service, not a data notice.)</summary>
public sealed class DataNoticeRulesTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static readonly DateTimeOffset Start = new(2026, 10, 12, 15, 50, 0, Ist);

    private static readonly GapSummary NoGaps = new(0, 0, 0, null);

    [Fact]
    public void A_failed_scheduled_run_is_no_data_notice_any_more_it_is_the_notice_of_the_service()
    {
        DataNoticeRules.Build(NoGaps).ShouldBeEmpty();
    }

    [Fact]
    public void Open_gaps_raise_a_warning_with_the_count_and_when_they_were_found()
    {
        var found = Start.AddMinutes(2);

        var notice = DataNoticeRules.Build(new GapSummary(3, 5, 40, found)).ShouldHaveSingleItem();

        (notice.Kind, notice.Severity, notice.Text, notice.At).ShouldBe(("data_gaps_open", "warn", "3 trading sessions have missing price bars", found));
        notice.Id.ShouldBe($"data:gaps_open:{found.ToUnixTimeSeconds()}:3");
    }

    [Fact]
    public void One_open_gap_is_worded_in_the_singular_and_a_changed_count_is_a_new_notice()
    {
        var found = Start;
        var one = DataNoticeRules.Build(new GapSummary(1, 0, 0, found)).Single();
        var two = DataNoticeRules.Build(new GapSummary(2, 0, 0, found)).Single();

        one.Text.ShouldBe("1 trading session has missing price bars");
        two.Id.ShouldNotBe(one.Id);
    }

    [Fact]
    public void Unfillable_and_filled_gaps_alone_raise_nothing()
    {
        DataNoticeRules.Build(new GapSummary(0, 12, 30, Start)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_handler_reads_the_gap_counts()
    {
        var gaps = new FakeBarGaps();
        gaps.Rows[(Guid.NewGuid(), new DateOnly(2026, 10, 9))] = BarGap.Detect(Guid.NewGuid(), new DateOnly(2026, 10, 9), 375, 10, Start);

        var notices = await new GetDataNoticesHandler(gaps, new StubQualityQueries(), new FixedCalendar(), new FakeDataSettings(), new InMemoryActionData(), new FakeContextDayStore(), new FakeTimeProvider(Start)).HandleAsync(new GetDataNoticesQuery(), TestContext.Current.CancellationToken);

        notices.Items.Select(n => n.Kind).ShouldBe(["data_gaps_open"]);
    }

    private static QualityRunView QualityRun(string status = "succeeded") =>
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "daily", status, Start, Start.AddSeconds(9), 5, 100, 0, 0, 0, null, null, null, null);

    [Fact]
    public void Without_a_quality_run_nothing_is_said_about_quality_even_though_the_gate_is_blocked()
    {
        var gate = QualityGateRules.Evaluate(new QualityCounts(0, 0, 0, 0, 0, 0, null), null, 5, new TradingCalendar(), Start);

        DataNoticeRules.Build(NoGaps, null, gate).ShouldBeEmpty();
    }

    [Fact]
    public void Failing_instruments_within_the_limit_raise_a_warning_with_the_gate_reason()
    {
        var run = QualityRun();
        var gate = QualityGateRules.Evaluate(new QualityCounts(100, 100, 97, 0, 3, 0, 95), run, 5, new TradingCalendar(), Start);

        var notice = DataNoticeRules.Build(NoGaps, run, gate).ShouldHaveSingleItem();

        (notice.Kind, notice.Severity, notice.At).ShouldBe(("data_quality_failed", "warn", run.FinishedAt));
        notice.Text.ShouldBe("3 of 100 instruments fail the checks (3 %); they are skipped, the limit is 5 %.");
        notice.Id.ShouldBe("data:quality_failed:11111111-1111-1111-1111-111111111111:3:ok");
    }

    [Fact]
    public void A_blocked_gate_is_an_error_that_says_the_nightly_plan_is_blocked()
    {
        var run = QualityRun();
        var gate = QualityGateRules.Evaluate(new QualityCounts(100, 100, 90, 0, 10, 0, 90), run, 5, new TradingCalendar(), Start);

        var notice = DataNoticeRules.Build(NoGaps, run, gate).ShouldHaveSingleItem();

        (notice.Severity, notice.Text).ShouldBe(("error", "Data quality blocks the nightly plan: 10 of 100 instruments fail the checks (10 %), more than the 5 % allowed."));
        notice.Id.ShouldEndWith(":10:blocked");
    }

    [Fact]
    public void A_failed_quality_run_is_an_error()
    {
        var run = QualityRun("failed");
        var gate = QualityGateRules.Evaluate(new QualityCounts(100, 100, 100, 0, 0, 0, 95), run, 5, new TradingCalendar(), Start);

        DataNoticeRules.Build(NoGaps, run, gate).ShouldHaveSingleItem().Severity.ShouldBe("error");
    }

    [Fact]
    public void A_clean_run_raises_nothing_and_a_changed_number_of_failures_is_a_new_notice()
    {
        var run = QualityRun();
        var clean = QualityGateRules.Evaluate(new QualityCounts(100, 100, 100, 0, 0, 0, 99), run, 5, new TradingCalendar(), Start);
        var three = DataNoticeRules.Build(NoGaps, run, QualityGateRules.Evaluate(new QualityCounts(100, 100, 97, 0, 3, 0, 95), run, 5, new TradingCalendar(), Start)).Single();
        var four = DataNoticeRules.Build(NoGaps, run, QualityGateRules.Evaluate(new QualityCounts(100, 100, 96, 0, 4, 0, 95), run, 5, new TradingCalendar(), Start)).Single();

        DataNoticeRules.Build(NoGaps, run, clean).ShouldBeEmpty();
        four.Id.ShouldNotBe(three.Id);
    }

    [Fact]
    public void The_quality_notice_comes_after_the_gaps()
    {
        var run = QualityRun();
        var gate = QualityGateRules.Evaluate(new QualityCounts(100, 100, 97, 0, 3, 0, 95), run, 5, new TradingCalendar(), Start);

        DataNoticeRules.Build(new GapSummary(2, 0, 0, Start), run, gate).Select(n => n.Kind).ShouldBe(["data_gaps_open", "data_quality_failed"]);
    }

    [Fact]
    public async Task The_handler_adds_the_quality_notice_from_the_last_run_and_the_counts()
    {
        var queries = new StubQualityQueries { LastFullRun = QualityRun(), Counts = new QualityCounts(100, 100, 90, 0, 10, 0, 90) };

        var notices = await new GetDataNoticesHandler(new FakeBarGaps(), queries, new FixedCalendar(), new FakeDataSettings(), new InMemoryActionData(), new FakeContextDayStore(), new FakeTimeProvider(Start)).HandleAsync(new GetDataNoticesQuery(), TestContext.Current.CancellationToken);

        notices.Items.ShouldHaveSingleItem().Kind.ShouldBe("data_quality_failed");
    }

    [Fact]
    public void A_context_more_than_one_session_behind_raises_a_stale_warning_with_the_days_behind()
    {
        var health = new ContextHealth(new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 6), 4, 0);

        var notice = DataNoticeRules.Build(NoGaps, context: health).ShouldHaveSingleItem();

        (notice.Kind, notice.Severity).ShouldBe(("data_context_stale", "warn"));
        notice.Text.ShouldContain("4 sessions behind");
        notice.Id.ShouldBe("data:context_stale:2026-10-06:4");
    }

    [Fact]
    public void A_current_context_and_a_missing_one_raise_nothing()
    {
        DataNoticeRules.Build(NoGaps, context: new ContextHealth(new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 9), 1, 0)).ShouldBeEmpty();
        DataNoticeRules.Build(NoGaps, context: null).ShouldBeEmpty();
    }

    [Fact]
    public void Sessions_missing_inside_the_context_raise_a_gap_warning_whose_id_changes_with_the_count()
    {
        var one = DataNoticeRules.Build(NoGaps, context: new ContextHealth(new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 9), 0, 1)).ShouldHaveSingleItem();
        var two = DataNoticeRules.Build(NoGaps, context: new ContextHealth(new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 9), 0, 2)).ShouldHaveSingleItem();

        one.Kind.ShouldBe("data_context_gap");
        one.Text.ShouldBe("1 session of the market context has no record");
        two.Text.ShouldBe("2 sessions of the market context have no record");
        two.Id.ShouldNotBe(one.Id);
    }

    [Fact]
    public async Task The_handler_reads_the_stored_context_against_the_calendar()
    {
        var store = new FakeContextDayStore();
        store.Rows.AddRange([new ContextDayRecord(new DateOnly(2026, 10, 1), 105, 95, 100, 15, 375, 375), new ContextDayRecord(new DateOnly(2026, 10, 5), 105, 95, 100, 15, 375, 375)]);

        var notices = await new GetDataNoticesHandler(new FakeBarGaps(), new StubQualityQueries(), new FixedCalendar(), new FakeDataSettings(), new InMemoryActionData(), store, new FakeTimeProvider(Start)).HandleAsync(new GetDataNoticesQuery(), TestContext.Current.CancellationToken);

        notices.Items.Select(n => n.Kind).ShouldBe(["data_context_stale", "data_context_gap"]);
    }
}
