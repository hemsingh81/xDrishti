using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Live.Push;

namespace XDrishti.Api.Endpoints;

/// <summary>The live feed's state for the screen (ADR 0024 §7): the REST twin of the hub's <c>feedStatus</c> event, for when the hub is not connected. Needs a session like everything else.</summary>
internal sealed class LiveEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/live").WithTags("Live");

        group.MapGet("/status", GetStatusAsync)
            .WithName("GetLiveStatus")
            .WithSummary("State of the live price feed from its heartbeat (same fields as the hub event feedStatus): state (connected, idle, connecting, reconnecting, closed, paused, no_token, token_refused, flapping, or not_running when it has not reported for 60 seconds), subscribed instruments and the exchange time of its latest tick");
    }

    private static async Task<Ok<LiveFeedStatus>> GetStatusAsync(
        IQueryHandler<GetLiveStatusQuery, LiveFeedStatus> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetLiveStatusQuery(), cancellationToken));
}
