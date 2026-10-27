using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Ai;
using XDrishti.Application.Ai.Briefing;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Assistant.Text;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Ai;

/// <summary>The ports of the briefing as in-memory fakes with counters (spec P9-06 test plan): a refusal or a switch that is off must not touch the model.</summary>
internal sealed class BriefingRig
{
    public AssistantSettings Settings { get; set; } = new(1, true, false, 6);

    public LlmState Llm { get; set; } = LlmState.Ready;

    public bool NightlyRunning { get; set; }

    public BriefingOptions Options { get; } = new() { UseModel = true, GateWaitMinutes = 0, ReadyWaitSeconds = 0 };

    public FakeTimeProvider Time { get; } = new();

    public TurnGate Gate { get; } = new();

    public FakeSource Source { get; } = new();

    public FakeStore Store { get; } = new();

    public FakeWriter Writer { get; } = new();

    public List<ModelCallRecord> Audit { get; } = [];

    public int HealthCalls { get; private set; }

    /// <summary>The clock starts at <paramref name="now"/>, by default 2026-10-28 21:00 IST: a Wednesday, after the nightly window.</summary>
    public BriefingRig(DateTimeOffset? now = null) => Time.SetUtcNow(now ?? new DateTimeOffset(2026, 10, 28, 21, 0, 0, TimeSpan.FromHours(5.5)));

    public BuildBriefingHandler Handler() =>
        new(Source, Store, Writer, new SettingsPort(this), new Health(this), new Nightly(this), new AuditPort(this), Gate, new TurnOptions(), Options, Time, NullLogger<BuildBriefingHandler>.Instance);

    public IAssistantSettingsStore SettingsStore() => new SettingsPort(this);

    public static BuildBriefingCommand Daily(bool newVersion = false, bool forceDeterministic = false) => new(BriefingFacts.Daily, "2026-10-28", newVersion, forceDeterministic, "test");

    public sealed class FakeSource : IBriefingFactSource
    {
        public BriefingFacts Facts { get; set; } = FactsOf();

        public Error? Failure { get; set; }

        public int Builds { get; private set; }

        public ValueTask<DateOnly?> NewestPlanDateAsync(CancellationToken cancellationToken) => ValueTask.FromResult<DateOnly?>(new DateOnly(2026, 10, 28));

        public ValueTask<Result<BriefingInput>> BuildAsync(string kind, string subject, bool portfolioContext, CancellationToken cancellationToken)
        {
            Builds++;
            return ValueTask.FromResult(Failure is { } error ? Result.Failure<BriefingInput>(error) : Result.Success(new BriefingInput(Facts with { Kind = kind, Subject = subject }, 0)));
        }
    }

    public sealed class FakeStore : IBriefingStore
    {
        private readonly List<StoredBriefing> _rows = [];

        public int Inserts { get; private set; }

        public IReadOnlyList<StoredBriefing> Rows => _rows;

        public ValueTask<(StoredBriefing Stored, bool Created)> InsertAsync(NewBriefing b, CancellationToken cancellationToken)
        {
            var current = _rows.Where(r => r.Kind == b.Kind && r.Subject == b.Subject).MaxBy(r => r.Version);
            if (current is not null && !b.NewVersion)
            {
                return ValueTask.FromResult((current, false));
            }

            Inserts++;
            var row = new StoredBriefing(
                _rows.Count + 1, b.Kind, b.Subject, (current?.Version ?? 0) + 1, b.Source, b.Reason, b.Text, b.DeterministicText, b.FactsJson, b.FactsHash, b.ModelId, b.PromptVersion, b.WordCount, DateTimeOffset.UnixEpoch, b.Attempts);
            _rows.Add(row);
            return ValueTask.FromResult((row, true));
        }

        public ValueTask<StoredBriefing?> FindAsync(string kind, string subject, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_rows.Where(r => r.Kind == kind && r.Subject == subject).MaxBy(r => r.Version));

        public ValueTask<IReadOnlyList<StoredBriefing>> ListAsync(string? kind, int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<StoredBriefing>>([.. _rows.Where(r => kind is null || r.Kind == kind).OrderByDescending(r => r.Id).Take(limit)]);
    }

    public sealed class FakeWriter : IBriefingWriter
    {
        private readonly Queue<object> _script = new();

        public List<BriefingRequest> Requests { get; } = [];

        public void Returns(string text, string finish = "stop") => _script.Enqueue(new BriefingWrite(text, 1000, 200, finish, 20));

        public void Throws() => _script.Enqueue(new ModelUnavailableException("down"));

        public ValueTask<BriefingWrite> WriteAsync(BriefingRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return _script.Dequeue() switch
            {
                ModelUnavailableException ex => throw ex,
                BriefingWrite write => ValueTask.FromResult(write),
                _ => throw new InvalidOperationException(),
            };
        }
    }

    public static BriefingFacts FactsOf() => new(
        BriefingFacts.Daily, "2026-10-28", true, ["AAA", "BBB"],
        [
            new FactLine(string.Empty, "plan", "plan", "Plan for 2026-10-28 is published with 2 primary and 0 reserve tickets; 1 candidates were rejected."),
            new FactLine(string.Empty, "plan", "provisional", "The plan is provisional: its cells are not paper-proven."),
            new FactLine("AAA", "ticket", "ticket", "AAA BUY primary ticket, strategy orb_15."),
            new FactLine("AAA", "ticketdetail", "levels", "AAA reference price 100.00, stop 98.50, target 104.00."),
            new FactLine("BBB", "ticket", "ticket", "BBB SELL primary ticket, strategy orb_15."),
            new FactLine("BBB", "ticketdetail", "levels", "BBB reference price 202.00, stop 205.00, target 196.00."),
            new FactLine(string.Empty, "risk", "risk", "Basis: gross costs were assumed."),
        ], 42, null, []);

    public const string GoodText = "The plan for 2026-10-28 is published, provisional, with 2 primary tickets.\n- AAA BUY: stop 98.50, target 104.00.\n- BBB SELL: stop 205.00, target 196.00.";

    public const string BadText = "The plan is provisional.\n- AAA BUY: stop 98.60, target 104.00.\n- BBB SELL: stop 205.00, target 196.00.";

    private sealed class SettingsPort(BriefingRig rig) : IAssistantSettingsStore
    {
        public ValueTask<AssistantSettings> CurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult(rig.Settings);

        public ValueTask<AssistantSettings?> AppendAsync(AssistantSettingsChange change, long? expectedCurrentId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Health(BriefingRig rig) : ILlmHealth
    {
        public Task<LlmStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            rig.HealthCalls++;
            return Task.FromResult(new LlmStatus(rig.Llm, "xd-main", "m.gguf", "abc", "b1", DateTimeOffset.UtcNow, "fixed"));
        }
    }

    private sealed class Nightly(BriefingRig rig) : INightlyRunProbe
    {
        public ValueTask<bool> IsRunningAsync(TimeSpan window, CancellationToken cancellationToken) => ValueTask.FromResult(rig.NightlyRunning);
    }

    private sealed class AuditPort(BriefingRig rig) : IModelCallAudit
    {
        public ValueTask RecordAsync(ModelCallRecord record, CancellationToken cancellationToken)
        {
            rig.Audit.Add(record);
            return ValueTask.CompletedTask;
        }
    }
}
