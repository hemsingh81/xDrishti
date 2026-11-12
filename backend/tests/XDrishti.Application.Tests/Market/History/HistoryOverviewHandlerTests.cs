using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.History;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Tests.Market.History;

/// <summary>CF-04e criterion 9: the read model of the history card.</summary>
public sealed class HistoryOverviewHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Targets(params FetchTarget[] list) : IFetchTargets
    {
        public ValueTask<IReadOnlyList<FetchTarget>> ListAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<FetchTarget>>(list);
    }

    private static readonly DateTimeOffset Last = new(2026, 11, 10, 15, 29, 0, IndiaTime.Offset);

    private static FetchTarget Target(string symbol, int firstYear) => new(Guid.NewGuid(), symbol, "share", "1", "NSE_EQ", Last, new DateTimeOffset(firstYear, 1, 2, 9, 15, 0, IndiaTime.Offset));

    private static (GetHistoryOverviewHandler Handler, FakeBackfillStore Store, FakeBackfillSettings Settings, IServiceRunStore Runs) Build(FetchTarget[] targets, int hourIst = 14)
    {
        var store = new FakeBackfillStore();
        var settings = new FakeBackfillSettings { Enabled = false };
        var runs = Substitute.For<IServiceRunStore>();
        runs.GetRunningServicesAsync(Ct).ReturnsForAnyArgs(new HashSet<string>());
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, hourIst, 0, 0, IndiaTime.Offset));
        return (new GetHistoryOverviewHandler(new Targets(targets), store, settings, new FakeBackfillCalendar(), runs, clock), store, settings, runs);
    }

    [Fact]
    public async Task Rows_are_in_symbol_order_with_the_four_states_and_totals_that_add_up()
    {
        var done = Target("DDD", 2018);
        var partial = Target("CCC", 2022);
        var unresolved = Target("BBB", 2024);
        var jump = Target("AAA", 2020);
        var (handler, store, _, _) = Build([done, partial, unresolved, jump]);
        for (var i = 0; i < 10; i++)
        {
            await store.SaveChunkAsync(new(done.InstrumentId, new DateOnly(2020, 1, 1).AddDays(i * 90), new DateOnly(2020, 1, 1).AddDays((i + 1) * 90), "done", 5, 1, null), Ct);
            await store.SaveChunkAsync(new(partial.InstrumentId, new DateOnly(2020, 1, 1).AddDays(i * 90), new DateOnly(2020, 1, 1).AddDays((i + 1) * 90), i < 4 ? "done" : i == 4 ? "failed" : "planned", 5, 1, null), Ct);
        }

        store.Records[unresolved.InstrumentId] = new(unresolved.InstrumentId, unresolved.FirstBar!.Value, "unresolved", Last, 70.5, 12.0, "x", false, null);
        store.Records[jump.InstrumentId] = new(jump.InstrumentId, jump.FirstBar!.Value, "passed", Last, 1.0, 100.0, null, true, 32.0);

        var view = await handler.HandleAsync(new GetHistoryOverviewQuery(), Ct);

        view.Instruments.Select(r => r.Symbol).ShouldBe(["AAA", "BBB", "CCC", "DDD"]);
        view.Instruments.Select(r => r.BackfillStatus).ShouldBe(["seam_jump", "seam_unresolved", "partial", "done"]);
        view.Instruments[0].SeamJumpPct.ShouldBe(32.0);
        view.Instruments[1].Probe.Status.ShouldBe("unresolved");
        view.Instruments[1].Probe.MedianBps.ShouldBe(70.5);
        view.Instruments[2].Chunks.ShouldBe(new HistoryChunksView(4, 1, 10));
        view.Totals.Instruments.ShouldBe(4);
        view.Totals.ByStatus.ShouldBe(new HistoryByStatusView(0, 0, 1, 1, 1, 1));
        view.Totals.Chunks.Total.ShouldBe(20);
    }

    [Fact]
    public async Task Depth_buckets_follow_HistoryDepth_and_the_switch_is_read_only_with_a_reason_while_off()
    {
        var (handler, _, _, _) = Build([Target("A", 2025), Target("B", 2024), Target("C", 2022), Target("D", 2020)]);

        var view = await handler.HandleAsync(new GetHistoryOverviewQuery(), Ct);

        view.Totals.Years.ShouldBe(new HistoryDepthView(1, 1, 1, 1));
        view.Switch.Enabled.ShouldBeFalse();
        view.Switch.Editable.ShouldBeFalse();
        view.Switch.Reason.ShouldNotBeNullOrEmpty();
        view.RunWindow.ShouldBe("20:00-06:00");
        view.InWindow.ShouldBeFalse();
        view.Running.ShouldBeFalse();
        view.ServiceId.ShouldBe("history-backfill");
    }

    [Fact]
    public async Task With_no_instrument_the_lists_are_empty_and_a_running_service_in_window_is_reported()
    {
        var (handler, _, settings, runs) = Build([], hourIst: 21);
        settings.Enabled = true;
        runs.GetRunningServicesAsync(Ct).ReturnsForAnyArgs(new HashSet<string> { "history-backfill" });

        var view = await handler.HandleAsync(new GetHistoryOverviewQuery(), Ct);

        view.Instruments.ShouldBeEmpty();
        view.Totals.Instruments.ShouldBe(0);
        view.Running.ShouldBeTrue();
        view.InWindow.ShouldBeTrue();
        view.Switch.Reason.ShouldBeNull();
    }
}
