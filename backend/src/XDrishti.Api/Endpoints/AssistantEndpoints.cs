using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Ai;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The AI assistant's surface (spec P9-01 so far): the state of the local model server. Read only, behind the session like every route; it starts nothing (the owner runs <c>scripts/xd-ai.sh start</c>), makes no call while the deployment flag is off and never
/// answers the key, the path or the address of the server.
/// </summary>
internal sealed class AssistantEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/assistant").WithTags("Assistant");

        group.MapGet("/llm-status", GetLlmStatusAsync)
            .WithName("GetLlmStatus")
            .WithSummary("Whether the local model server is ready, loading, down or misconfigured, with the loaded model file (and its SHA-256 when it is a pinned one), the build and how to start it. Read only; cached a few seconds; no key, path or address in the answer")
            .ProducesRejections(conflict: false, notFound: false);
    }

    private static async Task<Ok<LlmStatusView>> GetLlmStatusAsync(HttpContext http, IQueryHandler<GetLlmStatusQuery, LlmStatusView> handler, CancellationToken cancellationToken)
    {
        var view = await handler.HandleAsync(new GetLlmStatusQuery(), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(view);
    }
}
