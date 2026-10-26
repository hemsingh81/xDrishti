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

        group.MapGet("/accounts", AccountsAsync)
            .WithName("ListPaperAccounts")
            .WithSummary("The paper accounts (Trading role, mode paper) with their ledger derived from the stored paper trades: realised gross, charges and result (labelled gross, or after the stored sheet and not confirmed, never net as a result of record), equity = capital + realised result, funds (no leverage), open risk percent, unrealised (unavailable, never 0, when a mark is), last-session result and closed-trade drawdown. Optional epoch (default the newest). Read only; simulated, no order exists. 404 paper.not_found, 422 paper.invalid")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/accounts/{id:guid}/positions", PositionsAsync)
            .WithName("ListPaperPositions")
            .WithSummary("The open paper positions of one account with their marks (the close of the last closed session, or unavailable with the reason and no unrealised figure) and its closed trades, newest exit first. Read only. 404 paper.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/accounts/{id:guid}/equity", EquityAsync)
            .WithName("GetPaperEquity")
            .WithSummary("The realised-equity line of one paper account: one point per trading session from the epoch's start (capital plus the realised result of the exits up to that session). Optional from and to (sessions), at most 400 sessions. Read only. 404 paper.not_found, 422 paper.invalid")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/accounts/{id:guid}/reconcile", ReconcileAsync)
            .WithName("ReconcilePaperAccount")
            .WithSummary("Compares the derived ledger of one paper account with sums read straight from the stored rows (exits sum, entries without exit, exit before entry, entries of an account that is not paper now): status ok or each difference. Read only. 404 paper.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);
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

    private static async Task<Results<Ok<PaperAccountList>, ProblemHttpResult>> AccountsAsync(long? epoch, HttpContext http, IQueryHandler<GetPaperAccountsQuery, Result<PaperAccountList>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetPaperAccountsQuery(epoch), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<PaperPositionList>, ProblemHttpResult>> PositionsAsync(Guid id, long? epoch, HttpContext http, IQueryHandler<GetPaperPositionsQuery, Result<PaperPositionList>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetPaperPositionsQuery(id, epoch), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<PaperEquityView>, ProblemHttpResult>> EquityAsync(Guid id, DateOnly? from, DateOnly? to, long? epoch, HttpContext http, IQueryHandler<GetPaperEquityQuery, Result<PaperEquityView>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetPaperEquityQuery(id, from, to, epoch), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<PaperReconcileView>, ProblemHttpResult>> ReconcileAsync(Guid id, long? epoch, HttpContext http, IQueryHandler<ReconcilePaperQuery, Result<PaperReconcileView>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ReconcilePaperQuery(id, epoch), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422, as in the plans; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == PaperErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
