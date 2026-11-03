using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The stored verdicts of the evaluation protocols F, X, P and L (spec P15-10), S (the pairs study, spec P14-13) and O (option expression versus cash, spec P14-09): the records of a family, newest first, and one record with its evidence block. Read only: the records are written by <c>xd learn extension-eval</c>, <c>xd pairs eval</c> and <c>xd options expression-eval</c> and nothing here changes a setting or places an order.
/// </summary>
internal sealed class ExtensionEvaluationEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/research/extensions").WithTags("Research");

        group.MapGet("", ListAsync)
            .WithName("ListExtensionEvaluations")
            .WithSummary("The stored evaluations of one family (filter:results, ext:ev-1, ext:fu-1, ext:llm-1, ext:iv-1, pead, pairs:intraday-1 or expr:option_buy), newest first, at most 100: the verdict (KEEP, DROP, INCONCLUSIVE or NOT_EVALUABLE) with the named missing quantities, the counts, the limits and the estimates, evidence and inputs as JSON. A value that is not available is the text unavailable, never a number. 400 extensions.invalid_query for an unknown family")
            .ProducesValidationProblem();

        group.MapGet("/{id}", GetAsync)
            .WithName("GetExtensionEvaluation")
            .WithSummary("One stored evaluation by its 64-character id, with its evidence block; 404 extensions.not_found for an unknown id")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<ExtensionEvaluationList>, ProblemHttpResult>> ListAsync(
        HttpContext http,
        IQueryHandler<ListExtensionEvaluationsQuery, Result<ExtensionEvaluationList>> handler,
        CancellationToken cancellationToken,
        string? family = null)
    {
        var result = await handler.HandleAsync(new ListExtensionEvaluationsQuery(family), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ExtensionEvaluationView>, ProblemHttpResult>> GetAsync(
        HttpContext http,
        string id,
        IQueryHandler<GetExtensionEvaluationQuery, Result<ExtensionEvaluationView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetExtensionEvaluationQuery(id), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
