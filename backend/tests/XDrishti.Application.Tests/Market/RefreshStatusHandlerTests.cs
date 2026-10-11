using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Market.Refresh;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Tests.Market;

public sealed class RefreshStatusHandlerTests
{
    private readonly MarketFixture _f = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Before_any_refresh_there_is_nothing_to_show_but_the_schedule()
    {
        var status = await _f.Status.HandleAsync(new GetRefreshStatusQuery(), Ct);

        status.Current.ShouldBeNull();
        status.Last.ShouldBeNull();
        status.TotalActive.ShouldBe(0);
        status.AutoRefresh.Enabled.ShouldBeTrue();
        status.AutoRefresh.TimeIst.ShouldBe("08:45");
    }

    [Fact]
    public async Task The_next_automatic_run_is_the_coming_08_45_India_time()
    {
        // 2026-10-11 04:00 UTC = 09:30 IST: today's 08:45 is over, so tomorrow's is next (03:15 UTC).
        var status = await _f.Status.HandleAsync(new GetRefreshStatusQuery(), Ct);

        status.AutoRefresh.NextRunAt.ShouldBe(new DateTimeOffset(2026, 10, 12, 3, 15, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_disabled_schedule_has_no_next_run()
    {
        _f.Settings.AutoRefreshEnabled = false;

        var status = await _f.Status.HandleAsync(new GetRefreshStatusQuery(), Ct);

        status.AutoRefresh.Enabled.ShouldBeFalse();
        status.AutoRefresh.NextRunAt.ShouldBeNull();
    }

    [Fact]
    public async Task The_running_and_the_last_finished_refresh_are_reported_with_their_progress()
    {
        _f.Data.ActiveCount = 4381;
        _f.AddFinished();
        var running = _f.Register(RefreshTrigger.Forced, force: true);
        running.ReportProgress(RefreshPhase.Parsing, 120_000);

        var status = await _f.Status.HandleAsync(new GetRefreshStatusQuery(), Ct);

        status.TotalActive.ShouldBe(4381);
        status.Current!.Id.ShouldBe(running.Id);
        (status.Current.Status, status.Current.Phase, status.Current.RowsRead, status.Current.Trigger).ShouldBe(("running", "parsing", 120_000L, "forced"));
        (status.Last!.Status, status.Last.Phase, status.Last.Added, status.Last.Retired).ShouldBe(("succeeded", null, 1, 3));
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData(0, 1)]
    [InlineData(5, 5)]
    [InlineData(1000, 100)]
    public async Task The_history_is_newest_first_and_the_page_size_is_clamped(int? take, int expectedTake)
    {
        for (var i = 0; i < 120; i++)
        {
            _f.AddFinished(ago: TimeSpan.FromHours(i + 1));
        }

        var history = await new ListRefreshHistoryHandler(_f.Data).HandleAsync(new ListRefreshHistoryQuery(take), Ct);

        history.Count.ShouldBe(expectedTake);
        history.Select(h => h.StartedAt).ShouldBe(history.Select(h => h.StartedAt).OrderDescending());
    }

    [Fact]
    public void The_view_of_a_run_spells_enums_in_lower_case()
    {
        var run = RefreshRun.Start(RefreshTrigger.Daily, false, "system:daily-refresh", _f.Clock.GetUtcNow());
        run.Fail(_f.Clock.GetUtcNow(), "boom");

        var view = RefreshRunView.From(run);

        (view.Trigger, view.Status, view.Phase, view.Error).ShouldBe(("daily", "failed", null, "boom"));
        NullLogger.Instance.ShouldNotBeNull();
    }
}
