#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Ai;
using XDrishti.Application.Ai.Proposals;
using XDrishti.Application.Learning;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Ai;

/// <summary>The ports of the proposal use cases as fakes with counters (spec P9-08 test plan): a refusal, a switch that is off or a model that is down must not store, write or change anything.</summary>
internal sealed class ProposalRig : IDisposable
{
    public const string Owner = "owner:owner@example.invalid";

    public const string Cell = "ORB|15m|BUY|intraday|FIXED_RR|*";

    public static readonly string RunKey = new('a', 64);

    public static readonly Guid BasketId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid EvbId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly Guid TcsId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public AssistantSettings Settings { get; set; } = new(5, true, false, 6, AllowProposalAccept: true, SnapshotHash: new string('e', 64));

    public ProposalOptions Options { get; } = new() { Enabled = true };

    public LlmState Llm { get; set; } = LlmState.Ready;

    public TurnGate Gate { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.FromHours(5.5)));

    public FakeFacts Facts { get; } = new();

    public FakeProposalStore Store { get; }

    public List<ModelCallRecord> Audit { get; } = [];

    public Queue<Func<ProposalModelAnswer>> Answers { get; } = new();

    public List<ProposalModelRequest> Requests { get; } = [];

    public FakeAuto Auto { get; } = new();

    public ICommandHandler<ProposeHypothesisCommand, HypothesisMoveOutcome> Hypotheses { get; } = Substitute.For<ICommandHandler<ProposeHypothesisCommand, HypothesisMoveOutcome>>();

    public ICommandHandler<AddBasketMembersCommand, AddMembersResult> Add { get; } = Substitute.For<ICommandHandler<AddBasketMembersCommand, AddMembersResult>>();

    public ICommandHandler<RemoveBasketMembersCommand, RemoveMembersResult> Remove { get; } = Substitute.For<ICommandHandler<RemoveBasketMembersCommand, RemoveMembersResult>>();

    public void Dispose()
    {
        Store.Dispose();
        Gate.Dispose();
    }

    public ProposalRig()
    {
        Store = new FakeProposalStore(Time);
        Hypotheses.HandleAsync(Arg.Any<ProposeHypothesisCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new HypothesisMoveOutcome(77, "proposed", "Proposed."))));
        Add.HandleAsync(Arg.Any<AddBasketMembersCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new AddMembersResult(1, 1, 0, 0, 4))));
        Remove.HandleAsync(Arg.Any<RemoveBasketMembersCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new RemoveMembersResult(1, 1, 0, 2))));
        Facts.Seed();
    }

    public static string Hypothesis(string candidate = "2.5", string reason = "The cell has 120 trades and expectancy 0.21.", string? cell = null, string knobKind = "Parameter", string baseline = "2", string? run = null, IEnumerable<string>? refs = null)
    {
        var c = cell ?? Cell;
        var r = run ?? RunKey;
        var list = refs ?? [$"cell:{c}", $"run:{r}"];
        return "{\"kind\":\"hypothesis\",\"subject\":\"model subject\",\"payload\":{\"baselineRunKey\":\"" + r + "\",\"cellKey\":\"" + c + "\",\"knob\":{\"name\":\"rr\",\"kind\":\"" + knobKind + "\",\"baselineValue\":\"" + baseline + "\",\"candidateValue\":\"" + candidate + "\"}},\"reason\":\"" + reason + "\",\"evidenceRefs\":[" + string.Join(',', list.Select(x => "\"" + x + "\"")) + "]}";
    }

    public static string BasketItem(string kind = "basket_add", string symbol = "EVB", Guid? basket = null, string reason = "EVB has 40 trades and a lower bound of 0.31.", IEnumerable<string>? refs = null)
    {
        var list = refs ?? [$"instrument:{symbol}"];
        return "{\"kind\":\"" + kind + "\",\"subject\":\"x\",\"payload\":{\"basketId\":\"" + (basket ?? BasketId).ToString("D") + "\",\"symbol\":\"" + symbol + "\"},\"reason\":\"" + reason + "\",\"evidenceRefs\":[" + string.Join(',', list.Select(x => "\"" + x + "\"")) + "]}";
    }

    public static string Answer(params string[] items) => "{\"items\":[" + string.Join(',', items) + "]}";

    public static ProposalModelAnswer Reply(string text, string finish = "stop") => new(text, 100, 50, finish, 10);

    public ProposalRig Say(params string[] items)
    {
        Answers.Enqueue(() => Reply(Answer(items)));
        return this;
    }

    public IAssistantSettingsStore SettingsStore()
    {
        var store = Substitute.For<IAssistantSettingsStore>();
        store.CurrentAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Settings));
        return store;
    }

    public ProposalEvaluator Evaluator() => new(Facts, Store, Options, Time);

    public ProposalApplier Applier() => new(Hypotheses, Add, Remove, Facts);

    public GenerateProposalsHandler Job()
    {
        var health = Substitute.For<ILlmHealth>();
        health.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(new LlmStatus(Llm, "m", "m.gguf", new string('b', 64), null, DateTimeOffset.UnixEpoch, "x")));
        var audit = Substitute.For<IModelCallAudit>();
        audit.RecordAsync(Arg.Any<ModelCallRecord>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Audit.Add(call.Arg<ModelCallRecord>());
            return ValueTask.CompletedTask;
        });
        return new GenerateProposalsHandler(new ProposalPackBuilder(Facts, Options, Time), Evaluator(), new FakeModel(this), Store, Store, SettingsStore(), health, audit, Auto, Gate, Options, Time, NullLogger<GenerateProposalsHandler>.Instance);
    }

    public AcceptProposalHandler Accepter() => new(Store, Store, SettingsStore(), Evaluator(), Applier(), Time, NullLogger<AcceptProposalHandler>.Instance);

    public RejectProposalHandler Rejecter() => new(Store, Store, Time, NullLogger<RejectProposalHandler>.Instance);

    public AutoAcceptProposalsHandler AutoHandler() => new(Store, Store, SettingsStore(), Evaluator(), Applier(), Options, Time, NullLogger<AutoAcceptProposalsHandler>.Instance);

    /// <summary>Stores a proposal that passed the checks, through the real job, and returns its id.</summary>
    public async Task<long> StoreAsync(string item)
    {
        Answers.Clear();
        Say(item);
        var run = await Job().HandleAsync(new GenerateProposalsCommand(null, null, false, "system:proposals"), CancellationToken.None);
        run.IsSuccess.ShouldBeTrue(run.IsFailure ? run.Error.Message : string.Empty);
        run.Value.Stored.ShouldBe(1, string.Join(',', run.Value.Refused.Select(r => r.Code)));
        return Store.Proposals.Keys.Max();
    }

    private sealed class FakeModel(ProposalRig rig) : IProposalModel
    {
        public int Calls { get; private set; }

        public ValueTask<ProposalModelAnswer> ProposeAsync(ProposalModelRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            rig.ModelCalls++;
            rig.Requests.Add(request);
            if (rig.Answers.Count == 0)
            {
                throw new ModelUnavailableException("down");
            }

            return ValueTask.FromResult(rig.Answers.Dequeue()());
        }
    }

    public int ModelCalls { get; private set; }

    /// <summary>The auto option as a recording fake (the command type is internal, so NSubstitute cannot proxy it).</summary>
    public sealed class FakeAuto : ICommandHandler<AutoAcceptProposalsCommand, AutoAcceptOutcome>
    {
        public List<IReadOnlyList<long>> Asked { get; } = [];

        public ValueTask<Result<AutoAcceptOutcome>> HandleAsync(AutoAcceptProposalsCommand command, CancellationToken cancellationToken)
        {
            Asked.Add(command.Ids);
            return ValueTask.FromResult(Result.Success(new AutoAcceptOutcome(0, "off")));
        }
    }

    /// <summary>What the engine knows, as data a test can change.</summary>
    public sealed class FakeFacts : IProposalFacts
    {
        public List<CellFact> Cells { get; } = [];

        public Dictionary<string, RunFact> Runs { get; } = [];

        public List<OpenHypothesisFact> Open { get; } = [];

        public Dictionary<Guid, BasketFact> Baskets { get; } = [];

        public Dictionary<string, InstrumentFact> Instruments { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, InstrumentTradesFact> Trades { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool ReportsDown { get; set; }

        public bool Throw { get; set; }

        public void Seed()
        {
            Cells.Add(new CellFact(Cell, "ORB", 120, 0.21, 0.55, true, 30, "gross R (before charges)", new DateOnly(2026, 10, 20), RunKey));
            Runs[RunKey] = new RunFact(RunKey, "ORB", new RunDefinition("ORB", new Dictionary<string, double> { ["rr"] = 2.0, ["atr"] = 14.0 }, "{}", "buy={};sell={};", "{}"));
            Baskets[BasketId] = new BasketFact(BasketId, "EvalAlpha", false, false, ["TCS", "INFY"]);
            Instruments["EVB"] = new InstrumentFact(EvbId, "EVB", true, false);
            Instruments["TCS"] = new InstrumentFact(TcsId, "TCS", true, false);
            Trades["EVB"] = new InstrumentTradesFact("EVB", "value", 40, 0.31, new DateOnly(2025, 1, 1), new DateOnly(2026, 10, 1), [RunKey], "gross R (before charges)", ["Only closed trades are counted."]);
            Trades["TCS"] = new InstrumentTradesFact("TCS", "value", 30, 0.02, new DateOnly(2025, 1, 1), new DateOnly(2026, 10, 1), [RunKey], "gross R (before charges)", ["Only closed trades are counted."]);
        }

        public ValueTask<IReadOnlyList<CellFact>> CellsAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<CellFact>>([.. Cells]);

        public ValueTask<CellFact?> CellAsync(string cellKey, CancellationToken cancellationToken) => ValueTask.FromResult(Cells.FirstOrDefault(c => c.Key == cellKey));

        public ValueTask<RunFact?> RunAsync(string runKey, CancellationToken cancellationToken) => ValueTask.FromResult(Runs.GetValueOrDefault(runKey));

        public ValueTask<IReadOnlyList<OpenHypothesisFact>> OpenHypothesesAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<OpenHypothesisFact>>([.. Open]);

        public ValueTask<IReadOnlyList<BasketFact>> BasketsAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<BasketFact>>([.. Baskets.Values]);

        public ValueTask<BasketFact?> BasketAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Baskets.GetValueOrDefault(id));

        public ValueTask<InstrumentFact?> InstrumentAsync(string symbol, CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new InvalidOperationException("unreadable");
            }

            return ValueTask.FromResult(Instruments.GetValueOrDefault(symbol));
        }

        public ValueTask<InstrumentTradesFact> InstrumentTradesAsync(string symbol, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ReportsDown ? new InstrumentTradesFact(symbol, "unavailable", 0, null, null, null, [], string.Empty, []) : Trades.GetValueOrDefault(symbol) ?? new InstrumentTradesFact(symbol, "value", 0, null, null, null, [], string.Empty, []));

        public ValueTask<IReadOnlyList<InstrumentTradesFact>?> AllInstrumentTradesAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<InstrumentTradesFact>?>(ReportsDown ? null : [.. Trades.Values]);
    }

    public sealed class FakeProposalStore(FakeTimeProvider time) : IProposalInbox, IProposalReader, IProposalDecisions, IDisposable
    {
        public void Dispose() => _lock.Dispose();

        private readonly SemaphoreSlim _lock = new(1, 1);

        public Dictionary<long, StoredProposal> Proposals { get; } = [];

        public long NextId { get; set; } = 1;

        /// <summary>Runs once the lock is held (a test flips a setting here, as an owner could while a run waits).</summary>
        public Action? OnLock { get; set; }

        public int FailInserts { get; set; }

        public int InsertTries { get; private set; }

        public int Locks { get; private set; }

        public NewProposalDecision? LastDecision { get; private set; }

        public ValueTask<long?> InsertAsync(NewProposal p, CancellationToken cancellationToken)
        {
            if (Proposals.Values.Any(x => x.DedupeKey == p.DedupeKey && x.CreatedOn == p.CreatedOn))
            {
                return ValueTask.FromResult<long?>(null);
            }

            var id = NextId++;
            Proposals[id] = new StoredProposal(id, p.Kind, p.DedupeKey, p.CreatedOn, p.Subject, p.PayloadJson, p.Reason, p.EvidenceJson, p.ChecksJson, p.PackHash, p.ModelId, p.PromptVersion, p.CreatedBy, time.GetUtcNow(), p.ExpiresAt, null);
            return ValueTask.FromResult<long?>(id);
        }

        public ValueTask<StoredProposal?> GetAsync(long id, CancellationToken cancellationToken) => ValueTask.FromResult(Proposals.GetValueOrDefault(id));

        public ValueTask<IReadOnlyList<StoredProposal>> ListAsync(string? kind, int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<StoredProposal>>([.. Proposals.Values.Where(p => kind is null || p.Kind == kind).OrderByDescending(p => p.Id).Take(limit)]);

        public ValueTask<IReadOnlyList<StoredProposal>> PendingAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<StoredProposal>>([.. Proposals.Values.Where(p => p.Decision is null && p.ExpiresAt > now).OrderBy(p => p.Id)]);

        public ValueTask<bool> RejectedSinceAsync(string dedupeKey, DateTimeOffset since, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Proposals.Values.Any(p => p.DedupeKey == dedupeKey && p.Decision is { Decision: "rejected" } d && d.DecidedAt >= since));

        public ValueTask<int> CountAutoBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Proposals.Values.Count(p => p.Decision is { Auto: true } d && d.DecidedAt >= from && d.DecidedAt < to));

        public async ValueTask<IAsyncDisposable> LockAsync(CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            Locks++;
            OnLock?.Invoke();
            return new Release(_lock);
        }

        public ValueTask<(StoredProposalDecision Decision, bool Created)> InsertAsync(NewProposalDecision d, CancellationToken cancellationToken)
        {
            InsertTries++;
            if (FailInserts > 0)
            {
                FailInserts--;
                throw new InvalidOperationException("the write failed");
            }

            var proposal = Proposals[d.ProposalId];
            if (proposal.Decision is { } existing)
            {
                return ValueTask.FromResult((existing, false));
            }

            var row = new StoredProposalDecision(d.ProposalId, d.Decision, d.Actor, d.Auto, d.Reason, d.ResultRef, time.GetUtcNow());
            LastDecision = d;
            Proposals[d.ProposalId] = proposal with { Decision = row };
            return ValueTask.FromResult((row, true));
        }

        private sealed class Release(SemaphoreSlim slot) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                slot.Release();
                return ValueTask.CompletedTask;
            }
        }
    }
}
