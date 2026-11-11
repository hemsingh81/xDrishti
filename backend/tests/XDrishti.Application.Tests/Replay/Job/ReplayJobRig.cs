using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Job;
using XDrishti.Application.Tests.SimAll;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Tests.Replay.Job;

/// <summary>The pieces of the replay job over an invented market: the fake store, the yield inputs of the simulate-everything job, and runners that each stand for one process (a fresh rig, so nothing in memory survives a "kill").</summary>
internal sealed class ReplayJobRig(ReplayWorld world)
{
    // 2026-11-10 is a Tuesday; 14:30 UTC is 20:00 IST, 05:00 UTC is 10:30 IST
    public static readonly DateTimeOffset Evening = new(2026, 11, 10, 14, 30, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset MarketMorning = new(2026, 11, 10, 5, 0, 0, TimeSpan.Zero);

    public ReplayWorld World { get; } = world;

    public FakeReplayStore Store { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 10, 1, 0, 0, TimeSpan.Zero)); // 06:30 IST, outside the market window; a test moves it forward

    public FakeDisk Disk { get; } = new();

    public FakeGate Gate { get; } = new();

    public FakeSimAllStore SimAll { get; } = new();

    public FakeLedger Ledger { get; } = new();

    public Func<IReplayFacts, IReplayCandidates>? Candidates { get; set; }

    /// <summary>A runner that stands for a process: its own facts, driver and book.</summary>
    public ReplayRunner Runner(SystemReplayOptions? options = null)
    {
        var rig = ReplayRig.Create(World, candidates: Candidates);
        var calendar = Substitute.For<ITradingCalendar>();
        calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        var scopes = new FakeScopes(
            (typeof(IReplayStore), Store), (typeof(ReplayDriver), rig.Driver), (typeof(IFreeDiskProbe), Disk), (typeof(ISimAllBackfillGate), Gate), (typeof(ISimAllStore), SimAll), (typeof(ITradingCalendar), calendar));
        return new ReplayRunner(scopes, options ?? new SystemReplayOptions(), Time, NullLogger<ReplayRunner>.Instance);
    }

    /// <summary>Starts runs through the real start handler (the hashes, the ledger rows, the busy rule).</summary>
    public async Task<Result<StartedReplay>> StartAsync(params ReplayParams[] runs)
    {
        var rig = ReplayRig.Create(World, candidates: Candidates);
        var handler = new StartReplayHandler(rig.Driver, rig.Facts, rig.Facts, Store, Ledger, Time, NullLogger<StartReplayHandler>.Instance);
        return await handler.HandleAsync(new StartReplayCommand(runs), TestContext.Current.CancellationToken);
    }

    /// <summary>The same invented market as the session tests: AAA is taken on the first session, filled on the second and leaves on the sixth.</summary>
    public static ReplayWorld SwingWorld()
    {
        var world = new ReplayWorld().WithDefaults("AAA", "BBB");
        world.Fill = new(MaxEntriesPerDay: 1, MaxOpenPositions: 1, MaxTotalOpenRiskPct: 3.0, MaxPerSector: 10, MaxPairCorrelation: 1.0, MaxIntraday: 10, MaxSwing: 10, MaxNetLongRiskPct: 50, MaxNetShortRiskPct: 50, MaxPerTradeRiskPct: 2.0);
        var d1 = new DateOnly(2026, 10, 5);
        var d2 = new DateOnly(2026, 10, 6);
        var d5 = new DateOnly(2026, 10, 9);
        world.Series("AAA", d => d < d1 ? 99.9 : d <= d5 ? 100.0 : 105.0);
        world.Series("BBB", d => d <= d2 ? 100.0 : 100.0 + ((d.DayNumber - d2.DayNumber) * 0.1));
        return world;
    }
}

internal static class ReplayRunnerTestExtensions
{
    /// <summary>One tick on a fresh look at the queue: true when it found nothing to claim.</summary>
    public static async Task<bool> RunOnceClaimsNothing(this ReplayRunner runner)
    {
        await runner.TickAsync(TestContext.Current.CancellationToken);
        return true;
    }
}
