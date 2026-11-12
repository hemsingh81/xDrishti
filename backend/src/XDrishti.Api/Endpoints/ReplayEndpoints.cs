using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Replay.Job;
using XDrishti.Application.Replay.Reads;
using XDrishti.Application.Replay.Sweeps;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The system replay (SR-01 part C, ADR 0272): start one replay (or a frozen and a learning pair), read the runs while they run, cancel. The owner's session is needed for every route; a start and a cancel also need the CSRF token. A replay is research only: it places no order and changes no plan, ledger,
/// statistic, model or setting. Every answer carries the hindsight labels and the costs label.
/// </summary>
internal sealed class ReplayEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/replay").WithTags("Replay");

        group.MapPost("/runs", StartAsync)
            .WithName("StartReplay")
            .WithSummary("Start a replay of the whole system from a chosen date: body from, to (default the last readable session before the hold-out and its embargo), scope {basketIds, instrumentIds} (empty: the stored auto scope, its baskets read as of each session; at most 200 baskets and 2000 instruments), capital (default 1000000), riskProfileId (default the seeded standard profile), costs (null or gross, else the id of a stored charge sheet), versions (start or asOf, default start: today's active versions, labelled hindsight), modes (frozen and/or learning, default frozen; both give two linked runs and a pairId), filtersWithoutHistory and includeResearchCells (both default false; each is a labelled hindsight), baseline (default false; frozen mode only: also runs the no-selection baseline, one request and one pairId, compared by GET /pairs/{pairId} as selectionVsBaseline). 202 with runIds and pairId. 422 replay.invalid (bad input, an empty snapshot, the memory cap); 409 replay.busy (a replay is active, or the 1-minute backfill is incomplete), replay.holdout (to reaches the hold-out; the last allowed date is in the message)")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/runs", ListAsync)
            .WithName("ListReplayRuns")
            .WithSummary("The replay runs, newest first (skip, take 1 to 200, default 50): id, created, range, mode, state, completeness, final equity, max drawdown, trial number, costs label, hindsight labels. Read only.")
            .RequireAuthorization(ServiceReadAuthentication.AssistantEvidenceReadPolicy)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/runs/{id:guid}", GetAsync)
            .WithName("GetReplayRun")
            .WithSummary("One replay run, readable while it runs: the hindsight labels first, completeness (complete, sessions done of total, the as-of session), the costs label (\"before costs (charge sheet not filled, ISS-25)\" when gross), trial number, input and result hashes, the settings snapshot, and the figures so far (return, CAGR, max drawdown, Sharpe of daily returns, exposure, trades, hit rate, mean R, contribution per strategy). Read only.")
            .RequireAuthorization(ServiceReadAuthentication.AssistantEvidenceReadPolicy)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/runs/{id:guid}/equity", EquityAsync)
            .WithName("GetReplayEquity")
            .WithSummary("The daily equity, drawdown, cash line and the benchmark (Nifty 500, or the index chosen for the basket reports) rebased to the capital, between from and to (dates, optional). Read only.")
            .RequireAuthorization(ServiceReadAuthentication.AssistantEvidenceReadPolicy)
            .ProducesRejections(conflict: false);

        group.MapGet("/runs/{id:guid}/sessions", SessionsAsync)
            .WithName("ListReplaySessions")
            .WithSummary("One row per stored session between from and to (dates, optional): candidates, taken, blocked by reason, the day's result, equity. Read only.")
            .ProducesRejections(conflict: false);

        group.MapGet("/runs/{id:guid}/sessions/{session}", SessionAsync)
            .WithName("GetReplaySession")
            .WithSummary("The full record of one session (yyyy-MM-dd): candidates, ranked list, taken, blocked with reasons, fills, the book open at the close. 404 replay.session_not_found while it is not stored. Read only.")
            .ProducesRejections(conflict: false);

        group.MapGet("/runs/{id:guid}/trades", TradesAsync)
            .WithName("ListReplayTrades")
            .WithSummary("The trades of the replay book in booking order, with the strategy, entry, exit, costs and R of each. Read only.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/pairs/{pairId:guid}", PairAsync)
            .WithName("GetReplayPair")
            .WithSummary("The frozen-versus-learning comparison of two linked runs, computed on read from their stored equity (paired day bootstrap): learning_better, frozen_better, no_measurable_difference or too_little_history, with n and the probabilities. No answer (with the reason) while a run is not complete. Read only.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/runs/{id:guid}/trades/{tradeNo:long}/chart", TradeChartAsync)
            .WithName("GetReplayTradeChart")
            .WithSummary("One replay trade on its bars (query timeframe 1m, 5m, 15m, 1h or 1D, default 5m; before and after in sessions): the P8-11 chart block (bars, coherence) and the markers of entry, stop and exit, from the session before the entry to the session after the exit through the hold-out clamp (a window past it is cut and says so in limits). The exit price is derived from the gross result. At most 5,000 bars. 404 replay.not_found; 422 bad query or too many bars. Read only.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/sweeps", StartSweepAsync)
            .WithName("StartReplaySweep")
            .WithSummary("Start a start-date sweep: body starts (2 to 12 distinct dates), to (the shared end, default the last readable session), scope, capital, riskProfileId, costs, versions, filtersWithoutHistory, includeResearchCells as for a start; frozen mode, selection arm. One queued run per start, run one at a time in date order; each is one trial-ledger look (\"this sweep adds N replay looks\"). 202 with sweepId and runIds. 422 replay.invalid (count, duplicates, a start within 126 sessions of the end or without cell statistics as of it: each bad date is named); 409 replay.busy (any replay is active), replay.holdout")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/sweeps/{id:guid}", SweepAsync)
            .WithName("GetReplaySweep")
            .WithSummary("A sweep: hindsight labels, looks added, one row per start in date order (run id, state, CAGR, max drawdown, final equity, trades, beats the benchmark) and the spread over the completed starts (min, median and max of CAGR and of max drawdown, the share with CAGR above 0, the share beating the benchmark), and complete. Read only.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/sweeps/{id:guid}/cancel", CancelSweepAsync)
            .WithName("CancelReplaySweep")
            .WithSummary("Stop a sweep: the run in flight after its session and every queued run. The trial-ledger rows stay. 202 with how many runs were stopped. 404 replay.sweep_not_found; 409 replay.not_active when it has finished.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/runs/{id:guid}/cancel", CancelAsync)
            .WithName("CancelReplay")
            .WithSummary("Stop a queued or running replay after the session in flight; the finished sessions are kept and the run reads cancelled. 202 with the new state (cancelled or cancelling). 404 replay.not_found; 409 replay.not_active when it has finished.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Results<Accepted<StartedReplay>, ProblemHttpResult>> StartAsync(ReplayStartBody body, HttpContext http, ICommandHandler<StartReplayRequest, StartedReplay> handler, CancellationToken cancellationToken)
    {
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var costs = string.IsNullOrWhiteSpace(body.Costs) || string.Equals(body.Costs, "gross", StringComparison.OrdinalIgnoreCase) ? null : body.Costs;
        var result = await handler.HandleAsync(new StartReplayRequest(body.From, body.To, body.Scope?.BasketIds, body.Scope?.InstrumentIds, body.Capital, body.RiskProfileId, costs, body.Versions, body.Modes, body.FiltersWithoutHistory, body.IncludeResearchCells, body.Baseline), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Accepted("/api/replay/runs/" + result.Value.RunIds[0].ToString("D"), result.Value);
    }

    private static Task<Results<Ok<IReadOnlyList<ReplayRunListItem>>, ProblemHttpResult>> ListAsync(HttpContext http, IQueryHandler<ListReplayRunsQuery, Result<IReadOnlyList<ReplayRunListItem>>> handler, CancellationToken cancellationToken, int skip = 0, int take = 50) =>
        ReadAsync(http, handler.HandleAsync(new ListReplayRunsQuery(skip, take), cancellationToken));

    private static Task<Results<Ok<ReplayRunView>, ProblemHttpResult>> GetAsync(Guid id, HttpContext http, IQueryHandler<GetReplayRunQuery, Result<ReplayRunView>> handler, CancellationToken cancellationToken) =>
        ReadAsync(http, handler.HandleAsync(new GetReplayRunQuery(id), cancellationToken));

    private static Task<Results<Ok<ReplayEquityView>, ProblemHttpResult>> EquityAsync(Guid id, DateOnly? from, DateOnly? to, HttpContext http, IQueryHandler<GetReplayEquityQuery, Result<ReplayEquityView>> handler, CancellationToken cancellationToken) =>
        ReadAsync(http, handler.HandleAsync(new GetReplayEquityQuery(id, from, to), cancellationToken));

    private static Task<Results<Ok<ReplaySessionListView>, ProblemHttpResult>> SessionsAsync(Guid id, DateOnly? from, DateOnly? to, HttpContext http, IQueryHandler<ListReplaySessionsQuery, Result<ReplaySessionListView>> handler, CancellationToken cancellationToken) =>
        ReadAsync(http, handler.HandleAsync(new ListReplaySessionsQuery(id, from, to), cancellationToken));

    private static Task<Results<Ok<ReplaySessionView>, ProblemHttpResult>> SessionAsync(Guid id, DateOnly session, HttpContext http, IQueryHandler<GetReplaySessionQuery, Result<ReplaySessionView>> handler, CancellationToken cancellationToken) =>
        ReadAsync(http, handler.HandleAsync(new GetReplaySessionQuery(id, session), cancellationToken));

    private static Task<Results<Ok<ReplayTradeListView>, ProblemHttpResult>> TradesAsync(Guid id, HttpContext http, IQueryHandler<ListReplayTradesQuery, Result<ReplayTradeListView>> handler, CancellationToken cancellationToken) =>
        ReadAsync(http, handler.HandleAsync(new ListReplayTradesQuery(id), cancellationToken));

    private static Task<Results<Ok<ReplayPairView>, ProblemHttpResult>> PairAsync(Guid pairId, HttpContext http, IQueryHandler<GetReplayPairQuery, Result<ReplayPairView>> handler, CancellationToken cancellationToken) =>
        ReadAsync(http, handler.HandleAsync(new GetReplayPairQuery(pairId), cancellationToken));

    private static Task<Results<Ok<ReplayTradeChartView>, ProblemHttpResult>> TradeChartAsync(Guid id, long tradeNo, string? timeframe, int? before, int? after, HttpContext http, IQueryHandler<GetReplayTradeChartQuery, Result<ReplayTradeChartView>> handler, CancellationToken cancellationToken) =>
        ReadAsync(http, handler.HandleAsync(new GetReplayTradeChartQuery(id, tradeNo, timeframe, before, after), cancellationToken));

    private static Task<Results<Ok<ReplaySweepView>, ProblemHttpResult>> SweepAsync(Guid id, HttpContext http, IQueryHandler<GetReplaySweepQuery, Result<ReplaySweepView>> handler, CancellationToken cancellationToken) =>
        ReadAsync(http, handler.HandleAsync(new GetReplaySweepQuery(id), cancellationToken));

    private static async Task<Results<Accepted<StartedReplay>, ProblemHttpResult>> StartSweepAsync(ReplaySweepStartBody body, HttpContext http, ICommandHandler<StartReplaySweepRequest, StartedReplay> handler, CancellationToken cancellationToken)
    {
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var costs = string.IsNullOrWhiteSpace(body.Costs) || string.Equals(body.Costs, "gross", StringComparison.OrdinalIgnoreCase) ? null : body.Costs;
        var result = await handler.HandleAsync(new StartReplaySweepRequest(body.Starts ?? [], body.To, body.Scope?.BasketIds, body.Scope?.InstrumentIds, body.Capital, body.RiskProfileId, costs, body.Versions, body.FiltersWithoutHistory, body.IncludeResearchCells), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Accepted("/api/replay/sweeps/" + result.Value.SweepId!.Value.ToString("D"), result.Value);
    }

    private static async Task<Results<Accepted<ReplaySweepCancelView>, ProblemHttpResult>> CancelSweepAsync(Guid id, HttpContext http, ICommandHandler<CancelReplaySweepCommand, int> handler, CancellationToken cancellationToken)
    {
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new CancelReplaySweepCommand(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Accepted("/api/replay/sweeps/" + id.ToString("D"), new ReplaySweepCancelView(result.Value));
    }

    private static async Task<Results<Accepted<ReplayCancelView>, ProblemHttpResult>> CancelAsync(Guid id, HttpContext http, ICommandHandler<CancelReplayCommand, string> handler, CancellationToken cancellationToken)
    {
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new CancelReplayCommand(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Accepted("/api/replay/runs/" + id.ToString("D"), new ReplayCancelView(result.Value));
    }

    private static async Task<Results<Ok<T>, ProblemHttpResult>> ReadAsync<T>(HttpContext http, ValueTask<Result<T>> pending)
    {
        var result = await pending;
        http.Response.Headers.CacheControl = "no-store"; // a run is read while it changes
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422 (replay.invalid, replay.pair_misaligned); everything else is the shared mapping (409 busy and holdout, 404).</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Kind == ErrorKind.Validation
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
