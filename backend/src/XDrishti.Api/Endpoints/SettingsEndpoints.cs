using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.SettingsCatalog;

namespace XDrishti.Api.Endpoints;

internal sealed class SettingsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/settings").WithTags("Settings");

        group.MapGet("/catalog", GetCatalogAsync)
            .WithName("GetSettingsCatalog")
            .WithSummary("Every setting with its tier, value, default, range, safer direction and source");
    }

    private static async Task<Ok<SettingsCatalogResponse>> GetCatalogAsync(
        IQueryHandler<GetSettingsCatalogQuery, SettingsCatalogResponse> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetSettingsCatalogQuery(), cancellationToken));
}
