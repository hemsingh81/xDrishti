using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Application.Market.Quality;
using XDrishti.Application.Tests.Market.Bars;
using XDrishti.Application.Tests.Market.Calendar;
using XDrishti.Application.Tests.Market.Quality;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Actions;

/// <summary>Where corporate actions meet the rest: the aggregate phase of a data job, the bell notice and the pending counts of the Data and Data quality screens.</summary>
public sealed class CorporateActionHooksTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryActionData _data = new();
    private readonly FakeDailySeries _series = new();
    private readonly FakeAggregates _aggregates = new();
    private readonly FakeDataJobStore _jobs = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<AggregatePhase> RunAsync(IDailySeries? series = null)
    {
        var job = DataJob.Start([DataSteps.Csv, DataSteps.Aggregate], false, "owner", Now);
        _jobs.Jobs[job.Id] = job;
        var runner = new AggregatePhaseRunner(_aggregates, new FixedCalendar(), new FakeDataSettings(), new CorporateActionDetection(series ?? _series, _data, _data, _clock), _clock, NullLogger<AggregatePhaseRunner>.Instance);
        return await runner.RunAsync(job, new JobReporter(job, _jobs, _clock), Ct);
    }

    private DirtyInstrument Dirty(Guid id, DateOnly from)
    {
        var dirty = new DirtyInstrument(id, "NSE:ABB", new DateTimeOffset(from.Year, from.Month, from.Day, 9, 15, 0, TimeSpan.FromMinutes(330)), Now);
        _aggregates.Dirty.Add(dirty);
        return dirty;
    }

    [Fact]
    public async Task After_an_instrument_is_built_the_days_it_changed_are_checked_and_a_split_is_suggested_and_said_in_the_phase_message()
    {
        var bars = DaySeries.WithGap(80, 70, 0.2, 5.0);
        var id = _series.Add("NSE:ABB", bars);
        Dirty(id, bars[68].Date);

        var phase = await RunAsync();

        phase.Status.ShouldBe(PhaseStatus.Succeeded);
        phase.Message.ShouldBe("1 corporate action suggested for review.");
        var suggestion = _data.Actions.ShouldHaveSingleItem();
        (suggestion.ExDate, suggestion.Status, suggestion.Ratio).ShouldBe((bars[70].Date, CorporateActionStatus.Suggested, "1:5"));
    }

    [Fact]
    public async Task Days_the_job_did_not_change_are_not_looked_at_and_the_message_stays_empty()
    {
        var bars = DaySeries.WithGap(80, 50, 0.2, 5.0);
        var id = _series.Add("NSE:ABB", bars);
        Dirty(id, bars[60].Date);

        var phase = await RunAsync();

        _data.Actions.ShouldBeEmpty();
        phase.Message.ShouldBeNull();
    }

    [Fact]
    public async Task A_detection_that_fails_never_fails_the_phase()
    {
        var id = Guid.NewGuid();
        Dirty(id, new DateOnly(2026, 10, 1));

        var phase = await RunAsync(new ThrowingSeries());

        (phase.Status, phase.Done, phase.Message).ShouldBe((PhaseStatus.Succeeded, 1L, null));
        _aggregates.Built.Count.ShouldBe(1);
    }

    [Fact]
    public void Suggestions_waiting_give_a_warn_notice_with_the_count_and_a_new_id_when_the_number_changes()
    {
        var found = Now.AddHours(-2);

        var one = DataNoticeRules.Build(new GapSummary(0, 0, 0, null), pendingActions: new PendingActions(1, found));
        var three = DataNoticeRules.Build(new GapSummary(0, 0, 0, null), pendingActions: new PendingActions(3, found));

        var notice = one.ShouldHaveSingleItem();
        (notice.Kind, notice.Severity, notice.Text, notice.At).ShouldBe(("data_corporate_actions_pending", "warn", "1 suggested corporate action (split or bonus) to review", found));
        three.ShouldHaveSingleItem().Text.ShouldBe("3 suggested corporate actions (splits or bonus issues) to review");
        three[0].Id.ShouldNotBe(notice.Id);
        three[0].Id.ShouldBe(DataNoticeRules.Build(new GapSummary(0, 0, 0, null), pendingActions: new PendingActions(3, found))[0].Id);
    }

    [Fact]
    public void No_suggestions_no_notice()
    {
        DataNoticeRules.Build(new GapSummary(0, 0, 0, null), pendingActions: new PendingActions(0, null)).ShouldBeEmpty();
        DataNoticeRules.Build(new GapSummary(0, 0, 0, null)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_notice_handler_reads_the_pending_suggestions()
    {
        _data.Actions.Add(CorporateAction.Suggest(Guid.NewGuid(), new DateOnly(2026, 9, 1), CorporateActionKind.Split, 1, 5, null, Now));

        var notices = await new GetDataNoticesHandler(new FakeBarGaps(), new StubQualityQueries(), new FixedCalendar(), new FakeDataSettings(), _data, _clock).HandleAsync(new GetDataNoticesQuery(), Ct);

        notices.Items.ShouldHaveSingleItem().Kind.ShouldBe("data_corporate_actions_pending");
    }

    [Fact]
    public async Task The_quality_overview_carries_the_pending_count()
    {
        _data.Actions.Add(CorporateAction.Suggest(Guid.NewGuid(), new DateOnly(2026, 9, 1), CorporateActionKind.Split, 1, 5, null, Now));
        _data.Actions.Add(CorporateAction.Suggest(Guid.NewGuid(), new DateOnly(2026, 9, 2), CorporateActionKind.Split, 1, 2, null, Now));

        var overview = await new GetQualityOverviewHandler(new StubQualityQueries(), new FixedCalendar(), new FakeDataSettings(), _data, _clock).HandleAsync(new GetQualityOverviewQuery(), Ct);

        overview.PendingCorporateActions.ShouldBe(2);
    }

    private sealed class ThrowingSeries : IDailySeries
    {
        public ValueTask<IReadOnlyList<SeriesInstrument>> ListInstrumentsAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("boom");

        public ValueTask<IReadOnlyList<DailyBar>> ReadAsync(Guid instrumentId, DateOnly from, CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
    }
}
