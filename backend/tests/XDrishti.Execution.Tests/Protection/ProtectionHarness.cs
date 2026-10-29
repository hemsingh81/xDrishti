using Microsoft.Extensions.Time.Testing;
using XDrishti.Domain.Common;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Protection;

namespace XDrishti.Execution.Tests.Protection;

/// <summary>An IExecEventStore over the in-memory fold, for the services that move an intent or write an alarm event.</summary>
internal sealed class MemoryEvents : IExecEventStore
{
    private readonly InMemoryExecEventStore _inner = new();

    public Task<Result> StartAsync(ExecIntent intent, DateOnly sessionDate, ExecEnvironment environment, string createdBy, CancellationToken cancellationToken) => Task.FromResult(_inner.Start(intent));

    public Task<Result> AppendAsync(long intentId, ExecEvent e, CancellationToken cancellationToken) => Task.FromResult(_inner.Append(intentId, e));

    public Task<ExecIntentState?> StateAsync(long intentId, CancellationToken cancellationToken) =>
        Task.FromResult(_inner.Live(intentId) is { } live ? new ExecIntentState(live.State, live.Seq, live.FilledQty) : null);

    public Task<IReadOnlyList<ExecEvent>> EventsAsync(long intentId, CancellationToken cancellationToken) => Task.FromResult(_inner.Events(intentId));

    public Task<Result<ExecIntent>> RebuildAsync(long intentId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ExecState? State(long id) => _inner.Live(id)?.State;

    public IReadOnlyList<ExecEvent> Events(long id) => _inner.Events(id);

    /// <summary>Starts intent 361 (BUY 10 of 1594, entry 1,500, stop 1,485, target 1,530) and walks it to <c>filled</c>.</summary>
    public void StartFilled(Guid account, ExecStructure structure = ExecStructure.PlainStop, int fills = 10)
    {
        var intent = ExecIntent.Create(361, Guid.NewGuid(), account, 1, ExecSide.Buy, "RELIANCE", "1594", 10, 1500m, 1485m, 1530m, structure).Value;
        _inner.Start(intent).IsSuccess.ShouldBeTrue();
        var seq = 0;
        var at = DateTimeOffset.UnixEpoch;
        foreach (var (from, to) in new[] { (ExecState.Planned, ExecState.Armed), (ExecState.Armed, ExecState.Checking), (ExecState.Checking, ExecState.Submitting), (ExecState.Submitting, ExecState.Open) })
        {
            _inner.Append(361, ExecEvent.Move(++seq, from, to, ExecEventSource.Engine, "system:test", at)).IsSuccess.ShouldBeTrue();
        }

        _inner.Append(361, ExecEvent.Fill(++seq, fills, ExecEventSource.BrokerPoll, "system:test", at)).IsSuccess.ShouldBeTrue();
        _inner.Append(361, ExecEvent.Move(++seq, ExecState.Open, ExecState.Filled, ExecEventSource.Engine, "system:test", at)).IsSuccess.ShouldBeTrue();
    }
}

/// <summary>The snapshot source over a FakeBroker: the order book and the net position are the broker's, the rest is set by the test. <see cref="BookAge"/> makes the view old (blind).</summary>
internal sealed class FakeSource(FakeBroker broker, FakeTimeProvider clock, Guid account) : IProtectionSource
{
    public ExecState State { get; set; } = ExecState.Filled;

    public decimal? Last { get; set; } = 1495m;

    public DateTimeOffset? RaisedAt { get; set; }

    public TimeSpan BookAge { get; set; } = TimeSpan.Zero;

    public HaltLevel? Halt { get; set; }

    public int? NetOverride { get; set; }

    public int? BrokerNet { get; set; }

    public DateTimeOffset? BrokerPositionAt { get; set; }

    public decimal IntentStop { get; set; } = 1485m;

    public IReadOnlyList<BrokerOrderView>? BookOverride { get; set; }

    public bool Unknown { get; set; }

    /// <summary>Attributes every working entry in the book to this intent (default 361).</summary>
    public long EntryIntentOverride { get; set; } = 361;

    public async Task<ProtectionSnapshot?> ReadAsync(CancellationToken cancellationToken)
    {
        if (Unknown)
        {
            return null;
        }

        var book = BookOverride ?? (await broker.GetOrderBookAsync(cancellationToken)).Value!;
        var net = NetOverride ?? (await broker.GetPositionsAsync(cancellationToken)).Value!.FirstOrDefault(p => p.SecurityId == "1594")?.NetQty ?? 0;
        var positions = net == 0 && State is ExecState.Closed ? [] : new List<ProtectedPosition> { new(account, 361, "1594", State, net, IntentStop, 1500m, Last, RaisedAt, BrokerNet, BrokerPositionAt) };
        var entries = book.Where(o => o.Side == ExecSide.Buy && o.Status is "PENDING" or "PART_TRADED" && o.ClientOrderId is { } c && ClientOrderId.TryParse(c, out var id) && id.Kind == LegKind.Entry)
            .Select(o => new WorkingEntry(account, EntryIntentOverride, o.ClientOrderId!)).ToList();
        return new ProtectionSnapshot(clock.GetUtcNow() - BookAge, book, positions, entries, Halt, new DateOnly(2026, 10, 26));
    }
}

/// <summary>The pre-send step for these tests: legs that reduce risk go to the exit policy; an entry is refused (the entry checklist is P12-03).</summary>
internal sealed class ExitOnlyPreSend(IExitSendPolicy exits) : IPreSendCheck
{
    public Task<GateDecision> CheckAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken) =>
        leg.Kind == LegKind.Entry ? Task.FromResult(new GateDecision(false, "entry_not_in_this_test")) : exits.CheckAsync(accountId, intentId, leg, cancellationToken);
}

internal sealed class NullJournal : ISendJournal
{
    public Task BeginAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DroppedAsync(BrokerLeg leg, string reason, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task OutcomeAsync(BrokerLeg leg, BrokerAck ack, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FixedMode(ModeAnswer? answer) : IModeSource
{
    public Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(answer);
}

internal sealed class ClearHalts : IHaltSource
{
    public Task<HaltReading> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(HaltReading.Clear);
}

/// <summary>Everything of the protection logic on one FakeBroker and a fake clock. The clock starts at 09:30 IST on 2026-10-26.</summary>
internal sealed class Rig
{
    public static readonly Guid Account = Guid.NewGuid();

    public Rig(ProtectionSettings? settings = null, ModeAnswer? mode = null, ExecEnvironment environment = ExecEnvironment.Sandbox)
    {
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 4, 0, 0, TimeSpan.Zero));
        Broker = new FakeBroker(Clock);
        Source = new FakeSource(Broker, Clock, Account);
        Settings = settings ?? ProtectionSettings.Default;
        Events.StartFilled(Account);
        var modes = new FixedMode(mode ?? new ModeAnswer(ExecMode.SemiAuto, true));
        Report = new ProtectionReporter(Alarms, Halts, Events, Clock);
        Guard = new ProtectiveGuard(modes, environment);
        Sender = new LegSender(Broker, modes, new ClearHalts(), new ExitOnlyPreSend(new ProtectionExitPolicy(Source, () => Settings, Clock)), new NullJournal(), new ExecRateLimiter(Clock, 5, 100, 1000), environment, Clock, TimeSpan.FromSeconds(5));
        Dog = new ProtectionWatchdog(Source, Sender, Broker, Guard, Report, () => Settings, Clock);
        Stops = new StopManager(Source, Broker, Guard, Events, Report, () => Settings, Clock);
        SquareOff = new SquareOffService(Source, Sender, Broker, Guard, Report, () => Settings, Clock);
    }

    public FakeTimeProvider Clock { get; }

    public FakeBroker Broker { get; }

    public FakeSource Source { get; }

    public ProtectionSettings Settings { get; set; }

    public MemoryEvents Events { get; } = new();

    public InMemoryAlarmStore Alarms { get; } = new();

    public RecordingHaltRequests Halts { get; } = new();

    public ProtectionReporter Report { get; }

    public ProtectiveGuard Guard { get; }

    public LegSender Sender { get; }

    public ProtectionWatchdog Dog { get; }

    public StopManager Stops { get; }

    public SquareOffService SquareOff { get; }

    public static string Cid(LegKind kind, int attempt, long intent = 361) => ClientOrderId.Create(new DateOnly(2026, 10, 26), intent, kind, attempt).Value.ToString();

    public static BrokerLeg Entry(int qty = 10, decimal price = 1500m) => new(Cid(LegKind.Entry, 1), LegKind.Entry, ExecSide.Buy, "1594", qty, price);

    public static BrokerLeg Stop(int qty = 10, decimal trigger = 1485m, int attempt = 1) => new(Cid(LegKind.Stop, attempt), LegKind.Stop, ExecSide.Sell, "1594", qty, StopRules.StopLimit(trigger, 0.5m), trigger);

    /// <summary>Entry placed and filled at 1,500 (whole, or in parts when the broker's MaxFillPerStep is set).</summary>
    public async Task FilledAsync(int qty = 10)
    {
        await Broker.PlaceAsync(Entry(qty), CancellationToken.None);
        Broker.SetPrice("1594", 1500m);
        Source.RaisedAt = Clock.GetUtcNow();
    }

    public async Task StopAsync(int qty = 10, decimal trigger = 1485m, int attempt = 1) => await Broker.PlaceAsync(Stop(qty, trigger, attempt), CancellationToken.None);

    public async Task TickAsync(int seconds = 1)
    {
        for (var i = 0; i < seconds; i++)
        {
            Clock.Advance(TimeSpan.FromSeconds(1));
            await Dog.TickAsync(CancellationToken.None);
        }
    }

    public int Count(string code) => Alarms.Raised.Count(a => a.Code == code);

    public bool IsOpen(string code) => Alarms.Open.Any(a => a.Code == code);

    public int OrdersOf(LegKind kind) => Broker.Legs.Count(l => l.Kind == kind);
}
