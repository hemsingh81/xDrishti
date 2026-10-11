using XDrishti.Domain.Market;

namespace XDrishti.Application.Tests.Market;

public sealed class StartRefreshHandlerTests
{
    private readonly MarketFixture _f = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_manual_refresh_is_registered_and_queued()
    {
        var result = await _f.Start.HandleAsync(new XDrishti.Application.Market.Refresh.StartRefreshCommand(RefreshTrigger.Manual, false, MarketFixture.Owner), Ct);

        result.IsSuccess.ShouldBeTrue();
        var run = _f.Data.Runs.Single();
        run.Id.ShouldBe(result.Value.RefreshId);
        run.IsRunning.ShouldBeTrue();
        (run.Trigger, run.Force, run.RequestedBy, run.StartedAt).ShouldBe((RefreshTrigger.Manual, false, MarketFixture.Owner, _f.Clock.GetUtcNow()));
        _f.Data.Jobs.ShouldHaveSingleItem().RunId.ShouldBe(run.Id);
    }

    [Fact]
    public async Task A_forced_manual_refresh_is_recorded_as_forced()
    {
        await _f.Start.HandleAsync(new XDrishti.Application.Market.Refresh.StartRefreshCommand(RefreshTrigger.Manual, true, MarketFixture.Owner), Ct);

        var run = _f.Data.Runs.Single();
        (run.Trigger, run.Force).ShouldBe((RefreshTrigger.Forced, true));
    }

    [Fact]
    public async Task The_daily_refresh_is_never_forced()
    {
        await _f.Start.HandleAsync(new XDrishti.Application.Market.Refresh.StartRefreshCommand(RefreshTrigger.Daily, true, "system:daily-refresh"), Ct);

        var run = _f.Data.Runs.Single();
        (run.Trigger, run.Force).ShouldBe((RefreshTrigger.Daily, false));
    }

    [Fact]
    public async Task While_one_is_running_another_is_refused_and_not_queued()
    {
        _f.Register();

        var result = await _f.Start.HandleAsync(new XDrishti.Application.Market.Refresh.StartRefreshCommand(RefreshTrigger.Manual, false, MarketFixture.Owner), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("instruments.refresh_running");
        result.Error.Kind.ShouldBe(XDrishti.Domain.Common.ErrorKind.Conflict);
        _f.Data.Runs.Count.ShouldBe(1);
        _f.Data.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_that_never_ended_stops_blocking_after_half_an_hour()
    {
        _f.Register();
        _f.Clock.Advance(TimeSpan.FromMinutes(31));

        var result = await _f.Start.HandleAsync(new XDrishti.Application.Market.Refresh.StartRefreshCommand(RefreshTrigger.Manual, false, MarketFixture.Owner), Ct);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_finished_refresh_does_not_block_the_next()
    {
        _f.AddFinished();

        var result = await _f.Start.HandleAsync(new XDrishti.Application.Market.Refresh.StartRefreshCommand(RefreshTrigger.Manual, false, MarketFixture.Owner), Ct);

        result.IsSuccess.ShouldBeTrue();
    }
}
