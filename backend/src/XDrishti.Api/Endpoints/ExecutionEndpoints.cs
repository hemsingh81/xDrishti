using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Execution;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The status of the execution service (spec P12-01b). One read-only route: it shows the service's heartbeat, its environment, each account's gate and the count of open intents. It has no write route and instructs nothing; the API reaches the service only
/// through rows in <c>execution.commands</c>, and nothing in this slice writes one.
/// </summary>
internal sealed class ExecutionEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/execution").WithTags("Execution");

        group.MapGet("/status", GetAsync)
            .WithName("GetExecutionStatus")
            .WithSummary("The execution service: down / recovering / ready / halted from its heartbeat (down when older than 30 s), its process environment (none, sandbox, live), each account's mode and gate (closed until the mode ladder exists), halts and the open intents. Read only; an empty database answers environment none and closed gates");
    }

    private static async Task<Ok<ExecutionStatusView>> GetAsync(IQueryHandler<GetExecutionStatusQuery, ExecutionStatusView> handler, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetExecutionStatusQuery(), cancellationToken));
    }
}
