#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Learning;
using XDrishti.Application.Paper;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Tests.Selection;

/// <summary>
/// P15-06 criteria 7 and 8: the smart-rule snapshot of a paper account lists <c>smart_rules.event_filter.enabled</c> (so the configuration identity changes when the owner turns the filter on, off, or changes its window), an unreadable history makes the snapshot partial instead of reading OFF,
/// the change is classified as protective, and the news_results events are read from the stored filings and meetings, failing to "not supplied".
/// </summary>
public sealed class EventFilterConfigAndTagTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ConfigSnapshotSource Source(IEventFilterDecisions? decisions) => new(
        Substitute.For<IPlanningSettingsReader>(), Substitute.For<IAutonomySettingsStore>(), Substitute.For<IAccountStore>(), Substitute.For<IRiskProfileStore>(), Substitute.For<ILearningChangelog>(),
        Substitute.For<IStrategyStore>(), Substitute.For<ICellStatsStore>(), Substitute.For<IModelRegistry>(), Substitute.For<ISimulationEngine>(), Substitute.For<IGradeSettings>(), Substitute.For<ICostSheetStore>(), decisions);

    private static IEventFilterDecisions History(params EventFilterRow[] rows)
    {
        var decisions = Substitute.For<IEventFilterDecisions>();
        decisions.RowsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<EventFilterRow>>([.. rows.OrderByDescending(r => r.Id)]));
        return decisions;
    }

    private static EventFilterRow Row(long id, bool enabled, int days = 2, string approval = "manual") => new(id, enabled, days, approval, "owner", "owner:x@example.invalid", "r", "{}", null, DateTimeOffset.UnixEpoch);

    private static async Task<ConfigFingerprint> HashAsync(IEventFilterDecisions? decisions) => ConfigFingerprint.Create((await Source(decisions).ReadAsync(Ct)).Components.Where(c => c.Key.StartsWith("smart_rules.event_filter.", StringComparison.Ordinal)));

    [Fact]
    public async Task The_snapshot_lists_the_three_settings_with_no_row_as_off_two_manual_and_the_identity_changes_when_the_filter_is_turned_on()
    {
        var off = await HashAsync(History());
        var on = await HashAsync(History(Row(1, true)));
        var wider = await HashAsync(History(Row(1, true, 5)));
        var auto = await HashAsync(History(Row(1, true, 2, "auto")));

        off.Values.ShouldBe(new Dictionary<string, string> { ["smart_rules.event_filter.approval"] = "manual", ["smart_rules.event_filter.enabled"] = "false", ["smart_rules.event_filter.results_within_days"] = "2" });
        on.Values["smart_rules.event_filter.enabled"].ShouldBe("true");
        new[] { off.Hash, on.Hash, wider.Hash, auto.Hash }.Distinct().Count().ShouldBe(4, "each of enabled, window and approval changes the configuration identity");
    }

    [Fact]
    public async Task An_unreadable_history_makes_the_snapshot_partial_and_is_not_read_as_off()
    {
        var decisions = Substitute.For<IEventFilterDecisions>();
        decisions.RowsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<EventFilterRow>>>(_ => throw new InvalidOperationException("away"));

        var reading = await Source(decisions).ReadAsync(Ct);

        reading.Unreadable.ShouldContain("event_filter");
        reading.Components.ShouldNotContain(c => c.Key.StartsWith("smart_rules.event_filter.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_the_port_nothing_is_listed_and_the_other_smart_rule_keys_stay()
    {
        var reading = await Source(null).ReadAsync(Ct);

        reading.Components.ShouldNotContain(c => c.Key.StartsWith("smart_rules.event_filter.", StringComparison.Ordinal));
        reading.Components.ShouldContain(c => c.Key == "smart_rules.gate_enabled");
    }

    [Fact]
    public void Turning_the_filter_on_is_protective_off_is_frozen_and_the_window_and_approval_follow_the_tables()
    {
        PaperConfigKeys.Classify("smart_rules.event_filter.enabled", "false", "true").ShouldBe(ConfigClass.RiskDown);
        PaperConfigKeys.Classify("smart_rules.event_filter.enabled", "true", "false").ShouldBe(ConfigClass.Frozen);
        PaperConfigKeys.Classify("smart_rules.event_filter.results_within_days", "2", "5").ShouldBe(ConfigClass.RiskDown);
        PaperConfigKeys.Classify("smart_rules.event_filter.results_within_days", "5", "2").ShouldBe(ConfigClass.Frozen);
        PaperConfigKeys.Classify("smart_rules.event_filter.approval", "manual", "auto").ShouldBe(ConfigClass.Frozen);
        PaperConfigKeys.Describe("smart_rules.event_filter.enabled").ShouldContain("switching on is risk_down");
    }

    // ---- the loss-tag events

    private static LossTagEvents Events(ICalendarRows rows, IResultsFilingTimes filings, DateTimeOffset now)
    {
        var settings = Substitute.For<IFilingSettings>();
        settings.LagMinutes.Returns(5);
        var calendar = Substitute.For<ITradingCalendar>();
        calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(now);
        return new LossTagEvents(rows, filings, calendar, settings, clock, NullLogger<LossTagEvents>.Instance);
    }

    [Fact]
    public async Task The_events_are_the_filing_broadcasts_and_the_open_of_a_meeting_without_a_filing_and_an_unknown_instrument_is_absent()
    {
        var known = Guid.NewGuid();
        var unknown = Guid.NewGuid();
        var rows = Substitute.For<ICalendarRows>();
        var sha = new string('2', 64);
        var meeting = new CalendarRow(new StoredFiling(FilingId.Create(known, FilingKind.BoardMeeting, new DateOnly(2026, 3, 31), sha), known, FilingKind.BoardMeeting, new DateOnly(2026, 3, 31), new DateTimeOffset(2026, 4, 1, 5, 0, 0, TimeSpan.Zero), BroadcastState.Ok, null, DateTimeOffset.UnixEpoch, sha, 1, "m.csv", null), new DateOnly(2026, 5, 12), MeetingRow.Results, MeetingRow.Scheduled);
        rows.ResultsRowsAsync(Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<CalendarRow>>([meeting]));
        var filings = Substitute.For<IResultsFilingTimes>();
        filings.ReadAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<ResultsFilingTime>>([]));

        var all = await Events(rows, filings, new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero)).ReadAsync([known, unknown], Ct);

        all.ShouldNotBeNull().ContainsKey(unknown).ShouldBeFalse();
        var instant = all[known].ShouldHaveSingleItem();
        (instant.FromCalendar, instant.At).ShouldBe((true, new DateTimeOffset(2026, 5, 12, 9, 15, 0, TimeSpan.FromMinutes(330)).ToUniversalTime()));
    }

    [Fact]
    public async Task A_failing_read_an_empty_list_or_too_many_instruments_answer_not_supplied_and_a_cancellation_passes()
    {
        var rows = Substitute.For<ICalendarRows>();
        var filings = Substitute.For<IResultsFilingTimes>();
        var service = Events(rows, filings, DateTimeOffset.UnixEpoch);
        rows.ResultsRowsAsync(Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns<ValueTask<IReadOnlyList<CalendarRow>>>(_ => throw new InvalidOperationException("away"));

        (await service.ReadAsync([Guid.NewGuid()], Ct)).ShouldBeNull();
        (await service.ReadAsync([], Ct)).ShouldBeNull();
        (await service.ReadAsync([.. Enumerable.Range(0, LossTagEvents.MaxInstruments + 1).Select(_ => Guid.NewGuid())], Ct)).ShouldBeNull();
        rows.ResultsRowsAsync(Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns<ValueTask<IReadOnlyList<CalendarRow>>>(_ => throw new OperationCanceledException());
        await Should.ThrowAsync<OperationCanceledException>(async () => await service.ReadAsync([Guid.NewGuid()], Ct));
    }
}
