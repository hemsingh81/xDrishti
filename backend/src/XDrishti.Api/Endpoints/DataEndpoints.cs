using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// Market data (ADR 0016, P1-17a): what the system holds (per instrument: first bar, last bar, bars, source, how far behind), the data job that loads the CSV
/// folder, fetches what is missing from Dhan's Data API and aggregates every timeframe, and the latest bars of an instrument. All routes need a session; starting a
/// job also needs the CSRF token; none needs a step-up (the job only reads the mounted folder and the Data account's already stored token). Part of the market module.
/// </summary>
internal sealed class DataEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/data").WithTags("Data");

        group.MapGet("/overview", GetOverviewAsync)
            .WithName("GetDataOverview")
            .WithSummary("The CSV folder (files loaded and pending), the Dhan Data account and whether a fetch can run, the aggregates per timeframe, the stored bars with the last bar (last tick) and how far behind it is, the job in progress and the last job of each phase");

        group.MapGet("/instruments", ListInstrumentsAsync)
            .WithName("ListDataInstruments")
            .WithSummary("The tracked instruments and the ones with bars, with first bar, last bar, 1-minute bars, source (csv|dhan|mixed) and status (current|behind|empty): q, status, sort (symbol|name|firstBar|lastBar|bars, '-' for descending), page, pageSize (max 200)")
            .ProducesValidationProblem();

        group.MapGet("/instruments/{id:guid}/bars", GetBarsAsync)
            .WithName("GetInstrumentBars")
            .WithSummary("The latest bars of an instrument, newest first: timeframe 1m|5m|10m|15m|30m|1h|1D|1W|1M (default 1D), take 1 to 1000 (default 100)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/refresh", StartAsync)
            .WithName("StartDataRefresh")
            .WithSummary("Start a data update in the background: steps (any of csv, dhan, aggregate; default all, always in that order), forceCsv loads unchanged CSV files again. 202 with the job; 409 data.job_running while one runs, 409 data.master_missing without an instrument list")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/jobs", ListJobsAsync)
            .WithName("ListDataJobs")
            .WithSummary("The most recent data updates, newest first (take 1 to 100, default 20)");

        group.MapGet("/jobs/{id:guid}", GetJobAsync)
            .WithName("GetDataJob")
            .WithSummary("One data update with the progress and result of each phase")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Ok<DataOverview>> GetOverviewAsync(
        IQueryHandler<GetDataOverviewQuery, DataOverview> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetDataOverviewQuery(), cancellationToken));

    private static async Task<Results<Ok<DataInstrumentPage>, ProblemHttpResult>> ListInstrumentsAsync(
        HttpContext http,
        IQueryHandler<ListDataInstrumentsQuery, Result<DataInstrumentPage>> handler,
        CancellationToken cancellationToken,
        [FromQuery(Name = "q")] string? q = null,
        string? status = null,
        string? sort = null,
        int? page = null,
        int? pageSize = null)
    {
        var result = await handler.HandleAsync(new ListDataInstrumentsQuery(new DataInstrumentRequest(q, status, sort, page, pageSize)), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<InstrumentBars>, ProblemHttpResult>> GetBarsAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetInstrumentBarsQuery, Result<InstrumentBars>> handler,
        CancellationToken cancellationToken,
        string? timeframe = null,
        int? take = null)
    {
        var result = await handler.HandleAsync(new GetInstrumentBarsQuery(id, timeframe, take), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Accepted<DataJobView>, ProblemHttpResult>> StartAsync(
        HttpContext http,
        ICommandHandler<StartDataJobCommand, DataJobView> handler,
        CancellationToken cancellationToken,
        [FromBody] StartDataJobRequest? request = null)
    {
        var result = await handler.HandleAsync(new StartDataJobCommand(request ?? new StartDataJobRequest(), http.GetActor()), cancellationToken);
        return result.IsFailure
            ? ProblemResults.From(result.Error, http)
            : TypedResults.Accepted($"/api/data/jobs/{result.Value.Id}", result.Value);
    }

    private static async Task<Ok<IReadOnlyList<DataJobView>>> ListJobsAsync(
        IQueryHandler<ListDataJobsQuery, IReadOnlyList<DataJobView>> handler,
        CancellationToken cancellationToken,
        int? take = null) =>
        TypedResults.Ok(await handler.HandleAsync(new ListDataJobsQuery(take), cancellationToken));

    private static async Task<Results<Ok<DataJobView>, ProblemHttpResult>> GetJobAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetDataJobQuery, Result<DataJobView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetDataJobQuery(id), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
