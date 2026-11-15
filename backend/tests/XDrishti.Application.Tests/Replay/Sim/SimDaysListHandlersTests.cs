using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Sim;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Tests.Replay.Sim;

/// <summary>SR-04: the list of simulations (newest first, the active one marked, offset pages), the list of a run's days (a projection with the counts and the seen_holdout flag, both sort orders), the defaults read and the hasNewSessions decision.</summary>
public sealed class SimDaysListHandlersTests
{
    private static readonly DateOnly D1 = new(2026, 11, 9);
    private static readonly DateOnly D2 = new(2026, 11, 11);
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 6, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeSimDaysStore _store = new();

    private async Task<Guid> RunAsync(int minutes, string phase)
    {
        var p = new ReplayParams(D1, D2, new ReplayScope([Guid.NewGuid(), Guid.NewGuid()], [Guid.NewGuid()]), 500_000m, ReplayRiskProfile.Standard, null, ReplayVersionRule.Start, ReplayMode.Learning);
        var id = Guid.NewGuid();
        (await _store.InsertAsync(new NewSimDaysRun(id, new string('a', 64), ReplayJson.Canonical(p), "{}", "{}", null, 500_000m, "learning", D1, D2, D2, D1, null, null, T0.AddMinutes(minutes)), Ct)).ShouldBeTrue();
        _store.Put(_store.Row(id) with { Phase = phase });
        return id;
    }

    private static SimDaySnapshotRow Day(DateOnly session, decimal equity, params string[] labels)
    {
        var record = new ReplaySessionRecord(session, [], [], [], [], [], [], equity, null, null, 0, labels, []);
        var picks = ReplayJson.Canonical(new[] { new ReplayCandidateRow("AAA", "BUY", "k", 1), new ReplayCandidateRow("BBB", "BUY", "k", 2) });
        return new SimDaySnapshotRow(session, ReplayJson.Canonical(record), picks, "[]", "[]", equity, new string('b', 64), D2);
    }

    [Fact]
    public async Task The_list_is_newest_first_marks_the_active_run_and_pages_by_offset()
    {
        var old = await RunAsync(0, SimDays.Complete);
        var mid = await RunAsync(1, SimDays.Cancelled);
        var active = await RunAsync(2, SimDays.Idle);
        _store.Days[old].Add(Day(D1, 510_000m));
        var handler = new ListSimDaysHandler(_store);

        var first = await handler.HandleAsync(new ListSimDaysQuery("2", null), Ct);
        var second = await handler.HandleAsync(new ListSimDaysQuery("2", first.Value.NextCursor), Ct);

        first.Value.Items.Select(i => i.Id).ShouldBe([active, mid]);
        first.Value.Items[0].Active.ShouldBeTrue();
        first.Value.Items[1].Active.ShouldBeFalse();
        first.Value.Items[0].Scope.ShouldBe(new SimDaysScopeView(2, 1));
        first.Value.Items[0].Equity.ShouldBe(500_000m, "the capital before the first stored day");
        first.Value.NextCursor.ShouldBe("o2");
        second.Value.Items.Single().Id.ShouldBe(old);
        (second.Value.Items[0].Equity, second.Value.Items[0].DaysDone).ShouldBe((510_000m, 1));
        second.Value.NextCursor.ShouldBeNull();
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData("101", null)]
    [InlineData("x", null)]
    [InlineData(null, "p2")]
    [InlineData(null, "o-1")]
    public async Task A_bad_limit_or_cursor_is_sim_invalid(string? limit, string? cursor) =>
        (await new ListSimDaysHandler(_store).HandleAsync(new ListSimDaysQuery(limit, cursor), Ct)).Error.Code.ShouldBe(SimDays.InvalidCode);

    [Fact]
    public async Task The_days_list_projects_the_counts_and_the_seen_holdout_flag_in_either_order()
    {
        var id = await RunAsync(0, SimDays.Idle);
        _store.Days[id].Add(Day(D2, 1_100m, SeenHoldOut.Label(new DateOnly(2026, 11, 1))));
        _store.Days[id].Add(Day(D1, 1_000m, "hindsight"));
        var handler = new ListSimDayDaysHandler(_store);

        var asc = await handler.HandleAsync(new ListSimDayDaysQuery(id, null, null, null), Ct);
        var desc = await handler.HandleAsync(new ListSimDayDaysQuery(id, "1", null, "desc"), Ct);

        asc.Value.Items.ShouldBe([new SimDayListItemView(D1, 1_000m, 0, 2, false), new SimDayListItemView(D2, 1_100m, 0, 2, true)]);
        asc.Value.NextCursor.ShouldBeNull();
        desc.Value.Items.Single().Session.ShouldBe(D2);
        desc.Value.NextCursor.ShouldBe("o1");
    }

    [Fact]
    public async Task The_days_list_of_an_unknown_run_is_not_found_and_a_bad_sort_is_invalid()
    {
        var handler = new ListSimDayDaysHandler(_store);

        (await handler.HandleAsync(new ListSimDayDaysQuery(Guid.NewGuid(), null, null, null), Ct)).Error.Code.ShouldBe(SimDays.NotFoundCode);
        (await handler.HandleAsync(new ListSimDayDaysQuery(Guid.NewGuid(), null, null, "up"), Ct)).Error.Code.ShouldBe(SimDays.InvalidCode);
    }

    [Fact]
    public async Task The_settings_read_answers_the_configured_defaults()
    {
        var options = new SimDaysOptions { DefaultCapital = 250_000m, DefaultSpeedSecondsPerMinute = 5, AutoContinue = true };

        (await new GetSimDaysSettingsHandler(options).HandleAsync(new GetSimDaysSettingsQuery(), Ct)).ShouldBe(new SimDaysSettingsView(250_000m, 5, true));
    }

    [Fact]
    public async Task Has_new_sessions_only_for_a_complete_run_with_a_readable_session_after_its_end()
    {
        var calendar = new TradingCalendar();
        var complete = _store.Row(await RunAsync(0, SimDays.Complete)) with { Cursor = D2 };
        var friday = new DateOnly(2026, 11, 13);

        SimDaysSupport.HasNewSessions(complete, friday, friday, calendar).ShouldBeTrue();
        SimDaysSupport.HasNewSessions(complete, D2, D2, calendar).ShouldBeFalse("nothing newer is stored");
        SimDaysSupport.HasNewSessions(complete, friday, D2, calendar).ShouldBeFalse("the hold-out clamps the end at the run's end");
        SimDaysSupport.HasNewSessions(complete, null, null, calendar).ShouldBeFalse("no bar is stored");
        SimDaysSupport.HasNewSessions(complete with { Phase = SimDays.Idle }, friday, friday, calendar).ShouldBeFalse("only a complete run continues");
    }
}
