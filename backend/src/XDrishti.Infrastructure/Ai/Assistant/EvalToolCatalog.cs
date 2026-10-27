using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>
/// The tool client of the eval (spec P9-09 AC 13): the same MCP client as the assistant's, over the options of <c>Eval:Mcp</c>, registered under its own port so that the eval turn can only be handed this one. With no address or token it is disabled and nothing is called.
/// </summary>
internal sealed class EvalToolCatalog : IEvalToolCatalog, IDisposable
{
    private readonly McpToolCatalog _inner;

    public EvalToolCatalog(IOptions<EvalOptions> options, TimeProvider time, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        _inner = new McpToolCatalog(Options.Create(options.Value.Mcp), time, loggerFactory);
    }

    public ValueTask<ToolCatalogSnapshot> ListAsync(CancellationToken cancellationToken) => _inner.ListAsync(cancellationToken);

    public ValueTask<ToolInvocation> CallAsync(string name, string argumentsJson, CancellationToken cancellationToken) => _inner.CallAsync(name, argumentsJson, cancellationToken);

    public void Dispose() => _inner.Dispose();
}
