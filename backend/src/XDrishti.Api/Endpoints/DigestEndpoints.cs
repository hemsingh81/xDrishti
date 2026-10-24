using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The weekly learning digest of spec P7-15 (ADR 0094): the stored digests, read only. A digest is built by the weekly service or by <c>xd plan digest</c>; nothing here builds, applies or approves anything.</summary>
internal sealed class DigestEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var learning = api.MapGroup("/learning").WithTags("Learning");

        learning.MapGet("/digest", GetAsync)
            .WithName("GetLearningDigest")
            .WithSummary("The weekly learning digest of an ISO week (week=2026-W43), or the newest stored one: new and changed cells, drift and loss-tag proposals, lessons, hypotheses and experiments awaiting the owner, plan outcomes (simulated on plan prices), model status with the phase check, and the honest limits. Every section says nothing was applied. Read only. 404 learning.digest_not_found, 422 learning.invalid")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        learning.MapGet("/digests", ListAsync)
            .WithName("ListLearningDigests")
            .WithSummary("The stored weekly digests, newest week first (limit 1 to 52, default 12). Read only. 422 learning.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<Results<Ok<DigestView>, ProblemHttpResult>> GetAsync(
        string? week,
        HttpContext http,
        IQueryHandler<GetDigestQuery, Result<DigestView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetDigestQuery(week), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<IReadOnlyList<DigestView>>, ProblemHttpResult>> ListAsync(
        int? limit,
        HttpContext http,
        IQueryHandler<ListDigestsQuery, Result<IReadOnlyList<DigestView>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListDigestsQuery(limit ?? 12), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422, as in the Lab; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == LearningErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
