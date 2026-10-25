using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Paper;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The paper record of spec P10-01 (ADR 0116): the simulated trades of armed tickets and the runs that captured them. Read only; no order exists anywhere in the system, and nothing here writes.</summary>
internal sealed class PaperEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/paper").WithTags("Paper");

        group.MapGet("/entries", EntriesAsync)
            .WithName("ListPaperEntries")
            .WithSummary("The paper trades captured from armed tickets, newest entry first, one per ticket and account at the owner's quantity, each with its exit (instant, filled price, reason, rupees gross and net, R) once the replay closes it. Optional entry day range, account and epoch, limit 1 to 200 (default 50). Every response says: simulated on the official 1-minute bars; no order was placed or exists. Read only. 422 paper.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/capture-runs", RunsAsync)
            .WithName("ListPaperCaptureRuns")
            .WithSummary("The sessions the paper capture processed, newest first, one row per session and run, empty sessions included: what it saw, wrote, left pending and found inconsistent. Limit 1 to 200 (default 50). Read only. 422 paper.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<Results<Ok<PaperEntryList>, ProblemHttpResult>> EntriesAsync(
        DateOnly? from,
        DateOnly? to,
        Guid? account,
        long? epoch,
        int? limit,
        HttpContext http,
        IQueryHandler<ListPaperEntriesQuery, Result<PaperEntryList>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListPaperEntriesQuery(from, to, account, epoch, limit ?? 50), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<CaptureRunList>, ProblemHttpResult>> RunsAsync(
        int? limit,
        HttpContext http,
        IQueryHandler<ListCaptureRunsQuery, Result<CaptureRunList>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListCaptureRunsQuery(limit ?? 50), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422, as in the plans; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == PaperErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
