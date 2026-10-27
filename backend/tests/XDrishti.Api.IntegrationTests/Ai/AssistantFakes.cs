using System.Runtime.CompilerServices;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>The model server, the tool server and the model health of the assistant, scripted for the API tests (nothing leaves the process).</summary>
internal sealed class ScriptedModel : IChatModel
{
    private readonly Queue<Func<CancellationToken, IAsyncEnumerable<ModelStreamPart>>> _scripts = new();

    public int Calls { get; private set; }

    public void Script(Func<CancellationToken, IAsyncEnumerable<ModelStreamPart>> script) => _scripts.Enqueue(script);

    public void Script(params ModelStreamPart[] parts) => _scripts.Enqueue(token => FromList(parts, token));

    public IAsyncEnumerable<ModelStreamPart> StreamAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        return _scripts.TryDequeue(out var script) ? script(cancellationToken) : throw new InvalidOperationException("The model was called more often than scripted.");
    }

    public static async IAsyncEnumerable<ModelStreamPart> FromList(IEnumerable<ModelStreamPart> parts, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return part;
        }
    }
}

internal sealed class ScriptedTools : IToolCatalog
{
    public ToolCatalogState State { get; set; } = ToolCatalogState.Ready;

    public int Calls { get; private set; }

    public ValueTask<ToolCatalogSnapshot> ListAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(State == ToolCatalogState.Ready
            ? new ToolCatalogSnapshot(ToolCatalogState.Ready, [new ToolDescriptor("get_report", "A report.", """{"type":"object","properties":{"report":{"type":"string"}}}""")])
            : ToolCatalogSnapshot.Unavailable(State));

    public ValueTask<ToolInvocation> CallAsync(string name, string argumentsJson, CancellationToken cancellationToken)
    {
        Calls++;
        return ValueTask.FromResult(new ToolInvocation(true, false, $$"""{"ok":true,"tool":"{{name}}","rows":[{"n":143,"expectancy":0.1187}],"rowCount":1,"truncated":false,"evidence":{"basis":"gross","n":143,"dateRange":"2025"},"redactions":0,"injectionFlags":1}"""));
    }
}

internal sealed class ScriptedHealth : ILlmHealth
{
    public LlmState State { get; set; } = LlmState.Ready;

    public int Calls { get; private set; }

    public Task<LlmStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new LlmStatus(State, "xd-main", "Qwen3-30B-A3B-Q4_K_M.gguf", "0d003f6662faee786ed5da3e31b29c978de5ae5d275c8794c606a7f3c01aa8f5", "b1", DateTimeOffset.UtcNow, "fixed"));
    }
}
