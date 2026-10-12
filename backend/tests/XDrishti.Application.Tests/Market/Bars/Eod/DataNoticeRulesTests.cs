using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Market.Quality;
using XDrishti.Application.Tests.Market.Actions;
using XDrishti.Application.Tests.Market.Calendar;
using XDrishti.Application.Tests.Market.Quality;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Bars.Eod;

/// <summary>The notices of the bell about the data: a failed scheduled run, and sessions left with missing bars.</summary>
public sealed class DataNoticeRulesTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static readonly DateTimeOffset Start = new(2026, 10, 12, 15, 50, 0, Ist);

    private static readonly GapSummary NoGaps = new(0, 0, 0, null);

    private static DataJob Daily(bool fail, string? error = "Dhan returned errors for all 3 instruments: NSE:ABB: timeout")
    {
        var job = DataJob.Start([DataSteps.Dhan, DataSteps.Aggregate], false, "system:data-eod", Start, DataJobTrigger.Daily);
        if (fail)
        {
            job.Fail(Start.AddMinutes(4), error!);
        }
        else
        {
            job.Finish(Start.AddMinutes(4));
        }

        return job;
    }

    [Fact]
    public void A_failed_scheduled_run_raises_an_error_notice_with_the_reason_and_an_id_of_that_run()
    {
        var job = Daily(fail: true);

        var notice = DataNoticeRules.Build(job, NoGaps).ShouldHaveSingleItem();

        (notice.Kind, notice.Severity, notice.Id, notice.At).ShouldBe(("data_run_failed", "error", $"data:run_failed:{job.Id}", Start.AddMinutes(4)));
        notice.Text.ShouldBe("The automatic data update of 12 Oct failed: Dhan returned errors for all 3 instruments: NSE:ABB: timeout");
    }

    [Fact]
    public void A_later_run_is_a_new_notice_so_it_is_unread_again()
    {
        var first = DataNoticeRules.Build(Daily(fail: true), NoGaps).Single();
        var second = DataNoticeRules.Build(Daily(fail: true), NoGaps).Single();

        second.Id.ShouldNotBe(first.Id);
    }

    [Fact]
    public void A_successful_skipped_running_or_missing_scheduled_run_raises_nothing()
    {
        DataNoticeRules.Build(Daily(fail: false), NoGaps).ShouldBeEmpty();
        DataNoticeRules.Build(DataJob.Skipped(["dhan"], DataJobTrigger.Daily, "system:data-eod", "not connected", Start), NoGaps).ShouldBeEmpty();
        DataNoticeRules.Build(DataJob.Start(["dhan"], false, "system:data-eod", Start, DataJobTrigger.Daily), NoGaps).ShouldBeEmpty();
        DataNoticeRules.Build(null, NoGaps).ShouldBeEmpty();
    }

    [Fact]
    public void A_long_reason_is_cut_to_one_short_line()
    {
        var notice = DataNoticeRules.Build(Daily(fail: true, error: "first line\nsecond line " + new string('x', 400)), NoGaps).Single();

        notice.Text.ShouldNotContain('\n');
        notice.Text.Length.ShouldBeLessThan(260);
        notice.Text.ShouldEndWith("…");
    }

    [Fact]
    public void Open_gaps_raise_a_warning_with_the_count_and_when_they_were_found()
    {
        var found = Start.AddMinutes(2);

        var notice = DataNoticeRules.Build(null, new GapSummary(3, 5, 40, found)).ShouldHaveSingleItem();

        (notice.Kind, notice.Severity, notice.Text, notice.At).ShouldBe(("data_gaps_open", "warn", "3 trading sessions have missing price bars", found));
        notice.Id.ShouldBe($"data:gaps_open:{found.ToUnixTimeSeconds()}:3");
    }

    [Fact]
    public void One_open_gap_is_worded_in_the_singular_and_a_changed_count_is_a_new_notice()
    {
        var found = Start;
        var one = DataNoticeRules.Build(null, new GapSummary(1, 0, 0, found)).Single();
        var two = DataNoticeRules.Build(null, new GapSummary(2, 0, 0, found)).Single();

        one.Text.ShouldBe("1 trading session has missing price bars");
        two.Id.ShouldNotBe(one.Id);
    }

    [Fact]
    public void Unfillable_and_filled_gaps_alone_raise_nothing()
    {
        DataNoticeRules.Build(null, new GapSummary(0, 12, 30, Start)).ShouldBeEmpty();
    }

    [Fact]
    public void A_failed_run_and_open_gaps_give_both_notices_the_failure_first()
    {
        var notices = DataNoticeRules.Build(Daily(fail: true), new GapSummary(2, 0, 0, Start));

        notices.Select(n => n.Kind).ShouldBe(["data_run_failed", "data_gaps_open"]);
    }

    [Fact]
    public async Task The_handler_reads_the_last_scheduled_run_and_the_gap_counts()
    {
        var jobs = new FakeDataJobStore();
        var failed = Daily(fail: true);
        jobs.Jobs[failed.Id] = failed;
        var manual = DataJob.Start(["dhan"], false, "owner", Start.AddHours(1));
        manual.Fail(Start.AddHours(1), "manual failure");
        jobs.Jobs[manual.Id] = manual; // a manual failure is the owner's own business
        var gaps = new FakeBarGaps();
        gaps.Rows[(Guid.NewGuid(), new DateOnly(2026, 10, 9))] = BarGap.Detect(Guid.NewGuid(), new DateOnly(2026, 10, 9), 375, 10, Start);

        var notices = await new GetDataNoticesHandler(jobs, gaps, new StubQualityQueries(), new FixedCalendar(), new FakeDataSettings(), new InMemoryActionData(), new FakeTimeProvider(Start)).HandleAsync(new GetDataNoticesQuery(), TestContext.Current.CancellationToken);

        notices.Items.Select(n => n.Kind).ShouldBe(["data_run_failed", "data_gaps_open"]);
    }

    private static QualityRunView QualityRun(string status = "succeeded") =>
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "daily", status, Start, Start.AddSeconds(9), 5, 100, 0, 0, 0, null, null, null, null);

    [Fact]
    public void Without_a_quality_run_nothing_is_said_about_quality_even_though_the_gate_is_blocked()
    {
        var gate = QualityGateRules.Evaluate(new QualityCounts(0, 0, 0, 0, 0, 0, null), null, 5, new TradingCalendar(), Start);

        DataNoticeRules.Build(null, NoGaps, null, gate).ShouldBeEmpty();
    }

    [Fact]
    public void Failing_instruments_within_the_limit_raise_a_warning_with_the_gate_reason()
    {
        var run = QualityRun();
        var gate = QualityGateRules.Evaluate(new QualityCounts(100, 100, 97, 0, 3, 0, 95), run, 5, new TradingCalendar(), Start);

        var notice = DataNoticeRules.Build(null, NoGaps, run, gate).ShouldHaveSingleItem();

        (notice.Kind, notice.Severity, notice.At).ShouldBe(("data_quality_failed", "warn", run.FinishedAt));
        notice.Text.ShouldBe("3 of 100 instruments fail the checks (3 %); they are skipped, the limit is 5 %.");
        notice.Id.ShouldBe("data:quality_failed:11111111-1111-1111-1111-111111111111:3:ok");
    }

    [Fact]
    public void A_blocked_gate_is_an_error_that_says_the_nightly_plan_is_blocked()
    {
        var run = QualityRun();
        var gate = QualityGateRules.Evaluate(new QualityCounts(100, 100, 90, 0, 10, 0, 90), run, 5, new TradingCalendar(), Start);

        var notice = DataNoticeRules.Build(null, NoGaps, run, gate).ShouldHaveSingleItem();

        (notice.Severity, notice.Text).ShouldBe(("error", "Data quality blocks the nightly plan: 10 of 100 instruments fail the checks (10 %), more than the 5 % allowed."));
        notice.Id.ShouldEndWith(":10:blocked");
    }

    [Fact]
    public void A_failed_quality_run_is_an_error()
    {
        var run = QualityRun("failed");
        var gate = QualityGateRules.Evaluate(new QualityCounts(100, 100, 100, 0, 0, 0, 95), run, 5, new TradingCalendar(), Start);

        DataNoticeRules.Build(null, NoGaps, run, gate).ShouldHaveSingleItem().Severity.ShouldBe("error");
    }

    [Fact]
    public void A_clean_run_raises_nothing_and_a_changed_number_of_failures_is_a_new_notice()
    {
        var run = QualityRun();
        var clean = QualityGateRules.Evaluate(new QualityCounts(100, 100, 100, 0, 0, 0, 99), run, 5, new TradingCalendar(), Start);
        var three = DataNoticeRules.Build(null, NoGaps, run, QualityGateRules.Evaluate(new QualityCounts(100, 100, 97, 0, 3, 0, 95), run, 5, new TradingCalendar(), Start)).Single();
        var four = DataNoticeRules.Build(null, NoGaps, run, QualityGateRules.Evaluate(new QualityCounts(100, 100, 96, 0, 4, 0, 95), run, 5, new TradingCalendar(), Start)).Single();

        DataNoticeRules.Build(null, NoGaps, run, clean).ShouldBeEmpty();
        four.Id.ShouldNotBe(three.Id);
    }

    [Fact]
    public void The_quality_notice_comes_after_the_failed_run_and_the_gaps()
    {
        var run = QualityRun();
        var gate = QualityGateRules.Evaluate(new QualityCounts(100, 100, 97, 0, 3, 0, 95), run, 5, new TradingCalendar(), Start);

        DataNoticeRules.Build(Daily(fail: true), new GapSummary(2, 0, 0, Start), run, gate).Select(n => n.Kind).ShouldBe(["data_run_failed", "data_gaps_open", "data_quality_failed"]);
    }

    [Fact]
    public async Task The_handler_adds_the_quality_notice_from_the_last_run_and_the_counts()
    {
        var queries = new StubQualityQueries { LastFullRun = QualityRun(), Counts = new QualityCounts(100, 100, 90, 0, 10, 0, 90) };

        var notices = await new GetDataNoticesHandler(new FakeDataJobStore(), new FakeBarGaps(), queries, new FixedCalendar(), new FakeDataSettings(), new InMemoryActionData(), new FakeTimeProvider(Start)).HandleAsync(new GetDataNoticesQuery(), TestContext.Current.CancellationToken);

        notices.Items.ShouldHaveSingleItem().Kind.ShouldBe("data_quality_failed");
    }
}
