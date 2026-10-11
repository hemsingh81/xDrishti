using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Market.Refresh;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Tests.Market;

public sealed class RecoverInterruptedRefreshesTests
{
    private readonly MarketFixture _f = new();

    private RecoverInterruptedRefreshesHandler Handler => new(_f.Data, _f.Clock, NullLogger<RecoverInterruptedRefreshesHandler>.Instance);

    [Fact]
    public async Task A_run_that_started_before_this_process_is_marked_failed_so_a_new_refresh_can_start()
    {
        var old = _f.Register();
        _f.Clock.Advance(TimeSpan.FromMinutes(3));
        var processStart = _f.Clock.GetUtcNow();
        _f.Clock.Advance(TimeSpan.FromSeconds(5));

        (await Handler.HandleAsync(new RecoverInterruptedRefreshesCommand(processStart), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        old.Status.ShouldBe(RefreshStatus.Failed);
        old.Error!.ShouldContain("did not finish");
        old.FinishedAt.ShouldBe(_f.Clock.GetUtcNow());
        var start = await _f.Start.HandleAsync(new StartRefreshCommand(RefreshTrigger.Manual, false, MarketFixture.Owner), TestContext.Current.CancellationToken);
        start.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_run_started_by_this_process_is_left_alone()
    {
        var processStart = _f.Clock.GetUtcNow();
        _f.Clock.Advance(TimeSpan.FromSeconds(1));
        var mine = _f.Register();

        await Handler.HandleAsync(new RecoverInterruptedRefreshesCommand(processStart), TestContext.Current.CancellationToken);

        mine.IsRunning.ShouldBeTrue();
    }

    [Fact]
    public async Task Finished_runs_are_not_touched()
    {
        var finished = _f.AddFinished();

        await Handler.HandleAsync(new RecoverInterruptedRefreshesCommand(_f.Clock.GetUtcNow().AddDays(1)), TestContext.Current.CancellationToken);

        finished.Status.ShouldBe(RefreshStatus.Succeeded);
    }
}
