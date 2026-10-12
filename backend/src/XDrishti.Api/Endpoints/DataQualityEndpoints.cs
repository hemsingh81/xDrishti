using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Quality;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The data quality checks (ADR 0017 §4, P1-14a): a run over the last closed sessions of every tracked instrument — completeness against the trading calendar, bar sanity, spikes, the derived timeframes
/// recomputed from the 1-minute bars, freshness — with a verdict (pass, warning, fail) and a score per instrument, the history of runs, the gate for the nightly plan and the fix actions (fetch again,
/// rebuild the aggregates, each followed by checking the instrument again). Runs and fix actions are data jobs (one at a time with every other data job), so progress is read from
/// <c>/api/data/jobs/{id}</c>. All routes need a session; starting one also needs the CSRF token; none needs a step-up. Part of the market module.
/// </summary>
internal sealed class DataQualityEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/data/quality").WithTags("Data quality");

        group.MapGet("/overview", GetOverviewAsync)
            .WithName("GetDataQualityOverview")
            .WithSummary("The latest run over every instrument, the counts (checked, passed, warnings, failed, never checked) and the mean score of the current results, the gate, the window of sessions and the limits in force");

        group.MapGet("/instruments", ListInstrumentsAsync)
            .WithName("ListDataQualityInstruments")
            .WithSummary("The tracked instruments with bars and the current result of their checks: q, status (pass|warning|fail|notChecked), sort (symbol|name|status|score|coverage|barsChecked|missingMinutes|checkedAt, '-' for descending), page, pageSize (max 200)")
            .ProducesValidationProblem();

        group.MapGet("/instruments/{id:guid}", GetInstrumentAsync)
            .WithName("GetDataQualityInstrument")
            .WithSummary("Every check of one instrument, its missing minutes (at most 500 listed, with the total) and the stored buckets of the window next to the ones recomputed from the 1-minute bars now: differencesOnly, timeframe (5m|10m|15m|30m|1h|1D|1W|1M)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/runs", ListRunsAsync)
            .WithName("ListDataQualityRuns")
            .WithSummary("The most recent quality runs, newest first (take 1 to 100, default 20)");

        group.MapGet("/gate", GetGateAsync)
            .WithName("GetDataQualityGate")
            .WithSummary("The fail-closed gate for the nightly plan: blocked when no finished run exists, the last finished run failed or is older than 3 trading days, or more than Data:Quality:MaxFailedPercent of the checked instruments fail (a running run is no verdict); ok when not blocked and none fails; reasons say why");

        group.MapPost("/run", RunAsync)
            .WithName("StartDataQualityRun")
            .WithSummary("Run the checks in the background (a data job with the step quality): instrumentId limits it to one instrument. 202 with the job; 409 data.job_running while any data job runs, 404 data.instrument_not_found")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/instruments/{id:guid}/refetch", RefetchAsync)
            .WithName("RefetchDataQualityInstrument")
            .WithSummary("Fix action: fetch the instrument's missing bars from Dhan again (gap days, including unfillable ones), rebuild its aggregates and check it again, as one data job. 202 with the job; 409 data.job_running, data.dhan_not_connected or data.dhan_not_available; 404 data.instrument_not_found")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/instruments/{id:guid}/refresh-aggregates", RefreshAggregatesAsync)
            .WithName("RefreshDataQualityAggregates")
            .WithSummary("Fix action: rebuild the instrument's derived timeframes over the window from its 1-minute bars and check it again, as one data job. 202 with the job; 409 data.job_running; 404 data.instrument_not_found")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Ok<QualityOverview>> GetOverviewAsync(
        IQueryHandler<GetQualityOverviewQuery, QualityOverview> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetQualityOverviewQuery(), cancellationToken));

    private static async Task<Results<Ok<QualityInstrumentPage>, ProblemHttpResult>> ListInstrumentsAsync(
        HttpContext http,
        IQueryHandler<ListQualityInstrumentsQuery, Result<QualityInstrumentPage>> handler,
        CancellationToken cancellationToken,
        [FromQuery(Name = "q")] string? q = null,
        string? status = null,
        string? sort = null,
        int? page = null,
        int? pageSize = null)
    {
        var result = await handler.HandleAsync(new ListQualityInstrumentsQuery(new QualityInstrumentRequest(q, status, sort, page, pageSize)), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<QualityInstrumentDetail>, ProblemHttpResult>> GetInstrumentAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetQualityInstrumentQuery, Result<QualityInstrumentDetail>> handler,
        CancellationToken cancellationToken,
        bool? differencesOnly = null,
        string? timeframe = null)
    {
        var result = await handler.HandleAsync(new GetQualityInstrumentQuery(id, differencesOnly, timeframe), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Ok<IReadOnlyList<QualityRunView>>> ListRunsAsync(
        IQueryHandler<ListQualityRunsQuery, IReadOnlyList<QualityRunView>> handler,
        CancellationToken cancellationToken,
        int? take = null) =>
        TypedResults.Ok(await handler.HandleAsync(new ListQualityRunsQuery(take), cancellationToken));

    private static async Task<Ok<QualityGateView>> GetGateAsync(
        IQueryHandler<GetQualityGateQuery, QualityGateView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetQualityGateQuery(), cancellationToken));

    private static async Task<Results<Accepted<DataJobView>, ProblemHttpResult>> RunAsync(
        HttpContext http,
        ICommandHandler<StartQualityRunCommand, DataJobView> handler,
        CancellationToken cancellationToken,
        [FromBody] StartQualityRunRequest? request = null)
    {
        var result = await handler.HandleAsync(new StartQualityRunCommand(request ?? new StartQualityRunRequest(), http.GetActor()), cancellationToken);
        return Started(result, http);
    }

    private static async Task<Results<Accepted<DataJobView>, ProblemHttpResult>> RefetchAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<RefetchQualityInstrumentCommand, DataJobView> handler,
        CancellationToken cancellationToken) =>
        Started(await handler.HandleAsync(new RefetchQualityInstrumentCommand(id, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Accepted<DataJobView>, ProblemHttpResult>> RefreshAggregatesAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<RefreshQualityAggregatesCommand, DataJobView> handler,
        CancellationToken cancellationToken) =>
        Started(await handler.HandleAsync(new RefreshQualityAggregatesCommand(id, http.GetActor()), cancellationToken), http);

    private static Results<Accepted<DataJobView>, ProblemHttpResult> Started(Result<DataJobView> result, HttpContext http) =>
        result.IsFailure
            ? ProblemResults.From(result.Error, http)
            : TypedResults.Accepted($"/api/data/jobs/{result.Value.Id}", result.Value);
}
