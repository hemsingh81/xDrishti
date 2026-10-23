using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The learning changelog of spec P6-08 (ADR 0073): read what changed, by whom or what, why and on what evidence, and where a cell is. Moves and reverts are the <c>xd learn lifecycle</c> verb; nothing here writes.</summary>
internal sealed class ChangelogEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var changelog = api.MapGroup("/changelog").WithTags("Learning");

        changelog.MapGet("/", ListAsync)
            .WithName("ListChangelog")
            .WithSummary("The learning changelog, newest first (limit 1 to 200, default 50; subjectKind cell or model; subject one cell key or model:ID): what changed, by whom or what, why, before and after references and evidence ids. Rows are never changed; a revert is a row that points at the one it reverts. Empty before the first record. 422 learning.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        changelog.MapGet("/cell-state", GetStateAsync)
            .WithName("GetCellState")
            .WithSummary("Where a cell is (query cell=strategy|tf|side|style|exit|regime), replayed from its changelog rows, with the moves the lifecycle allows next. A record only: nothing acts on a cell's state before paper data and live exist. 422 learning.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        changelog.MapGet("/{id:long}", GetAsync)
            .WithName("GetChangelogEntry")
            .WithSummary("One changelog row. 404 changelog.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<IReadOnlyList<ChangelogEntryView>>, ProblemHttpResult>> ListAsync(
        string? subjectKind,
        string? subject,
        int? limit,
        HttpContext http,
        IQueryHandler<ListChangelogQuery, Result<IReadOnlyList<ChangelogEntryView>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListChangelogQuery(string.IsNullOrWhiteSpace(subjectKind) ? null : subjectKind, string.IsNullOrWhiteSpace(subject) ? null : subject, limit ?? 50), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<CellStateView>, ProblemHttpResult>> GetStateAsync(
        string? cell,
        HttpContext http,
        IQueryHandler<GetCellStateQuery, Result<CellStateView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetCellStateQuery(cell ?? string.Empty), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ChangelogEntryView>, ProblemHttpResult>> GetAsync(
        long id,
        HttpContext http,
        IQueryHandler<GetChangelogEntryQuery, Result<ChangelogEntryView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetChangelogEntryQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422, as for the models; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == LearningErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
