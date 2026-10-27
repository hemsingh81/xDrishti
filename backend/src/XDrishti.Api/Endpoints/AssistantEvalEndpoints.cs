using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Assistant.Evaluation;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The eval results, read only (spec P9-09 Contract, ADR 0143): the runs, one run with its cases, and the status of the accepted baseline. There is no endpoint that runs the eval, grades, accepts or compares: those are the owner's verb <c>xd eval</c>, so nothing in the web can start a model run or call a run accepted.
/// The answers shown are the assistant's answers to synthetic fixture questions.
/// </summary>
internal sealed class AssistantEvalEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/assistant/evals").WithTags("Assistant");

        group.MapGet(string.Empty, ListAsync)
            .WithName("ListAssistantEvals")
            .WithSummary("The eval runs, newest first (limit 1 to 50, default 20), each with its summary: per set the outcome, the rates with their 95 % intervals and the verdict. Read only. 422 assistant.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/status", StatusAsync)
            .WithName("GetAssistantEvalStatus")
            .WithSummary("Whether the accepted eval run still describes the assistant: the baseline, the current fingerprint, stale and what differs (model, runtime, prompt X, sets, thresholds). No network call. With no accepted run: 'no accepted eval run'. Read only");

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetAssistantEval")
            .WithSummary("One eval run with its cases, the prompts and the parameters it measured. Read only. 404 assistant.eval_not_found")
            .ProducesRejections(conflict: false);
    }

    private static async Task<Results<Ok<IReadOnlyList<EvalRunView>>, ProblemHttpResult>> ListAsync(
        HttpContext http, IQueryHandler<ListEvalsQuery, Result<IReadOnlyList<EvalRunView>>> handler, CancellationToken cancellationToken, int limit = ListEvalsQuery.DefaultLimit)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new ListEvalsQuery(limit), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Ok<EvalStatusView>> StatusAsync(HttpContext http, IQueryHandler<GetEvalStatusQuery, EvalStatusView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetEvalStatusQuery(), cancellationToken));
    }

    private static async Task<Results<Ok<EvalDetailView>, ProblemHttpResult>> GetAsync(Guid id, HttpContext http, IQueryHandler<GetEvalQuery, Result<EvalDetailView>> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new GetEvalQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422 as the contract says; the rest through the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Kind != ErrorKind.Validation ? ProblemResults.From(error, http) : TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code));
}
