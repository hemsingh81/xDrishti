#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Ai;
using XDrishti.Application.Ai.Drafting;
using XDrishti.Application.Lab;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Ai;

/// <summary>The ports of the drafting use cases as fakes with counters (spec P9-07 test plan): a refusal, a switch that is off or a model that is down must not store, create or run anything.</summary>
internal sealed class DraftRig
{
    public const string Owner = "owner:owner@example.invalid";

    public const string Idea = "Buy when the 15-min close is above VWAP with RSI(14) above 55 and exit at 15:15 with a target of 1:2.";

    public const string Yaml = "id: DRAFT_X\ntrigger: close > vwap and rsi(14) > 55\nsquare_off: \"15:15\"\nrr: 2\n";

    public const string Tree = """{"id":"DRAFT_X","params":{"rr":"2"},"exits":[{"policy":"FIXED_RR","rr":"2"}]}""";

    public AssistantSettings Settings { get; set; } = new(1, true, false, 6, AllowDraftConfirm: true);

    public DraftOptions Options { get; } = new() { Enabled = true, MaxPerDay = 3 };

    public LlmState Llm { get; set; } = LlmState.Ready;

    public TurnGate Gate { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.FromHours(5.5)));

    public FakeDraftStore Store { get; } = new();

    public List<ModelCallRecord> Audit { get; } = [];

    public Queue<Func<DraftModelAnswer>> Answers { get; } = new();

    public int ModelCalls { get; private set; }

    public bool Hang { get; set; }

    public List<DraftModelRequest> Requests { get; } = [];

    public Func<StrategyCheck> Check { get; set; } = () => new StrategyCheck(Description(), null, []);

    public IStrategyChecker Checker => new FakeChecker(this);

    public IStrategyStore Strategies { get; } = Substitute.For<IStrategyStore>();

    public ICommandHandler<CreateStrategyCommand, CreatedStrategy> Create { get; } = Substitute.For<ICommandHandler<CreateStrategyCommand, CreatedStrategy>>();

    public ICommandHandler<StartLabRunCommand, StartedLabRun> Start { get; } = Substitute.For<ICommandHandler<StartLabRunCommand, StartedLabRun>>();

    public ILabRunStore Runs { get; } = Substitute.For<ILabRunStore>();

    public static StrategyDescription Description(string id = "DRAFT_X") => new(id, "intraday", new string('a', 64), "{}", [Timeframe.M15], [Side.Buy], []);

    public static string Json(string? yaml = Yaml, bool decline = false, string understood = "\"close above VWAP\"", string assumptions = "", string notExpressed = "") =>
        "{\"decline\":" + (decline ? "true" : "false") + ",\"unsupported\":" + (decline ? "[\"news\"]" : "[]") + ",\"understood\":[" + understood + "],\"assumptions\":[" + assumptions + "],\"not_expressed\":[" + notExpressed + "],\"requested_test\":{\"from\":null,\"to\":null,\"baskets\":[]},\"yaml\":"
        + (yaml is null ? "null" : System.Text.Json.JsonSerializer.Serialize(yaml)) + "}";

    public static DraftModelAnswer Reply(string text, string finish = "stop") => new(text, 100, 50, finish, 10);

    public DraftRig()
    {
        Strategies.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<StoredStrategy?>(null));
        Create.HandleAsync(Arg.Any<CreateStrategyCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new CreatedStrategy("DRAFT_X", 1, new string('a', 64), null, true))));
    }

    public SubmitDraftHandler Submit() => new(Checker, new FakeReader());

    public DraftStrategyHandler Job()
    {
        var health = Substitute.For<ILlmHealth>();
        health.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(new LlmStatus(Llm, "m", "m.gguf", new string('b', 64), null, DateTimeOffset.UnixEpoch, "x")));
        var audit = Substitute.For<IModelCallAudit>();
        audit.RecordAsync(Arg.Any<ModelCallRecord>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Audit.Add(call.Arg<ModelCallRecord>());
            return ValueTask.CompletedTask;
        });
        return new DraftStrategyHandler(Store, new FakeSheet(), new FakeDrafter(this), Submit(), health, audit, Options, NullLogger<DraftStrategyHandler>.Instance);
    }

    /// <summary>Jobs whose scope hands out <paramref name="job"/> as the handler of the drafting job.</summary>
    public static DraftJobs JobsRunning(ICommandHandler<DraftStrategyCommand, DraftView>? job = null)
    {
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ICommandHandler<DraftStrategyCommand, DraftView>)).Returns(job);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        return new DraftJobs(factory);
    }

    public StartDraftHandler Starter(DraftJobs? jobs = null) => new(SettingsStore(), Store, jobs ?? JobsRunning(), Gate, Options, Time, NullLogger<StartDraftHandler>.Instance);

    public ConfirmDraftHandler Confirmer() => new(Store, SettingsStore(), Checker, Strategies, Create, Start, Runs, NullLogger<ConfirmDraftHandler>.Instance);

    public DiscardDraftHandler Discarder() => new(Store, Runs, NullLogger<DiscardDraftHandler>.Instance);

    public IAssistantSettingsStore SettingsStore()
    {
        var store = Substitute.For<IAssistantSettingsStore>();
        store.CurrentAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Settings));
        return store;
    }

    public StoredDraft Seed(string status = "valid", string? yaml = Yaml, string via = "job")
    {
        var id = Store.NextId++;
        var draft = new StoredDraft(id, Idea, status, null, via, null, yaml, null, [], [], [], "m", "draft.v1", new string('c', 64), Owner, DateTimeOffset.UnixEpoch);
        Store.Drafts[id] = draft;
        return draft;
    }

    private sealed class FakeChecker(DraftRig rig) : IStrategyChecker
    {
        public StrategyCheck Check(string text) => rig.Check();

        public string? Retarget(string text, string newId) => text;

        public string SchemaJson() => "{}";
    }

    private sealed class FakeReader : IStrategyTextReader
    {
        public string? ToJson(string text) => text.Contains("unreadable", StringComparison.Ordinal) ? null : Tree;
    }

    private sealed class FakeSheet : IDraftSheetSource
    {
        public string Sheet => "SHEET";

        public string Hash => new('d', 64);
    }

    private sealed class FakeDrafter(DraftRig rig) : IStrategyDrafter
    {
        public async ValueTask<DraftModelAnswer> DraftAsync(DraftModelRequest request, CancellationToken cancellationToken)
        {
            rig.ModelCalls++;
            if (rig.Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            rig.Requests.Add(request);
            if (rig.Answers.Count == 0)
            {
                throw new ModelUnavailableException("down");
            }

            return rig.Answers.Dequeue()();
        }
    }

    /// <summary>The drafting job as a recording fake (the command type is internal, so NSubstitute cannot proxy it).</summary>
    public sealed class RecordingJob : ICommandHandler<DraftStrategyCommand, DraftView>
    {
        public List<DraftStrategyCommand> Seen { get; } = [];

        public ValueTask<Result<DraftView>> HandleAsync(DraftStrategyCommand command, CancellationToken cancellationToken)
        {
            Seen.Add(command);
            return ValueTask.FromResult(Result.Success(DraftView.InProgress(command.Id, command.Idea)));
        }
    }

    public sealed class FakeDraftStore : IDraftStore, IDisposable
    {
        public void Dispose() => _lock.Dispose();

        public Dictionary<long, StoredDraft> Drafts { get; } = [];

        public Dictionary<long, StoredDraftDecision> Decisions { get; } = [];

        public long NextId { get; set; } = 1;

        public int TodayCount { get; set; }

        public ValueTask<long> ReserveIdAsync(CancellationToken cancellationToken) => ValueTask.FromResult(NextId++);

        public ValueTask InsertAsync(NewDraft d, CancellationToken cancellationToken)
        {
            Drafts[d.Id] = new StoredDraft(d.Id, d.Idea, d.Status, d.FailReason, d.Via, d.BasketId, d.Yaml, d.Answer, d.Attempts, d.Problems, d.Findings, d.ModelId, d.PromptVersion, d.SheetHash, d.RequestedBy, DateTimeOffset.UnixEpoch);
            return ValueTask.CompletedTask;
        }

        public ValueTask<StoredDraft?> GetAsync(long id, CancellationToken cancellationToken) => ValueTask.FromResult(Drafts.GetValueOrDefault(id));

        public ValueTask<IReadOnlyList<StoredDraft>> ListAsync(int limit, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<StoredDraft>>([.. Drafts.Values.OrderByDescending(d => d.Id).Take(limit)]);

        public ValueTask<int> CountBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) => ValueTask.FromResult(TodayCount);

        private readonly SemaphoreSlim _lock = new(1, 1);

        public async ValueTask<IAsyncDisposable> LockConfirmAsync(CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            return new Release(_lock);
        }

        private sealed class Release(SemaphoreSlim slot) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                slot.Release();
                return ValueTask.CompletedTask;
            }
        }

        public ValueTask<StoredDraftDecision?> DecisionAsync(long draftId, CancellationToken cancellationToken) => ValueTask.FromResult(Decisions.GetValueOrDefault(draftId));

        public ValueTask<(StoredDraftDecision Decision, bool Created)> InsertDecisionAsync(NewDraftDecision d, CancellationToken cancellationToken)
        {
            if (Decisions.TryGetValue(d.DraftId, out var existing))
            {
                return ValueTask.FromResult((existing, false));
            }

            var row = new StoredDraftDecision(d.DraftId, d.Decision, d.StrategyId, d.StrategyVersion, d.LabRunId, d.Edited, d.Auto, d.Actor, DateTimeOffset.UnixEpoch, d.RunError);
            Decisions[d.DraftId] = row;
            return ValueTask.FromResult((row, true));
        }
    }
}
