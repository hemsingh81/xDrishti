using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Intents;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Intents;
using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Tests.Intents;

/// <summary>India time for the intent tests. 2026-10-27 is a Tuesday, the session of the plan; the plan was approved the evening before.</summary>
internal static class Ist
{
    public static readonly DateOnly Session = new(2026, 10, 27);

    public static DateTimeOffset At(int day, int hour, int minute, int second = 0) => new(2026, 10, day, hour, minute, second, TimeSpan.FromMinutes(330));
}

/// <summary>
/// One fake for every port of the intent creator, so a test reads like a scene: the plan tickets, the account's mode, the halt record, the newest readiness check, the live price and the settings are plain fields. The intents and their
/// events live in P12-01's in-memory store, so every move is judged by the real lifecycle.
/// </summary>
internal sealed class IntentScene : IIntentPlanSource, IIntentRecord, IIntentQuotes, IIntentSettings, IModeSource, IHaltSource, IReadinessReads, IExecEventStore
{
    public static readonly Guid Account = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly InMemoryExecEventStore _store = new();
    private readonly List<(IntentRow Row, long PlanId, long? ApprovalId, int Quantity, decimal Stop, decimal Target, string SecurityId)> _intents = [];

    public FakeTimeProvider Clock { get; } = new(Ist.At(26, 20, 0));

    public List<PlanEntryCandidate> Candidates { get; } = [Ticket()];

    public bool PlanUnreadable { get; set; }

    public bool IntentsUnreadable { get; set; }

    public Dictionary<Guid, ModeAnswer?> Modes { get; } = new() { [Account] = new ModeAnswer(ExecMode.SemiAuto, true) };

    public HaltReading Halt { get; set; } = HaltReading.Clear;

    public ReadinessCheckRecord? Readiness { get; set; } = new(1, Ist.At(27, 9, 55), Account, Ist.Session, ReadinessState.Ready, []);

    public LivePrice? Price { get; set; }

    public IntentSettings? Settings { get; set; } = new(new TimeOnly(9, 20), new TimeOnly(15, 0), 2.0m, CanaryPending: false, TimeSpan.FromSeconds(5));

    public long? TrailId { get; set; } = 41;

    public IReadOnlyList<IntentRow> Intents => [.. _intents.Select(i => Current(i.Row))];

    public int EventCount => _intents.Sum(i => _store.Events(i.Row.Id).Count);

    public static PlanEntryCandidate Ticket(long item = 501, string side = "BUY", decimal? entry = 412.35m, string? security = "3426", TicketState state = TicketState.Approved, int quantity = 120, Guid? account = null) =>
        new(77, item, 9001, Ist.Session, account ?? Account, quantity, side, "NSE:TATAPOWER", security, entry, entry is null ? null : 405.10m, entry is null ? null : 425.80m, state);

    public IntentCreator Creator(ExecEnvironment environment = ExecEnvironment.Sandbox) => new(this, this, this, this, this, this, this, this, environment, Clock);

    public void ItemState(TicketState state) => Candidates[0] = Candidates[0] with { ItemState = state };

    public void Now(int day, int hour, int minute, int second = 0) => Clock.SetUtcNow(Ist.At(day, hour, minute, second));

    public void PriceNow(decimal last) => Price = new(last, Clock.GetUtcNow());

    public IntentRow Single() => Intents.ShouldHaveSingleItem();

    public string? LastReason(IntentRow intent) =>
        _store.Events(intent.Id) is { Count: > 0 } events ? JsonDocument.Parse(events[^1].Payload).RootElement.GetProperty("reason").GetString() : null;

    public (long PlanId, long? ApprovalId, int Quantity, string SecurityId) Stored(IntentRow intent)
    {
        var i = _intents.Single(x => x.Row.Id == intent.Id);
        return (i.PlanId, i.ApprovalId, i.Quantity, i.SecurityId);
    }

    public Task<IReadOnlyList<PlanEntryCandidate>?> CandidatesAsync(DateOnly session, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlanEntryCandidate>?>(PlanUnreadable ? null : [.. Candidates.Where(c => c.Session >= session && c.Session <= session.AddDays(7))]);

    public Task<IntentRow?> CreateAsync(IntentDraft draft, CancellationToken cancellationToken)
    {
        if (_intents.Any(i => i.Row.AccountId == draft.AccountId && i.Row.PlanItemId == draft.PlanItemId))
        {
            return Task.FromResult<IntentRow?>(null);
        }

        var id = _intents.Count + 1L;
        var intent = ExecIntent.Create(id, Guid.NewGuid(), draft.AccountId, draft.PlanItemId, ExecSide.Buy, draft.Symbol, draft.SecurityId, draft.Quantity, draft.Entry, draft.Stop, draft.Target, ExecStructure.Super).Value;
        _store.Start(intent).IsSuccess.ShouldBeTrue();
        var row = new IntentRow(id, intent.PublicId, draft.AccountId, draft.PlanItemId, draft.Session, draft.Symbol, ExecSide.Buy, draft.Entry, ExecState.Planned, 0, TrailId);
        _intents.Add((row, draft.PlanId, draft.ApprovalId, draft.Quantity, draft.Stop, draft.Target, draft.SecurityId));
        return Task.FromResult<IntentRow?>(row);
    }

    public Task<IReadOnlyList<IntentRow>> SinceAsync(DateOnly fromSession, CancellationToken cancellationToken) =>
        IntentsUnreadable ? throw new InvalidOperationException("database down") : Task.FromResult<IReadOnlyList<IntentRow>>([.. Intents.Where(i => i.Session >= fromSession)]);

    public Task<LivePrice?> LastAsync(string symbol, CancellationToken cancellationToken) => Task.FromResult(Price);

    IntentSettings? IIntentSettings.Current() => Settings;

    public Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(Modes.GetValueOrDefault(accountId));

    public Task<HaltReading> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Halt);

    public Task<ReadinessCheckRecord?> LastCheckAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(Readiness?.AccountId == accountId ? Readiness : null);

    public Task<Result> StartAsync(ExecIntent intent, DateOnly sessionDate, ExecEnvironment environment, string createdBy, CancellationToken cancellationToken) => Task.FromResult(_store.Start(intent));

    public Task<Result> AppendAsync(long intentId, ExecEvent e, CancellationToken cancellationToken) => Task.FromResult(_store.Append(intentId, e));

    public Task<ExecIntentState?> StateAsync(long intentId, CancellationToken cancellationToken) =>
        Task.FromResult(_store.Live(intentId) is { } live ? new ExecIntentState(live.State, live.Seq, live.FilledQty) : null);

    public Task<IReadOnlyList<ExecEvent>> EventsAsync(long intentId, CancellationToken cancellationToken) => Task.FromResult(_store.Events(intentId));

    public Task<Result<ExecIntent>> RebuildAsync(long intentId, CancellationToken cancellationToken) =>
        Task.FromResult(_store.Live(intentId) is { } live ? Result.Success(live) : Result.Failure<ExecIntent>(Error.NotFound("execution.intent_unknown", "No such intent.")));

    private IntentRow Current(IntentRow row)
    {
        var live = _store.Live(row.Id)!;
        return row with { State = live.State, Seq = live.Seq };
    }
}
