using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Market.Refresh;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Tests.Market;

/// <summary>Builds the refresh handlers on in-memory ports and a controllable clock. Everything is made up or taken from the committed samples.</summary>
internal sealed class MarketFixture
{
    public const string Owner = "owner@example.invalid";

    public FakeTimeProvider Clock { get; } = new(MasterFixtures.Now);

    public InMemoryMarketData Data { get; } = new();

    public FakeInstrumentSource Source { get; } = new();

    public FakeRefreshSettings Settings { get; } = new();

    public StartRefreshHandler Start => new(Data, Data, Clock, NullLogger<StartRefreshHandler>.Instance);

    public ExecuteRefreshHandler Execute => new(Data, Source, Data, Clock, NullLogger<ExecuteRefreshHandler>.Instance);

    public GetRefreshStatusHandler Status => new(Data, Data, ServiceReaders.Schedules(Clock, refresh: Settings));

    /// <summary>A registered, running refresh.</summary>
    public RefreshRun Register(RefreshTrigger trigger = RefreshTrigger.Manual, bool force = false)
    {
        var run = RefreshRun.Start(trigger, force, Owner, Clock.GetUtcNow());
        Data.Runs.Add(run);
        return run;
    }

    /// <summary>A refresh that finished earlier with the given stamps (what makes the next one conditional).</summary>
    public RefreshRun AddFinished(RefreshStatus status = RefreshStatus.Succeeded, TimeSpan? ago = null)
    {
        var run = RefreshRun.Start(RefreshTrigger.Daily, false, Owner, Clock.GetUtcNow() - (ago ?? TimeSpan.FromHours(24)));
        if (status == RefreshStatus.Succeeded)
        {
            run.Succeed(run.StartedAt, FakeInstrumentSource.DhanStamp, FakeInstrumentSource.SectorStamp, Outcome());
        }
        else if (status == RefreshStatus.Unchanged)
        {
            run.MarkUnchanged(run.StartedAt, FakeInstrumentSource.DhanStamp, FakeInstrumentSource.SectorStamp, 60);
        }
        else
        {
            run.Fail(run.StartedAt, "earlier failure");
        }

        Data.Runs.Add(run);
        return run;
    }

    public static RefreshOutcome Outcome() => new(1, 2, 3, 4, 5, 6, 0, 7, 8, 9, 60, 75, true, new Dictionary<string, int> { ["share"] = 30 }, []);

    public async Task<RefreshRun> RunAsync(RefreshRun run)
    {
        (await Execute.HandleAsync(new ExecuteRefreshCommand(run.Id), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        return run;
    }
}
