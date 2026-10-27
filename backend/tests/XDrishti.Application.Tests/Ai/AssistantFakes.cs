using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Application.Tests.Ai;

/// <summary>The ports of the assistant's turn as in-memory fakes, with counters: a refusal must not touch the model, the tool server or the store.</summary>
internal sealed class AssistantRig
{
    public AssistantSettings Settings { get; set; } = new(7, true, false, 6);

    public LlmState Llm { get; set; } = LlmState.Ready;

    public ToolCatalogState Tools { get; set; } = ToolCatalogState.Ready;

    public bool NightlyRunning { get; set; }

    public FakeModel Model { get; } = new();

    public FakeCatalog Catalog { get; } = new();

    public FakeStore Store { get; } = new();

    public TurnOptions Options { get; } = new();

    public TurnGate Gate { get; } = new();

    public TimeProvider Time { get; set; } = TimeProvider.System;

    public int HealthCalls { get; private set; }

    public AssistantTurnService Service()
    {
        Catalog.State = Tools;
        var settings = new FakeSettings(this);
        var health = new FakeHealth(this);
        return new AssistantTurnService(settings, health, Catalog, new FakeNightly(this), Store, Model, Gate, Options, Time, NullLogger<AssistantTurnService>.Instance);
    }

    public sealed class FakeSettings(AssistantRig rig) : IAssistantSettingsStore
    {
        public ValueTask<AssistantSettings> CurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult(rig.Settings);

        public ValueTask<AssistantSettings?> AppendAsync(AssistantSettingsChange change, long? expectedCurrentId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    public sealed class FakeHealth(AssistantRig rig) : ILlmHealth
    {
        public Task<LlmStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            rig.HealthCalls++;
            return Task.FromResult(new LlmStatus(rig.Llm, "xd-main", "m.gguf", "abc", "b1", DateTimeOffset.UtcNow, "fixed"));
        }
    }

    public sealed class FakeNightly(AssistantRig rig) : INightlyRunProbe
    {
        public ValueTask<bool> IsRunningAsync(TimeSpan window, CancellationToken cancellationToken) => ValueTask.FromResult(rig.NightlyRunning);
    }

    /// <summary>Scripted model: each call to <see cref="StreamAsync"/> plays the next script (a list of parts); a script may throw or wait.</summary>
    public sealed class FakeModel : IChatModel
    {
        private readonly Queue<Func<CancellationToken, IAsyncEnumerable<ModelStreamPart>>> _scripts = new();

        public int Calls { get; private set; }

        public List<ModelRequest> Requests { get; } = [];

        public Func<CancellationToken, IAsyncEnumerable<ModelStreamPart>>? Repeat { get; set; }

        public void Script(params ModelStreamPart[] parts) => _scripts.Enqueue(token => FromList(parts, token));

        public void Script(Func<CancellationToken, IAsyncEnumerable<ModelStreamPart>> script) => _scripts.Enqueue(script);

        public IAsyncEnumerable<ModelStreamPart> StreamAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Requests.Add(request);
            if (_scripts.TryDequeue(out var script))
            {
                return script(cancellationToken);
            }

            return Repeat is { } repeat ? repeat(cancellationToken) : throw new InvalidOperationException("The model was called more often than scripted.");
        }

        public static async IAsyncEnumerable<ModelStreamPart> FromList(IEnumerable<ModelStreamPart> parts, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var part in parts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return part;
            }
        }
    }

    public sealed class FakeCatalog : IToolCatalog
    {
        public ToolCatalogState State { get; set; } = ToolCatalogState.Ready;

        public List<(string Name, string Args)> Calls { get; } = [];

        public Func<string, string, string> Respond { get; set; } = (name, _) => $$"""{"ok":true,"tool":"{{name}}","rows":[{"n":143,"expectancy":0.1187}],"rowCount":1,"truncated":false,"evidence":{"basis":"gross","n":143,"dateRange":"2025-01-01..2025-12-31"},"redactions":0,"injectionFlags":0}""";

        public ValueTask<ToolCatalogSnapshot> ListAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(State == ToolCatalogState.Ready
                ? new ToolCatalogSnapshot(ToolCatalogState.Ready, [new ToolDescriptor("get_report", "A report.", """{"type":"object","properties":{"report":{"type":"string"}}}"""), new ToolDescriptor("get_portfolio", "The portfolio.", """{"type":"object"}""")])
                : ToolCatalogSnapshot.Unavailable(State));

        public ValueTask<ToolInvocation> CallAsync(string name, string argumentsJson, CancellationToken cancellationToken)
        {
            Calls.Add((name, argumentsJson));
            return ValueTask.FromResult(new ToolInvocation(true, false, Respond(name, argumentsJson)));
        }
    }

    public sealed class FakeStore : IConversationStore
    {
        public List<StartTurnRecord> Started { get; } = [];

        public List<ToolCallRecord> ToolCalls { get; } = [];

        public List<AssistantMessageRecord> Completed { get; } = [];

        public HashSet<Guid> Known { get; } = [];

        public List<HistoryMessage> History { get; } = [];

        public ValueTask<bool> ExistsAsync(Guid conversationId, CancellationToken cancellationToken) => ValueTask.FromResult(Known.Contains(conversationId));

        public ValueTask<IReadOnlyList<HistoryMessage>> RecentAsync(Guid conversationId, int count, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<HistoryMessage>>([.. History.TakeLast(count)]);

        public ValueTask<StartedTurn> StartTurnAsync(StartTurnRecord record, CancellationToken cancellationToken)
        {
            Started.Add(record);
            return ValueTask.FromResult(new StartedTurn(record.ConversationId ?? Guid.NewGuid(), 100 + Started.Count, 1));
        }

        public ValueTask<long> RecordToolCallAsync(ToolCallRecord record, CancellationToken cancellationToken)
        {
            ToolCalls.Add(record);
            return ValueTask.FromResult((long)ToolCalls.Count);
        }

        public ValueTask<long> CompleteTurnAsync(AssistantMessageRecord record, CancellationToken cancellationToken)
        {
            Completed.Add(record);
            return ValueTask.FromResult((long)(500 + Completed.Count));
        }
    }
}
