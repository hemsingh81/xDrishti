using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The report reads over the stored simulation runs (spec P8-06, ADR 0100): GET only, one query model (<see cref="ReportQueryInput"/>, checked into <see cref="ReportQuery"/>; each endpoint declares only the fields it uses), the evidence block and the six honest states on every
/// answer. Nothing here writes, starts a job or touches an order. A net basis is requested by name but allowed only by the owner's stored confirmation of the charge sheet; no parameter, header or body can supply one.
/// </summary>
internal sealed class ReportsEndpoints : IEndpointModule
{
    private const string Common = " Takes runs (64-hex keys, comma separated; default the newest run of each strategy and universe), basis (gross or net) and the filters strategy, timeframe, side, style, exit (semicolon separated), basket, symbol, from, to. Strategy, symbol, exit and basket match exactly and case-sensitively.";

    private const string Lens = " lens is a comma separated list of up to three dimensions: basket, instrument, strategy, timeframe, side, style, exit, month, weekday, sector (as of today), hour (the entry hour in India time, intraday runs only) (regime and account are refused with the reason).";

    private const string Caps = " An answer larger than its cap (2,000 groups, 10,000 pivot cells, 200 distributions, 40 curves, 2,000 what-works cells) is refused at the first group past it, never cut; runs holding more than 500,000 closed trades are refused before any trade is read (422 reports.too_large). One report at a time is built; a request waits its turn.";

    private const string Errors = " 422 reports.invalid, reports.too_large, reports.net_not_confirmed, reports.net_unavailable, reports.dimension_unavailable or reports.double_count; 404 reports.not_found; 409 reports.run_overlap or reports.mixed_basis.";

    public void Map(IEndpointRouteBuilder api)
    {
        var reports = api.MapGroup("/reports").WithTags("Reports");

        reports.MapGet("/runs", RunsAsync)
            .WithName("ListReportRuns")
            .WithSummary("The stored simulation runs a report may use, newest first: strategy, universe, window, costed or gross, the style and exit labels and where they come from, closed and open-at-end trades, whether the default run set takes the run (and why not), and whether net figures are possible at all (unavailable with the reason until the owner has confirmed the stored charge sheet, on the Settings screen, tab Charge sheet). Takes no parameter. Read only.");

        reports.MapGet("/summary", SummaryAsync)
            .WithName("GetReportSummary")
            .WithSummary("The headline figures of the whole report population: trades, win rate, average win and loss R, expectancy, profit factor, total R, maximum drawdown, longest loss streak, holding sessions, the shrunk win rate with its credible lower bound. Each figure is a state (value, provisional, insufficient, unavailable, not_evaluable, empty) and carries a number only for a value; the evidence block says what stands behind it. Read only." + Common + Caps + Errors)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reports.MapGet("/groups", GroupsAsync)
            .WithName("GetReportGroups")
            .WithSummary("The report grouped by the lens, in a fixed order (a value that was not recorded last). A trade in several baskets appears under each, so basket totals are not additive (nonAdditive says so). Read only." + Lens + Common + Caps + Errors)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reports.MapGet("/pivot", PivotAsync)
            .WithName("GetReportPivot")
            .WithSummary("A pivot of the two dimensions of the lens (rows first; exactly two) by one figure (metric, snake case, default expectancy_r): every cell of the grid, empty where no trade fell and masked (insufficient, with its count) below the minimum sample, never a zero. Read only." + Lens + Common + Caps + Errors)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reports.MapGet("/equity", EquityAsync)
            .WithName("GetReportEquity")
            .WithSummary("The equity curve (running R at the end of each exit date) and the deepest drawdown of each date for each group of the lens. A group below the minimum sample has no curve, only the reason. Read only." + Lens + Common + Caps + Errors)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reports.MapGet("/distribution", DistributionAsync)
            .WithName("GetReportDistribution")
            .WithSummary("The distribution of R of each group of the lens in bins of half an R from -4 R to 6 R with an open bin at each end. A group below the minimum sample has no bins, only the reason. Read only." + Lens + Common + Caps + Errors)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reports.MapGet("/mae-mfe", MaeMfeAsync)
            .WithName("GetReportMaeMfe")
            .WithSummary("The excursions of each group of the lens as counts, never one point per trade: a grid of the maximum adverse excursion (0.25 R bins to 2 R, then 2 R or more) by the result R (half an R, -5 R to +5 R with a tail at each end), a histogram of the maximum favourable excursion (half an R to 5 R, then 5 R or more), how many trades reached +1, +2 and +3 R at some point, and the 90th percentile (nearest rank) of the adverse excursion of the winners. Trades with no recorded excursion are left out and counted apart (excursionN). A group below the minimum sample has no counts, only the reason. Read only." + Lens + Common + Caps + Errors)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reports.MapGet("/comparisons", ComparisonsAsync)
            .WithName("ListReportComparisons")
            .WithSummary("The stored comparisons of exits on identical entries, newest first: key, entries key, strategy, unit, common entries and each member run with the exit candidate it ran. Headers only; the figures come from the report endpoints over the member runs (runs=<member keys>, lens=exit). Takes no parameter. Read only.");

        reports.MapGet("/exit-selections", ExitSelectionsAsync)
            .WithName("ListReportExitSelections")
            .WithSummary("The stored out-of-sample exit choices (walk-forward, ADR 0054), newest first: per cell the chosen exit or why there is none, the default, the stability verdict with its reason and the chance-pass probability, the pooled out-of-sample figures (a number only for a value, insufficient below the minimum sample), the winner of each fold and the record's own caveats. Takes no parameter. Read only.");

        reports.MapGet("/insights", InsightsAsync)
            .WithName("GetReportInsights")
            .WithSummary("The eight auto-insights (best and weakest strategy, basket gap, side bias, best exit, weekday, regime, calibration) as fixed sentences. A figure is stated only when every group compared has at least 15 trades; otherwise the insight says why it states nothing. The insights compare fixed groupings, so there is no lens. Read only." + Common + Caps + Errors)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reports.MapGet("/what-works", WhatWorksAsync)
            .WithName("GetReportWhatWorks")
            .WithSummary("What works, in which basket, on which timeframe, with which exit: the cells (the dimensions of the lens, or basket, strategy, timeframe, side and exit by default) ranked by the credible lower bound of expectancy, the rest after them with the reason, how many cells were looked at, and the answer 'nothing has enough evidence yet' when no cell qualifies (a 200, not an error). Read only." + Lens + Common + Caps + Errors)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reports.MapGet("/benchmarks", BenchmarksAsync)
            .WithName("GetReportBenchmarks")
            .WithSummary("What simply holding each basket would have done over a window (equal weight, bought at the first close and held, price return, dividends not included) beside the benchmark index in force, each with the instruments behind it and every instrument left out with its reason (no_bars, starts_late, ends_early, discontinuity, bad_price, too_few_bars). Takes basket (ids, comma separated, at most 12), from, to (default the window of the default run set; window.source says which) and membersAsOf (default the India date of the newest run of the basket, else today; members as of that date, not as of each trading day). Fewer than 3 included instruments gives no number. Baskets share instruments, so nonAdditive is true and there is no total. Read only. 422 reports.invalid; 404 reports.not_found.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reports.MapGet("/plan-vs-actual", PlanVsActualAsync)
            .WithName("GetPlanVsActual")
            .WithSummary("Where the edge goes between the plan and the fills: the plan's R, then entry slippage, charges, a remainder 'not attributed' (exit slippage and anything else) and the realised R after the charges the sync recorded, as means over the tickets graded on fills; the counts of approved lines (with fills, uncertain, without fills), triggered tickets that had no fill (outside the chain), the parts not decomposed with the reason, per strategy, and the approvals by kind (owner, auto). Each figure below the minimum number of graded tickets is insufficient with no number. Takes from and to (planned sessions, at most 366 days apart). Read only. 422 reports.invalid.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        reports.MapGet("/trades", TradesAsync)
            .WithName("ListReportTrades")
            .WithSummary("A page of the closed trades the report counts, in the order of the exits (limit 1 to 1,000, default 100; cursor from the previous page, which belongs to that report and is refused for another): the very set the figures were computed from, open-at-end trades excluded. Every page recomputes the population. There is no lens. Read only." + Common + Caps + Errors)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reports.MapGet("/trades/{runKey}/{ordinal:int}", TradeReplayAsync)
            .WithName("GetTradeReplay")
            .WithSummary("One stored simulated trade on its own bars (the replay of the drill-down): the trade facts in adjusted prices, its legs, the costing (figures only for a charge sheet the owner confirmed), the bars of its window at the chosen timeframe built on the server from the 1-minute bars and adjusted for the corporate actions applied now, a check that the stored entry still fits the bars (ok, entry_mismatch, no_bars), the decision-time features stored for the trade (a number only where one is stored) and the evidence block. Takes openAtEnd (default false), timeframe (1m, 5m, 15m, 1h, 1D; default 5m), before and after (sessions around the trade: 0 to 3, or 0 to 60 for 1D). A window above 5,000 bars is refused, never thinned. Read only. 422 reports.invalid or reports.window_too_large; 404 reports.not_found.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
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
        [AsParameters] ReportFilterParams input,
        HttpContext http,
        IQueryHandler<GetReportSummaryQuery, Result<SummaryView>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(input.ToInput(), http, q => handler.HandleAsync(new GetReportSummaryQuery(q), cancellationToken));

    private static Task<Results<Ok<GroupsView>, ProblemHttpResult>> GroupsAsync(
        [AsParameters] ReportLensParams input,
        HttpContext http,
        IQueryHandler<GetReportGroupsQuery, Result<GroupsView>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(input.ToInput(), http, q => handler.HandleAsync(new GetReportGroupsQuery(q), cancellationToken));

    private static Task<Results<Ok<PivotView>, ProblemHttpResult>> PivotAsync(
        [AsParameters] ReportPivotParams input,
        HttpContext http,
        IQueryHandler<GetReportPivotQuery, Result<PivotView>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(input.ToInput(), http, q => handler.HandleAsync(new GetReportPivotQuery(q), cancellationToken));

    private static Task<Results<Ok<EquityView>, ProblemHttpResult>> EquityAsync(
        [AsParameters] ReportLensParams input,
        HttpContext http,
        IQueryHandler<GetReportEquityQuery, Result<EquityView>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(input.ToInput(), http, q => handler.HandleAsync(new GetReportEquityQuery(q), cancellationToken));

    private static Task<Results<Ok<DistributionView>, ProblemHttpResult>> DistributionAsync(
        [AsParameters] ReportLensParams input,
        HttpContext http,
        IQueryHandler<GetReportDistributionQuery, Result<DistributionView>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(input.ToInput(), http, q => handler.HandleAsync(new GetReportDistributionQuery(q), cancellationToken));

    private static Task<Results<Ok<ReportMaeMfeView>, ProblemHttpResult>> MaeMfeAsync(
        [AsParameters] ReportLensParams input,
        HttpContext http,
        IQueryHandler<GetReportMaeMfeQuery, Result<ReportMaeMfeView>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(input.ToInput(), http, q => handler.HandleAsync(new GetReportMaeMfeQuery(q), cancellationToken));

    private static async Task<Results<Ok<ReportComparisonsView>, ProblemHttpResult>> ComparisonsAsync(
        HttpContext http,
        IQueryHandler<ListReportComparisonsQuery, Result<ReportComparisonsView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListReportComparisonsQuery(), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ReportExitSelectionsView>, ProblemHttpResult>> ExitSelectionsAsync(
        HttpContext http,
        IQueryHandler<ListReportExitSelectionsQuery, Result<ReportExitSelectionsView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListReportExitSelectionsQuery(), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static Task<Results<Ok<InsightsView>, ProblemHttpResult>> InsightsAsync(
        [AsParameters] ReportFilterParams input,
        HttpContext http,
        IQueryHandler<GetReportInsightsQuery, Result<InsightsView>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(input.ToInput(), http, q => handler.HandleAsync(new GetReportInsightsQuery(q), cancellationToken));

    private static Task<Results<Ok<WhatWorksView>, ProblemHttpResult>> WhatWorksAsync(
        [AsParameters] ReportLensParams input,
        HttpContext http,
        IQueryHandler<GetReportWhatWorksQuery, Result<WhatWorksView>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(input.ToInput(), http, q => handler.HandleAsync(new GetReportWhatWorksQuery(q), cancellationToken));

    private static async Task<Results<Ok<ReportBenchmarksView>, ProblemHttpResult>> BenchmarksAsync(
        [AsParameters] BenchmarkParams input,
        HttpContext http,
        IQueryHandler<GetReportBenchmarksQuery, Result<ReportBenchmarksView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetReportBenchmarksQuery(input.Basket, input.From, input.To, input.MembersAsOf), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<PlanVsActualView>, ProblemHttpResult>> PlanVsActualAsync(
        DateOnly? from,
        DateOnly? to,
        HttpContext http,
        IQueryHandler<GetPlanVsActualQuery, Result<PlanVsActualView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetPlanVsActualQuery(from, to), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static Task<Results<Ok<TradesView>, ProblemHttpResult>> TradesAsync(
        [AsParameters] ReportTradesParams input,
        HttpContext http,
        IQueryHandler<PageReportTradesQuery, Result<TradesView>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(input.ToInput(), http, q => handler.HandleAsync(new PageReportTradesQuery(q), cancellationToken));

    private static async Task<Results<Ok<TradeReplayView>, ProblemHttpResult>> TradeReplayAsync(
        string runKey,
        int ordinal,
        bool? openAtEnd,
        string? timeframe,
        int? before,
        int? after,
        HttpContext http,
        IQueryHandler<GetTradeReplayQuery, Result<TradeReplayView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetTradeReplayQuery(runKey, ordinal, openAtEnd ?? false, timeframe, before, after), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>Checks the query against its whitelist, then runs the use case; every refusal of the engine passes through with its code.</summary>
    private static async Task<Results<Ok<T>, ProblemHttpResult>> ReadAsync<T>(ReportQueryInput input, HttpContext http, Func<ReportQuery, ValueTask<Result<T>>> run)
    {
        var query = ReportQuery.Parse(input);
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
