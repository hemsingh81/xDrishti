using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The report reads over the stored simulation runs (spec P8-06, ADR 0100): GET only, one query model (<see cref="ReportQueryParams"/>, checked into <see cref="ReportQuery"/>),the evidence block and the six honest states on every answer. Nothing here writes, starts a job or touches an order.
/// A net basis is requested by name but allowed only by the owner's stored confirmation of the charge sheet; no parameter, header or body can supply one.
/// </summary>
internal sealed class ReportsEndpoints : IEndpointModule
{
    private const string QueryNote = " Takes the one report query: runs (64-hex keys, comma separated; default the newest run of each strategy and universe), basis (gross or net), lens (comma separated dimensions: basket, instrument, strategy, timeframe, side, style, exit, month, weekday; regime and account are refused with the reason), the filters strategy, timeframe, side, style, exit (semicolon separated), basket, symbol, from, to. An answer larger than its cap (2,000 groups, 10,000 pivot cells, 200 distributions, 40 curves, 2,000 what-works cells) is refused with the count, never cut. 422 reports.invalid, 404 reports.not_found, 409 reports.run_overlap or reports.mixed_basis, 422 reports.net_not_confirmed, reports.net_unavailable, reports.dimension_unavailable or reports.double_count.";

    public void Map(IEndpointRouteBuilder api)
    {
        var reports = api.MapGroup("/reports").WithTags("Reports");

        reports.MapGet("/runs", RunsAsync)
            .WithName("ListReportRuns")
            .WithSummary("The stored simulation runs a report may use, newest first: strategy, universe, window, costed or gross, the style and exit labels and where they come from, closed and open-at-end trades, whether the default run set takes the run (and why not), and whether net figures are possible at all (unavailable with the reason until the owner has confirmed the stored charge sheet). Read only.");

        reports.MapGet("/summary", SummaryAsync)
            .WithName("GetReportSummary")
            .WithSummary("The headline figures of the whole report population: trades, win rate, average win and loss R, expectancy, profit factor, total R, maximum drawdown, longest loss streak, holding sessions, the shrunk win rate with its credible lower bound. Each figure is a state (value, provisional, insufficient, unavailable, not_evaluable, empty) and carries a number only for a value; the evidence block says what stands behind it. Read only." + QueryNote)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reports.MapGet("/groups", GroupsAsync)
            .WithName("GetReportGroups")
            .WithSummary("The report grouped by the lens, in a fixed order (a value that was not recorded last). A trade in several baskets appears under each, so basket totals are not additive (nonAdditive says so). Read only." + QueryNote)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reports.MapGet("/pivot", PivotAsync)
            .WithName("GetReportPivot")
            .WithSummary("A pivot of the two dimensions of the lens (rows first) by one figure (metric, snake case, default expectancy_r): every cell of the grid, empty where no trade fell and masked (insufficient, with its count) below the minimum sample, never a zero. Read only." + QueryNote)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reports.MapGet("/equity", EquityAsync)
            .WithName("GetReportEquity")
            .WithSummary("The equity curve (running R) and drawdown of each group of the lens, one point per exit date. A group below the minimum sample has no curve, only the reason. At most 40 curves; a lens with more is refused. Read only." + QueryNote)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reports.MapGet("/distribution", DistributionAsync)
            .WithName("GetReportDistribution")
            .WithSummary("The distribution of R of each group of the lens in bins of half an R from -4 R to 6 R with an open bin at each end. A group below the minimum sample has no bins, only the reason. Read only." + QueryNote)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reports.MapGet("/insights", InsightsAsync)
            .WithName("GetReportInsights")
            .WithSummary("The eight auto-insights (best and weakest strategy, basket gap, side bias, best exit, weekday, regime, calibration) as fixed sentences. A figure is stated only when every group compared has at least 15 trades; otherwise the insight says why it states nothing. Read only." + QueryNote)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reports.MapGet("/what-works", WhatWorksAsync)
            .WithName("GetReportWhatWorks")
            .WithSummary("What works, in which basket, on which timeframe, with which exit: the cells (the dimensions of the lens, or basket, strategy, timeframe, side and exit by default) ranked by the credible lower bound of expectancy, the rest after them with the reason, how many cells were looked at, and the answer 'nothing has enough evidence yet' when no cell qualifies (a 200, not an error). Read only." + QueryNote)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reports.MapGet("/trades", TradesAsync)
            .WithName("ListReportTrades")
            .WithSummary("A page of the closed trades the report counts, in the order of the exits (limit 1 to 1,000, default 100; cursor from the previous page): the very set the figures were computed from, open-at-end trades excluded. Read only." + QueryNote)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<RunsView>, ProblemHttpResult>> RunsAsync(
        HttpContext http,
        IQueryHandler<ListReportRunsQuery, Result<RunsView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListReportRunsQuery(), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static Task<Results<Ok<SummaryView>, ProblemHttpResult>> SummaryAsync(
        [AsParameters] ReportQueryParams input,
        HttpContext http,
        IQueryHandler<GetReportSummaryQuery, Result<SummaryView>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(input, http, q => handler.HandleAsync(new GetReportSummaryQuery(q), cancellationToken));

    private static Task<Results<Ok<GroupsView>, ProblemHttpResult>> GroupsAsync(
        [AsParameters] ReportQueryParams input,
        HttpContext http,
        IQueryHandler<GetReportGroupsQuery, Result<GroupsView>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(input, http, q => handler.HandleAsync(new GetReportGroupsQuery(q), cancellationToken));

    private static Task<Results<Ok<PivotView>, ProblemHttpResult>> PivotAsync(
        [AsParameters] ReportQueryParams input,
        HttpContext http,
        IQueryHandler<GetReportPivotQuery, Result<PivotView>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(input, http, q => handler.HandleAsync(new GetReportPivotQuery(q), cancellationToken));

    private static Task<Results<Ok<EquityView>, ProblemHttpResult>> EquityAsync(
        [AsParameters] ReportQueryParams input,
        HttpContext http,
        IQueryHandler<GetReportEquityQuery, Result<EquityView>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(input, http, q => handler.HandleAsync(new GetReportEquityQuery(q), cancellationToken));

    private static Task<Results<Ok<DistributionView>, ProblemHttpResult>> DistributionAsync(
        [AsParameters] ReportQueryParams input,
        HttpContext http,
        IQueryHandler<GetReportDistributionQuery, Result<DistributionView>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(input, http, q => handler.HandleAsync(new GetReportDistributionQuery(q), cancellationToken));

    private static Task<Results<Ok<InsightsView>, ProblemHttpResult>> InsightsAsync(
        [AsParameters] ReportQueryParams input,
        HttpContext http,
        IQueryHandler<GetReportInsightsQuery, Result<InsightsView>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(input, http, q => handler.HandleAsync(new GetReportInsightsQuery(q), cancellationToken));

    private static Task<Results<Ok<WhatWorksView>, ProblemHttpResult>> WhatWorksAsync(
        [AsParameters] ReportQueryParams input,
        HttpContext http,
        IQueryHandler<GetReportWhatWorksQuery, Result<WhatWorksView>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(input, http, q => handler.HandleAsync(new GetReportWhatWorksQuery(q), cancellationToken));

    private static Task<Results<Ok<TradesView>, ProblemHttpResult>> TradesAsync(
        [AsParameters] ReportQueryParams input,
        HttpContext http,
        IQueryHandler<PageReportTradesQuery, Result<TradesView>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(input, http, q => handler.HandleAsync(new PageReportTradesQuery(q), cancellationToken));

    /// <summary>Checks the query against its whitelist, then runs the use case; every refusal of the engine passes through with its code.</summary>
    private static async Task<Results<Ok<T>, ProblemHttpResult>> ReadAsync<T>(ReportQueryParams input, HttpContext http, Func<ReportQuery, ValueTask<Result<T>>> run)
    {
        var query = ReportQuery.Parse(input.ToInput());
        if (query.IsFailure)
        {
            return Problem(query.Error, http);
        }

        var result = await run(query.Value);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422, as in the Lab and the plans; a missing run is 404, an overlap or a mixed basis 409, through the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Kind == ErrorKind.Validation && error.Code.StartsWith("reports.", StringComparison.Ordinal)
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
