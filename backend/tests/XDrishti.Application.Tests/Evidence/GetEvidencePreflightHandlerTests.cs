using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using XDrishti.Application.Abstractions.Evidence;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.AutoSwitch;
using XDrishti.Application.ChargeSheet;
using XDrishti.Application.Evidence;
using XDrishti.Application.Lab;
using XDrishti.Application.Market.Context;
using XDrishti.Application.Market.History;
using XDrishti.Application.Platform.SystemStatus;
using XDrishti.Application.Replay;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Evidence;

/// <summary>The state rules of <c>xd evidence preflight</c> (spec EV-01): each item, and UNAVAILABLE (exception type only) when its read throws.</summary>
public sealed class GetEvidencePreflightHandlerTests
{
    private static readonly DateOnly Today = new(2026, 11, 12); // a Thursday

    private readonly IQueryHandler<GetSystemStatusQuery, SystemStatusResponse> _status = Substitute.For<IQueryHandler<GetSystemStatusQuery, SystemStatusResponse>>();
    private readonly IEvidencePreflightHost _host = Substitute.For<IEvidencePreflightHost>();
    private readonly IQueryHandler<GetChargeSheetStatusQuery, Result<ChargeSheetStatusView>> _sheet = Substitute.For<IQueryHandler<GetChargeSheetStatusQuery, Result<ChargeSheetStatusView>>>();
    private readonly IQueryHandler<GetContextCoverageQuery, ContextCoverageView> _context = Substitute.For<IQueryHandler<GetContextCoverageQuery, ContextCoverageView>>();
    private readonly IQueryHandler<GetHoldOutQuery, Result<HoldOutView>> _holdOut = Substitute.For<IQueryHandler<GetHoldOutQuery, Result<HoldOutView>>>();
    private readonly ICellStatsStore _cells = Substitute.For<ICellStatsStore>();
    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();
    private readonly IModelRegistry _models = Substitute.For<IModelRegistry>();
    private readonly IQueryHandler<GetShadowSummaryQuery, ShadowSummaryView> _shadow = Substitute.For<IQueryHandler<GetShadowSummaryQuery, ShadowSummaryView>>();
    private readonly IServiceScheduleReader _schedules = Substitute.For<IServiceScheduleReader>();
    private readonly IQueryHandler<GetAutoQuery, AutoView> _auto = Substitute.For<IQueryHandler<GetAutoQuery, AutoView>>();
    private readonly IQueryHandler<ShowHistoryQuery, IReadOnlyList<HistoryRow>> _history = Substitute.For<IQueryHandler<ShowHistoryQuery, IReadOnlyList<HistoryRow>>>();
    private readonly IBackfillSettings _backfill = Substitute.For<IBackfillSettings>();
    private readonly ISimAllBackfillGate _gate = Substitute.For<ISimAllBackfillGate>();
    private readonly SystemReplayOptions _replay = new();

    /// <summary>A world in which every item is MET.</summary>
    public GetEvidencePreflightHandlerTests()
    {
        _status.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new SystemStatusResponse(null!, new DatabaseStatus(true, null, null, "20261101_Last", 0, 1, null), [], true, DateTimeOffset.UnixEpoch));
        _host.NewestCompiledMigration().Returns("20261101_Last");
        _host.ComposeApiMemLimitMib().Returns(1024);
        _host.NseHistoryEnabled().Returns(true);
        _host.ComposePath().Returns("/repo/deploy/compose.yaml");
        _gate.IncompleteReasonAsync(Arg.Any<CancellationToken>()).Returns((string?)null);
        _backfill.AdmitVerified.Returns(true);
        _sheet.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Result.Success(Sheet("confirmed")));
        _context.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Context());
        _holdOut.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Result.Success(new HoldOutView(true, false, new HoldOutBlockView(new DateOnly(2026, 5, 1), new DateOnly(2016, 1, 1), new DateOnly(2026, 11, 11), 6, "h", DateTimeOffset.UnixEpoch), null, [])));
        _cells.NewestAsOfAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new DateOnly(2026, 11, 9));
        _calendar.GetAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new TradingCalendar());
        _models.ListAsync(null, 0, Arg.Any<CancellationToken>()).ReturnsForAnyArgs([new StoredModel(7, ModelRole.Champion, null!, 1)]);
        _shadow.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Shadow(true));
        _schedules.GetAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new ServiceScheduleInfo(true, null, "daily", null));
        _auto.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Auto(true, true));
        _history.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs([Row("done", 0, "yes")]);
    }

    [Fact]
    public async Task Every_item_is_met_when_every_fact_is_in_place()
    {
        var view = await RunAsync();

        view.Items.Count.ShouldBe(12);
        view.Items.ShouldAllBe(i => i.State == GetEvidencePreflightHandler.Met);
    }

    [Fact]
    public async Task The_version_is_not_met_when_a_migration_is_pending_or_another_one_is_newest()
    {
        _status.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new SystemStatusResponse(null!, new DatabaseStatus(true, null, null, "20261031_Older", 1, 1, null), [], false, DateTimeOffset.UnixEpoch));

        var item = Item(await RunAsync(), GetEvidencePreflightHandler.DeployedVersion);

        item.State.ShouldBe(GetEvidencePreflightHandler.NotMet);
        item.Reason.ShouldContain("20261031_Older");
        item.Reason.ShouldContain("20261101_Last");
    }

    [Fact]
    public async Task The_version_is_unavailable_when_the_database_is_not_reachable()
    {
        _status.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new SystemStatusResponse(null!, new DatabaseStatus(false, null, null, null, 0, 1, "NpgsqlException"), [], false, DateTimeOffset.UnixEpoch));

        Item(await RunAsync(), GetEvidencePreflightHandler.DeployedVersion).State.ShouldBe(GetEvidencePreflightHandler.Unavailable);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("not_confirmed")]
    [InlineData("revoked")]
    public async Task A_charge_sheet_that_is_not_confirmed_at_the_current_rates_is_not_met(string state)
    {
        _sheet.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Result.Success(Sheet(state)));

        Item(await RunAsync(), GetEvidencePreflightHandler.ChargeSheetItem).State.ShouldBe(GetEvidencePreflightHandler.NotMet);
    }

    [Fact]
    public async Task A_stale_confirmation_says_the_rates_hash_differs()
    {
        _sheet.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Result.Success(Sheet("stale")));

        Item(await RunAsync(), GetEvidencePreflightHandler.ChargeSheetItem).Reason.ShouldContain("another rates hash");
    }

    [Fact]
    public async Task The_context_is_not_met_while_the_regime_is_not_readable()
    {
        _context.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Context() with { RegimeReadableFrom = null });

        Item(await RunAsync(), GetEvidencePreflightHandler.ContextItem).State.ShouldBe(GetEvidencePreflightHandler.NotMet);
    }

    [Fact]
    public async Task The_context_is_not_met_when_a_prerequisite_is_missing()
    {
        _context.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Context() with { Missing = "INDIA VIX is not tracked" });

        var item = Item(await RunAsync(), GetEvidencePreflightHandler.ContextItem);

        item.State.ShouldBe(GetEvidencePreflightHandler.NotMet);
        item.Reason.ShouldContain("INDIA VIX");
    }

    [Fact]
    public async Task A_hold_out_not_yet_fixed_says_it_is_fixed_at_the_first_lab_post()
    {
        _holdOut.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Result.Success(new HoldOutView(true, false, null, null, [])));

        var item = Item(await RunAsync(), GetEvidencePreflightHandler.HoldOutItem);

        item.State.ShouldBe(GetEvidencePreflightHandler.NotMet);
        item.Reason.ShouldContain("fixed at the first Lab post (ADR 0207)");
    }

    [Fact]
    public async Task A_fixed_hold_out_prints_the_block_start_and_end()
    {
        var item = Item(await RunAsync(), GetEvidencePreflightHandler.HoldOutItem);

        item.Reason.ShouldContain("2026-05-01");
        item.Reason.ShouldContain("2026-11-11");
    }

    [Theory]
    [InlineData(2026, 11, 5, true)]    [InlineData(2026, 11, 4, false)]
    public async Task The_cell_snapshot_is_met_up_to_five_sessions_old(int year, int month, int day, bool met)
    {
        _cells.NewestAsOfAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new DateOnly(year, month, day));

        var item = Item(await RunAsync(), GetEvidencePreflightHandler.CellsItem);

        item.State.ShouldBe(met ? GetEvidencePreflightHandler.Met : GetEvidencePreflightHandler.NotMet);
    }

    [Fact]
    public async Task No_cell_snapshot_is_not_met()
    {
        _cells.NewestAsOfAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs((DateOnly?)null);

        Item(await RunAsync(), GetEvidencePreflightHandler.CellsItem).State.ShouldBe(GetEvidencePreflightHandler.NotMet);
    }

    [Fact]
    public async Task No_champion_is_not_met()
    {
        _models.ListAsync(null, 0, Arg.Any<CancellationToken>()).ReturnsForAnyArgs([]);

        Item(await RunAsync(), GetEvidencePreflightHandler.ChampionItem).State.ShouldBe(GetEvidencePreflightHandler.NotMet);
    }

    [Fact]
    public async Task A_shadow_switch_that_is_off_is_not_met()
    {
        _shadow.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Shadow(false));

        Item(await RunAsync(), GetEvidencePreflightHandler.ShadowItem).State.ShouldBe(GetEvidencePreflightHandler.NotMet);
    }

    [Fact]
    public async Task A_paused_capture_service_is_not_met_and_an_unregistered_one_is_unavailable()
    {
        _schedules.GetAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new ServiceScheduleInfo(false, null, "paused", null));
        Item(await RunAsync(), GetEvidencePreflightHandler.CaptureItem).State.ShouldBe(GetEvidencePreflightHandler.NotMet);

        _schedules.GetAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs((ServiceScheduleInfo?)null);
        Item(await RunAsync(), GetEvidencePreflightHandler.CaptureItem).State.ShouldBe(GetEvidencePreflightHandler.Unavailable);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task Auto_is_met_only_when_on_and_the_chain_is_complete(bool on, bool complete)
    {
        _auto.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Auto(on, complete));

        Item(await RunAsync(), GetEvidencePreflightHandler.AutoItem).State.ShouldBe(GetEvidencePreflightHandler.NotMet);
    }

    [Fact]
    public async Task An_incomplete_backfill_is_not_met_with_the_reason_of_the_replay_gate_and_the_counts()
    {
        _history.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs([Row("done", 0, "yes"), Row("partial", 0, "no_seam"), Row("partial", 2, "no_seam")]);
        _gate.IncompleteReasonAsync(Arg.Any<CancellationToken>()).Returns("the 1-minute history backfill has unfinished chunks for 2 instruments.");

        var item = Item(await RunAsync(), GetEvidencePreflightHandler.BackfillItem);

        item.State.ShouldBe(GetEvidencePreflightHandler.NotMet);
        item.Reason.ShouldContain("unfinished chunks for 2 instruments");
        item.Reason.ShouldContain("1 verified, 1 pending, 1 failed chunks");
    }

    [Fact]
    public async Task A_row_with_no_chunks_and_a_known_seam_does_not_keep_the_backfill_from_being_met()
    {
        _history.HandleAsync(null!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs([Row("none", 0, "no_seam"), Row("seam_unresolved", 0, "seam_unresolved"), Row("seam_jump", 0, "seam_jump"), Row("done", 0, "yes")]);

        var item = Item(await RunAsync(), GetEvidencePreflightHandler.BackfillItem);

        item.State.ShouldBe(GetEvidencePreflightHandler.Met);
        item.Reason.ShouldContain("2 with an unresolved seam or a seam jump");
    }

    [Fact]
    public async Task A_backfill_gate_that_throws_is_unavailable()
    {
        _gate.IncompleteReasonAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("x"));

        Item(await RunAsync(), GetEvidencePreflightHandler.BackfillItem).State.ShouldBe(GetEvidencePreflightHandler.Unavailable);
    }

    [Fact]
    public async Task The_memory_line_names_the_compose_file_read_and_the_source_of_the_window()
    {
        var item = Item(await RunAsync(), GetEvidencePreflightHandler.MemoryItem);

        item.Reason.ShouldContain("/repo/deploy/compose.yaml");
        item.Reason.ShouldContain("Replay:MaxWindowMb from this host's configuration");
    }

    [Fact]
    public async Task The_replay_window_must_fit_inside_the_heap_share_of_the_memory_limit()
    {
        _replay.MaxWindowMb = 600;

        var item = Item(await RunAsync(), GetEvidencePreflightHandler.MemoryItem);

        item.State.ShouldBe(GetEvidencePreflightHandler.NotMet);
        item.Reason.ShouldContain("512 MiB");
    }

    [Fact]
    public async Task The_default_window_is_met_in_one_gibibyte_and_prints_the_largest_scope()
    {
        var item = Item(await RunAsync(), GetEvidencePreflightHandler.MemoryItem);

        item.State.ShouldBe(GetEvidencePreflightHandler.Met);
        item.Reason.ShouldContain("largest scope 53 instruments");
    }

    [Fact]
    public async Task An_unreadable_memory_limit_is_unavailable()
    {
        _host.ComposeApiMemLimitMib().Returns((long?)null);

        Item(await RunAsync(), GetEvidencePreflightHandler.MemoryItem).State.ShouldBe(GetEvidencePreflightHandler.Unavailable);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Both_history_switches_must_be_on(bool nse, bool admit)
    {
        _host.NseHistoryEnabled().Returns(nse);
        _backfill.AdmitVerified.Returns(admit);

        Item(await RunAsync(), GetEvidencePreflightHandler.HistorySwitchesItem).State.ShouldBe(GetEvidencePreflightHandler.NotMet);
    }

    [Theory]
    [InlineData(GetEvidencePreflightHandler.DeployedVersion)]
    [InlineData(GetEvidencePreflightHandler.ChargeSheetItem)]
    [InlineData(GetEvidencePreflightHandler.ContextItem)]
    [InlineData(GetEvidencePreflightHandler.HoldOutItem)]
    [InlineData(GetEvidencePreflightHandler.CellsItem)]
    [InlineData(GetEvidencePreflightHandler.ChampionItem)]
    [InlineData(GetEvidencePreflightHandler.ShadowItem)]
    [InlineData(GetEvidencePreflightHandler.CaptureItem)]
    [InlineData(GetEvidencePreflightHandler.AutoItem)]
    [InlineData(GetEvidencePreflightHandler.BackfillItem)]
    [InlineData(GetEvidencePreflightHandler.MemoryItem)]
    [InlineData(GetEvidencePreflightHandler.HistorySwitchesItem)]
    public async Task A_read_that_throws_is_unavailable_with_the_exception_type_only(string item)
    {
        const string Secret = "password=hunter2";
        var boom = new InvalidOperationException(Secret);
        switch (item)
        {
            case GetEvidencePreflightHandler.DeployedVersion: _status.HandleAsync(null!, Arg.Any<CancellationToken>()).ThrowsAsyncForAnyArgs(boom); break;
            case GetEvidencePreflightHandler.ChargeSheetItem: _sheet.HandleAsync(null!, Arg.Any<CancellationToken>()).ThrowsAsyncForAnyArgs(boom); break;
            case GetEvidencePreflightHandler.ContextItem: _context.HandleAsync(null!, Arg.Any<CancellationToken>()).ThrowsAsyncForAnyArgs(boom); break;
            case GetEvidencePreflightHandler.HoldOutItem: _holdOut.HandleAsync(null!, Arg.Any<CancellationToken>()).ThrowsAsyncForAnyArgs(boom); break;
            case GetEvidencePreflightHandler.CellsItem: _cells.NewestAsOfAsync(Arg.Any<CancellationToken>()).ThrowsAsyncForAnyArgs(boom); break;
            case GetEvidencePreflightHandler.ChampionItem: _models.ListAsync(null, 0, Arg.Any<CancellationToken>()).ThrowsAsyncForAnyArgs(boom); break;
            case GetEvidencePreflightHandler.ShadowItem: _shadow.HandleAsync(null!, Arg.Any<CancellationToken>()).ThrowsAsyncForAnyArgs(boom); break;
            case GetEvidencePreflightHandler.CaptureItem: _schedules.GetAsync(null!, Arg.Any<CancellationToken>()).ThrowsAsyncForAnyArgs(boom); break;
            case GetEvidencePreflightHandler.AutoItem: _auto.HandleAsync(null!, Arg.Any<CancellationToken>()).ThrowsAsyncForAnyArgs(boom); break;
            case GetEvidencePreflightHandler.BackfillItem: _history.HandleAsync(null!, Arg.Any<CancellationToken>()).ThrowsAsyncForAnyArgs(boom); break;
            case GetEvidencePreflightHandler.MemoryItem: _host.ComposeApiMemLimitMib().Throws(boom); break;
            default: _host.NseHistoryEnabled().Throws(boom); break;
        }

        var view = await RunAsync();

        var result = Item(view, item);
        result.State.ShouldBe(GetEvidencePreflightHandler.Unavailable);
        result.Reason.ShouldContain(nameof(InvalidOperationException));
        result.Reason.ShouldNotContain(Secret);
        view.Items.Count(i => i.State == GetEvidencePreflightHandler.Unavailable).ShouldBe(1);
    }

    [Fact]
    public async Task A_cancelled_run_is_not_swallowed()
    {
        using var cancel = new CancellationTokenSource();
        _status.HandleAsync(null!, Arg.Any<CancellationToken>()).ThrowsAsyncForAnyArgs(new OperationCanceledException(cancel.Token));
        await cancel.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await Handler().HandleAsync(new GetEvidencePreflightQuery(), cancel.Token));
    }

    private static EvidencePreflightItem Item(EvidencePreflightView view, string name) => view.Items.Single(i => i.Item == name);

    private async Task<EvidencePreflightView> RunAsync() => await Handler().HandleAsync(new GetEvidencePreflightQuery(), TestContext.Current.CancellationToken);

    private GetEvidencePreflightHandler Handler()
    {
        // 12 Nov 2026 12:00 UTC is 17:30 India time: the 12th session has closed, so the last closed session is the 12th.
        var clock = new FakeTimeProvider(new DateTimeOffset(Today.Year, Today.Month, Today.Day, 12, 0, 0, TimeSpan.Zero));
        return new GetEvidencePreflightHandler(_status, _host, _sheet, _context, _holdOut, _cells, _calendar, _models, _shadow, _schedules, _auto, _history, _backfill, _gate, _replay, clock);
    }

    private static ChargeSheetStatusView Sheet(string state) => new(true, state, "text", null, null, null, "meaning", []);

    private static ContextCoverageView Context() => new(
        true, null, null, "NIFTY 50", "INDIA VIX", 100, new DateOnly(2026, 1, 1), Today, 0, false, new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 1), 90, 0, 10, false, 0, 0, [], "src", null!);

    private static ShadowSummaryView Shadow(bool enabled) => new(enabled, false, null, "s", "c", null!, null!, [], null!, null!, null!);

    private static AutoView Auto(bool on, bool complete) => new(on, complete, complete ? null : "nightly is paused", "skeleton", [], [], new AutoNightView("no_run", null, null, null), [], "no order");

    private static HistoryRow Row(string status, int failed, string verified) => new(Guid.NewGuid(), "SYM", null, status, 1, failed, 2, "ok", verified);
}
