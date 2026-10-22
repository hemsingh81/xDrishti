using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Api.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The Lab runs of spec P5-02 (ADR 0058): post a quick test, list and read runs, cancel. A post never waits for the run; a background service of this host carries it out and the hub tells the screen how it goes.</summary>
internal sealed class LabRunsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var runs = api.MapGroup("/lab/runs").WithTags("Lab");

        runs.MapPost("/", StartAsync)
            .WithName("StartLabRun")
            .WithSummary("Queue a quick test of a stored strategy version on a basket or a list of symbols (202 with the run); the same input again returns the run that holds it (200). 422 lab.invalid_input, 404 lab.not_found, 409 lab.no_data")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        runs.MapGet("/", ListAsync)
            .WithName("ListLabRuns")
            .WithSummary("Lab runs, newest first; filter by strategyId and state; limit 1 to 200 (default 50)")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        runs.MapGet("/{id:guid}", GetAsync)
            .WithName("GetLabRun")
            .WithSummary("One Lab run: state, stage, progress, the simulation run keys, counts, located problems, the stage log and the labels (cost basis, sample, exploration)")
            .ProducesProblem(StatusCodes.Status404NotFound);

        runs.MapGet("/{id:guid}/report", GetReportAsync)
            .WithName("GetLabReport")
            .WithSummary("The report of a succeeded full or robust run, built from the stored result: verdict, gates, figures, best cells, warnings, honest limits, a deterministic plain-English summary. 409 lab.no_report for any other run")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        runs.MapGet("/{id:guid}/report/html", GetReportHtmlAsync)
            .WithName("GetLabReportHtml")
            .WithSummary("The report as one static, inert HTML page")
            .Produces<string>(StatusCodes.Status200OK, "text/html")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        runs.MapGet("/{id:guid}/report/folder", GetReportFolderAsync)
            .WithName("DownloadLabReportFolder")
            .WithSummary("The report as a zip folder (report.json, report.html, summary.txt, README.txt, manifest.json), identical bytes for the same report")
            .Produces<byte[]>(StatusCodes.Status200OK, "application/zip")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet("/lab/strategies/{id}/ledger", GetLedgerAsync)
            .WithTags("Lab")
            .WithName("GetLabLedger")
            .WithSummary("The test ledger of a strategy family: every Lab run created for it, the number of distinct variants (hashes) and the factor the qualification gates are tightened by now")
            .ProducesProblem(StatusCodes.Status404NotFound);

        runs.MapPost("/{id:guid}/cancel", CancelAsync)
            .WithName("CancelLabRun")
            .WithSummary("Stop a run: a queued run at once, a running run at its next safe point between instruments (202); nothing is stored for it. 409 lab.not_cancellable once it has finished")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Results<Accepted<LabRunView>, Ok<LabRunView>, ProblemHttpResult>> StartAsync(
        StartLabRunRequest request,
        HttpContext http,
        ICommandHandler<StartLabRunCommand, StartedLabRun> handler,
        CancellationToken cancellationToken)
    {
        var universe = request.Universe;
        var result = await handler.HandleAsync(
            new StartLabRunCommand(request.StrategyId, request.Version, universe?.BasketId, universe?.Symbols, request.Method, request.Window?.From, request.Window?.To),
            cancellationToken);
        if (result.IsFailure)
        {
            return Problem(result.Error, http);
        }

        var started = result.Value;
        return started.Created ? TypedResults.Accepted($"/api/lab/runs/{started.Run.Id}", started.Run) : TypedResults.Ok(started.Run);
    }

    private static async Task<Results<Ok<IReadOnlyList<LabRunView>>, ProblemHttpResult>> ListAsync(
        string? strategyId,
        string? state,
        int? limit,
        HttpContext http,
        IQueryHandler<ListLabRunsQuery, Result<IReadOnlyList<LabRunView>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListLabRunsQuery(strategyId, state, limit), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<LabRunView>, ProblemHttpResult>> GetAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetLabRunQuery, Result<LabRunView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetLabRunQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<LabReportView>, ProblemHttpResult>> GetReportAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetLabReportQuery, Result<LabReportView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetLabReportQuery(id), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<ContentHttpResult, ProblemHttpResult>> GetReportHtmlAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetLabReportQuery, Result<LabReportView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetLabReportQuery(id), cancellationToken);
        if (result.IsFailure)
        {
            return ProblemResults.From(result.Error, http);
        }

        http.Response.Headers.ContentSecurityPolicy = ReportHtml.Policy;
        return TypedResults.Text(ReportHtml.Render(result.Value), "text/html", System.Text.Encoding.UTF8);
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> GetReportFolderAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetLabReportQuery, Result<LabReportView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetLabReportQuery(id), cancellationToken);
        return result.IsFailure
            ? ProblemResults.From(result.Error, http)
            : TypedResults.File(ReportFolder.Build(result.Value), "application/zip", $"lab-report-{id:D}.zip");
    }

    private static async Task<Results<Ok<LedgerView>, ProblemHttpResult>> GetLedgerAsync(
        string id,
        HttpContext http,
        IQueryHandler<GetLedgerQuery, Result<LedgerView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetLedgerQuery(id), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Accepted<LabRunView>, ProblemHttpResult>> CancelAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<CancelLabRunCommand, LabRunView> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CancelLabRunCommand(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Accepted($"/api/lab/runs/{id}", result.Value);
    }

    /// <summary>A refused input is 422 (the spec's word, as the strategy create), every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == LabErrors.InvalidInputCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
