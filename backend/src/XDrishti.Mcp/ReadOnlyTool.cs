using System.Text.Json;
using Microsoft.Extensions.AI;

namespace XDrishti.Mcp;

/// <summary>
/// Base of the four tools (spec P9-02). A tool is an <c>AIFunction</c> with its own JSON Schema, so that the schema the model sees (and its bounds) is the contract <see cref="ToolSpec"/> checks, and a bad argument becomes <c>tool.invalid_arguments</c> naming the property instead of an
/// exception of the SDK's binder. The body only builds a <see cref="ToolOutcome"/>; <see cref="ToolRunner"/> enforces everything else. A tool class has no write capability: it receives read-only ports and nothing else.
/// </summary>
internal abstract class ReadOnlyTool(ToolSpec spec, ToolRunner runner) : AIFunction
{
    public override string Name => spec.Name;

    public override string Description => spec.Description;

    public override JsonElement JsonSchema => spec.Schema;

    protected abstract Task<ToolOutcome> RunAsync(ToolArguments arguments, CancellationToken cancellationToken);

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
        await runner.RunAsync(spec, arguments, RunAsync, cancellationToken);
}
