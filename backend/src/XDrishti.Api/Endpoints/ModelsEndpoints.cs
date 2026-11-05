using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The model registry of spec P6-07 (ADR 0070): read the registered models and one model in full, and (CF-10 part 10c-2, ADR 0217) whether a model may influence a decision. Training and naming a champion are the <c>xd learn model</c> and <c>xd learn designate</c> verbs; nothing here writes.</summary>
internal sealed class ModelsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var models = api.MapGroup("/models").WithTags("Learning");

        models.MapGet("/", ListAsync)
            .WithName("ListModels")
            .WithSummary("The registered models, newest first (limit 1 to 200, default 50; role challenger, champion or retired filters on the current role): hash, the training set, the validation verdict against the baselines (Eligible, CannotBeatBaselines or Inconclusive) with its reasons and pooled numbers, and the role. Advisory: nothing uses a model until P7. Empty before the first registration. 422 learning.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        models.MapGet("/{id:long}", GetAsync)
            .WithName("GetModel")
            .WithSummary("One model with its parameters, the full validation report with its honest limits, the calibration table, the feature importance (path-based, not exact, not SHAP) and every designation. No model bytes. 404 models.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        // the route of the spec (CF-10 contract): under /learning, beside the other learning reads; the registry reads above keep their older /api/models route
        api.MapGroup("/learning/models").WithTags("Learning").MapGet("/{id:long}/influence", InfluenceAsync)
            .WithName("GetModelInfluence")
            .WithSummary("Whether the model may influence a decision (CF-10 MlMayInfluence), term by term: the hold-out (once per model family), the forward shadow candidates, the calibration detector and the owner's switch. Off by default, an unavailable figure is never a pass, and nothing here places an order. 404 models.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<MlInfluenceView>, ProblemHttpResult>> InfluenceAsync(
        long id,
        HttpContext http,
        IQueryHandler<GetMlInfluenceQuery, Result<MlInfluenceView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetMlInfluenceQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<IReadOnlyList<ModelView>>, ProblemHttpResult>> ListAsync(
        string? role,
        int? limit,
        HttpContext http,
        IQueryHandler<ListModelsQuery, Result<IReadOnlyList<ModelView>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListModelsQuery(string.IsNullOrWhiteSpace(role) ? null : role, limit ?? 50), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ModelDetailView>, ProblemHttpResult>> GetAsync(
        long id,
        HttpContext http,
        IQueryHandler<GetModelQuery, Result<ModelDetailView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetModelQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422, as in the Lab; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == LearningErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
