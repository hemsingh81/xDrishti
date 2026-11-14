#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Sim;
using XDrishti.Application.Tests.SimAll;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Tests.Replay.Sim;

/// <summary>The pieces of the day-by-day simulation over an invented market: the fake store, the yield inputs, the real handlers, and runners that each stand for one process. Every scope gets a fresh replay rig (facts, driver), as the host's scopes do.</summary>
internal sealed class SimDaysRig(ReplayWorld world)
{
    public ReplayWorld World { get; } = world;

    public FakeSimDaysStore Store { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 10, 14, 30, 0, TimeSpan.Zero)); // 20:00 IST, outside the market window

    public FakeDisk Disk { get; } = new();

    public FakeGate Gate { get; } = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SimDaysOptions Options { get; } = new();

    public ITradingCalendar Calendar { get; } = Calendars();

    private static ITradingCalendar Calendars()
    {
        var calendar = Substitute.For<ITradingCalendar>();
        calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        return calendar;
    }

    /// <summary>Stores a run over the first <paramref name="sessions"/> sessions of the world, as the start handler would (the parameters, the one settings snapshot and the versions of a fresh rig).</summary>
    public async Task<Guid> StartAsync(int sessions, ReplayMode mode = ReplayMode.Frozen)
    {
        var rig = ReplayRig.Create(World);
        var p = ReplayRig.Params(sessions, mode: mode);
        var prepared = await rig.Driver.PrepareAsync(p, null, null, Ct);
        prepared.IsSuccess.ShouldBeTrue(prepared.IsFailure ? prepared.Error.Message : string.Empty);
        var versions = await rig.Facts.VersionsAsync(p, Ct);
        var learning = await rig.Facts.LearningSourcesAsync(p, Ct);
        var id = Guid.CreateVersion7();
        (await Store.InsertAsync(new NewSimDaysRun(id, new string('a', 64), ReplayJson.Canonical(p), ReplayJson.Canonical(prepared.Value.Run.Settings), ReplayJson.Canonical(versions), learning, p.Capital, mode == ReplayMode.Learning ? "learning" : "frozen", p.From, p.To, p.To, prepared.Value.Sessions[0], null, null, Time.GetUtcNow()), Ct)).ShouldBeTrue();
        return id;
    }

    public SettleSimDayHandler Settle() => new(Store, Calendar, Options, Time);

    public AdvanceSimDayHandler Advance() => new(Store, Calendar, Options, Time, NullLogger<AdvanceSimDayHandler>.Instance);

    /// <summary>A runner that stands for a process: nothing it keeps in memory survives a "kill".</summary>
    public SimDaysRunner Runner() => new(new Scopes(this), Options, new SystemReplayOptions(), Time, NullLogger<SimDaysRunner>.Instance);

    /// <summary>The SR-01 records of the same inputs, session by session (the golden side).</summary>
    public async Task<IReadOnlyList<ReplaySessionRecord>> Sr01Async(int sessions, ReplayMode mode = ReplayMode.Frozen)
    {
        var run = await ReplayRig.Create(World).Driver.RunAsync(ReplayRig.Params(sessions, mode: mode), Ct);
        run.IsSuccess.ShouldBeTrue(run.IsFailure ? run.Error.Message : string.Empty);
        return run.Value.Sessions;
    }

    private sealed class Scopes(SimDaysRig owner) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(owner, ReplayRig.Create(owner.World));
    }

    private sealed class Scope(SimDaysRig owner, ReplayRig rig) : IServiceScope, IServiceProvider
    {
        public IServiceProvider ServiceProvider => this;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(ISimDaysStore) ? owner.Store
            : serviceType == typeof(ReplayDriver) ? rig.Driver
            : serviceType == typeof(IReplayFacts) ? rig.Facts
            : serviceType == typeof(IFreeDiskProbe) ? owner.Disk
            : serviceType == typeof(ISimAllBackfillGate) ? owner.Gate
            : serviceType == typeof(ISimAllStore) ? new FakeSimAllStore()
            : serviceType == typeof(ITradingCalendar) ? owner.Calendar
            : serviceType == typeof(ICommandHandler<SettleSimDayCommand, SimDaysView>) ? owner.Settle()
            : serviceType == typeof(ICommandHandler<AdvanceSimDayCommand, SimDaysView>) ? owner.Advance()
            : null;

        public void Dispose()
        {
        }
    }
}
