using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Replay.Job;
using XDrishti.Application.Replay.Reads;
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
            .WithSummary("Start a replay of the whole system from a chosen date: body from, to (default the last readable session before the hold-out and its embargo), scope {basketIds, instrumentIds} (empty: the stored auto scope with its baskets as of from), capital (default 1000000), riskProfileId (default the seeded standard profile), costs (null or gross, else the id of a stored charge sheet), versions (start or asOf, default asOf), modes (frozen and/or learning, default frozen; both give two linked runs and a pairId), filtersWithoutHistory and includeResearchCells (both default false; each is a labelled hindsight). 202 with runIds and pairId. 422 replay.invalid (bad input, an empty snapshot, the memory cap); 409 replay.busy (a replay is active, or the 1-minute backfill is incomplete), replay.holdout (to reaches the hold-out; the last allowed date is in the message)")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/runs", ListAsync)
            .WithName("ListReplayRuns")
            .WithSummary("The replay runs, newest first (skip, take 1 to 200, default 50): id, created, range, mode, state, completeness, final equity, max drawdown, trial number, costs label, hindsight labels. Read only.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/runs/{id:guid}", GetAsync)
            .WithName("GetReplayRun")
            .WithSummary("One replay run, readable while it runs: the hindsight labels first, completeness (complete, sessions done of total, the as-of session), the costs label (\"before costs (charge sheet not filled, ISS-25)\" when gross), trial number, input and result hashes, the settings snapshot, and the figures so far (return, CAGR, max drawdown, Sharpe of daily returns, exposure, trades, hit rate, mean R, contribution per strategy). Read only.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/runs/{id:guid}/equity", EquityAsync)
            .WithName("GetReplayEquity")
            .WithSummary("The daily equity, drawdown, cash line and the benchmark (Nifty 500, or the index chosen for the basket reports) rebased to the capital, between from and to (dates, optional). Read only.")
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
            .WithSummary("The trades of the replay book in booking order, each with a barsRoute to the instrument's day bars where its symbol names one instrument. Read only.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/pairs/{pairId:guid}", PairAsync)
            .WithName("GetReplayPair")
            .WithSummary("The frozen-versus-learning comparison of two linked runs, computed on read from their stored equity (paired day bootstrap): learning_better, frozen_better, no_measurable_difference or too_little_history, with n and the probabilities. No answer (with the reason) while a run is not complete. Read only.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

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
        var result = await handler.HandleAsync(new StartReplayRequest(body.From, body.To, body.Scope?.BasketIds, body.Scope?.InstrumentIds, body.Capital, body.RiskProfileId, costs, body.Versions, body.Modes, body.FiltersWithoutHistory, body.IncludeResearchCells), cancellationToken);
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
