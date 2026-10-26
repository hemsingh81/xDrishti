using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Paper;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The configuration governance of the paper count (spec P10-05, ADR 0119): the epochs, the recorded changes, the configuration now and the queue of accepted hypotheses. Every route is a GET; the owner acts (restart, explain) are CLI verbs only.
/// Nothing here changes a setting, applies a hypothesis or places an order.
/// </summary>
internal sealed class PaperGovernanceEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/paper").WithTags("Paper");

        group.MapGet("/epochs", EpochsAsync)
            .WithName("ListPaperEpochs")
            .WithSummary("The measurement epochs, newest first: kind, first session, end (the next epoch's start), configuration fingerprint (short), counted and straddle entries, risk-down changes and unexplained changes. Epochs are never pooled. Read only.");

        group.MapGet("/epochs/{id:long}", EpochAsync)
            .WithName("GetPaperEpoch")
            .WithSummary("One epoch with the configuration changes recorded under it and the owner's notes. Read only. 404 paper.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/config", ConfigAsync)
            .WithName("GetPaperConfig")
            .WithSummary("The configuration now with the class rule of every component, the last snapshot's hash, the differences not yet captured (each marked would restart the count or noted only) and the learning services enabled during an epoch (blockers). A preview: nothing is stored. Read only.");

        group.MapGet("/config/changes", ChangesAsync)
            .WithName("ListPaperConfigChanges")
            .WithSummary("The recorded configuration changes, newest first, with class, before, after, effect and explanation (hypothesis:id, changelog:id or unexplained). Optional epoch, limit 1 to 200 (default 50). Read only. 422 paper.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/queue", QueueAsync)
            .WithName("GetPaperQueue")
            .WithSummary("Accepted hypotheses that no change names: age, cell, knob, from, to and experiment. Accepted means judged better offline; nothing was applied; applying it starts a new epoch. The system never applies or reorders it. Read only.");
    }

    private static async Task<Results<Ok<EpochList>, ProblemHttpResult>> EpochsAsync(HttpContext http, IQueryHandler<GetEpochsQuery, Result<EpochList>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetEpochsQuery(), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<EpochDetail>, ProblemHttpResult>> EpochAsync(long id, HttpContext http, IQueryHandler<GetEpochQuery, Result<EpochDetail>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetEpochQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ConfigView>, ProblemHttpResult>> ConfigAsync(HttpContext http, IQueryHandler<GetConfigQuery, Result<ConfigView>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetConfigQuery(), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ConfigChangeList>, ProblemHttpResult>> ChangesAsync(long? epoch, int? limit, HttpContext http, IQueryHandler<GetConfigChangesQuery, Result<ConfigChangeList>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetConfigChangesQuery(epoch, limit ?? 50), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<QueueView>, ProblemHttpResult>> QueueAsync(HttpContext http, IQueryHandler<GetQueueQuery, Result<QueueView>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetQueueQuery(), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == PaperErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
