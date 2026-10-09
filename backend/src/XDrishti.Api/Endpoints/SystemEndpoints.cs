using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Platform.SystemStatus;

namespace XDrishti.Api.Endpoints;

internal sealed class SystemEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/system").WithTags("System");

        group.MapGet("/status", GetStatusAsync)
            .WithName("GetSystemStatus")
            .WithSummary("Health of the API, database and background services");
    }

    private static async Task<Ok<SystemStatusResponse>> GetStatusAsync(
        IQueryHandler<GetSystemStatusQuery, SystemStatusResponse> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetSystemStatusQuery(), cancellationToken));
}
