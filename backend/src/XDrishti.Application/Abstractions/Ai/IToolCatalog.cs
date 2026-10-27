namespace XDrishti.Application.Abstractions.Ai;

/// <summary>
/// The tool server (<c>xd-mcp</c>) as the assistant sees it (spec P9-03): the list of tools (cached for a minute) and one call. The tools are read-only; arguments are validated by the server, not trusted here. Never throws for an unreachable server.
/// </summary>
public interface IToolCatalog
{
    ValueTask<ToolCatalogSnapshot> ListAsync(CancellationToken cancellationToken);

    ValueTask<ToolInvocation> CallAsync(string name, string argumentsJson, CancellationToken cancellationToken);
}
