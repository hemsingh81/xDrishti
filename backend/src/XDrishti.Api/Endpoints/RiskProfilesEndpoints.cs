using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Accounts.RiskProfiles;

namespace XDrishti.Api.Endpoints;

/// <summary>Shared risk profiles that trading accounts refer to. Writes need a recent password re-entry.</summary>
internal sealed class RiskProfilesEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/risk-profiles").WithTags("Risk profiles");

        group.MapGet("/", ListAsync)
            .WithName("ListRiskProfiles")
            .WithSummary("All risk profiles with the number of accounts that use each");

        group.MapPost("/", CreateAsync)
            .RequireStepUp()
            .WithName("CreateRiskProfile")
            .WithSummary("Create a risk profile; needs step-up")
            .ProducesRejections(conflict: true, notFound: false);

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequireStepUp()
            .WithName("UpdateRiskProfile")
            .WithSummary("Replace the name and values of a risk profile; needs step-up")
            .ProducesRejections(conflict: true);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireStepUp()
            .WithName("DeleteRiskProfile")
            .WithSummary("Delete a risk profile; refused (409) while an account uses it and for the built-in profiles; needs step-up")
            .ProducesRejections(conflict: true);
    }

    private static async Task<Ok<IReadOnlyList<RiskProfileView>>> ListAsync(
        IQueryHandler<ListRiskProfilesQuery, IReadOnlyList<RiskProfileView>> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListRiskProfilesQuery(), cancellationToken));

    private static async Task<Results<Created<RiskProfileView>, ProblemHttpResult>> CreateAsync(
        RiskProfileInput input,
        HttpContext http,
        ICommandHandler<CreateRiskProfileCommand, RiskProfileView> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CreateRiskProfileCommand(input, http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Created($"/api/risk-profiles/{result.Value.Id}", result.Value);
    }

    private static async Task<Results<Ok<RiskProfileView>, ProblemHttpResult>> UpdateAsync(
        Guid id,
        RiskProfileInput input,
        HttpContext http,
        ICommandHandler<UpdateRiskProfileCommand, RiskProfileView> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new UpdateRiskProfileCommand(id, input, http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<DeleteRiskProfileCommand> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new DeleteRiskProfileCommand(id, http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.NoContent();
    }
}
