using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Portfolio;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The portfolio value history, the benchmark and the two track actions of the Portfolio screen (ADR 0020, P2-07). Everything is computed on read from the lots and the stored day bars. Reading needs a session; changing the benchmark and the track actions
/// also need the CSRF token, none needs a step-up (public market data and the owner's own choice). Percentages are percent numbers (12.5 = 12.5 %).
/// </summary>
internal sealed class PortfolioHistoryEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/portfolio").WithTags("Portfolio");

        group.MapGet("/history", GetHistoryAsync)
            .WithName("GetPortfolioHistory")
            .WithSummary("Value history of the lots of a scope (accountId, bucketId; all accounts that count in totals when neither is given) from the lots and the stored day bars adjusted for splits and bonuses: points {date, value, invested, benchmarkValue (the same cash flows in the benchmark index, null when unavailable), nav, benchmarkNav} (NAV starts at 100 and ignores cash flows), coverage {holdings, covered, unpriced, lateStart}, partial, benchmark {state ok|unavailable, instrumentId, symbol, name, tracked, hasBars, reason}, maxDrawdown and benchmarkMaxDrawdown {pct, from, to} (a fall of pct %), firstDate, lastPriceAt (set when the last point is today's live one) and resolution. from/to yyyy-MM-dd; resolution day|week|month (last trading day of the period; default the finest with at most 2,000 points, 400 portfolio.invalid when a given one gives more). An unknown account or bucket gives an empty history")
            .ProducesValidationProblem();

        group.MapGet("/benchmark", GetBenchmarkAsync)
            .WithName("GetPortfolioBenchmark")
            .WithSummary("The benchmark index the portfolio is compared with (Nifty 50 until another is chosen): instrumentId, symbol, name, isDefault, tracked, hasBars; all null when the instrument master has no such index yet");

        group.MapPut("/benchmark", SetBenchmarkAsync)
            .WithName("SetPortfolioBenchmark")
            .WithSummary("Choose the benchmark index: body {instrumentId} of any index of the instrument master that is still listed; 400 portfolio.invalid for no id, an unknown id, a non-index or a retired index. Audited; the cached histories are dropped")
            .ProducesRejections(conflict: true, notFound: false);

        group.MapPost("/history/track-holdings", TrackHoldingsAsync)
            .WithName("TrackPortfolioHoldings")
            .WithSummary("Track my holdings: tracks the instruments of the open holdings of the scope (accountId, bucketId) that are in the instrument master, with the bulk tracking command; idempotent. Answers how many were added, how many holdings are not in the master (notInMaster) and how many instruments are retired and skipped; starts no job, the hint says the next Update data builds the bars")
            .ProducesRejections(conflict: true, notFound: false);

        group.MapPost("/history/track-benchmark", TrackBenchmarkAsync)
            .WithName("TrackPortfolioBenchmark")
            .WithSummary("Track the benchmark: tracks the benchmark index with the bulk tracking command; idempotent. Answers how many were added; starts no job, the hint says the next Update data builds the bars. 404 portfolio.not_found when the instrument master has no benchmark index")
            .ProducesRejections(conflict: true);
    }

    private static async Task<Results<Ok<PortfolioHistory>, ProblemHttpResult>> GetHistoryAsync(
        HttpContext http,
        IQueryHandler<GetPortfolioHistoryQuery, Result<PortfolioHistory>> handler,
        CancellationToken cancellationToken,
        Guid? accountId = null,
        Guid? bucketId = null,
        string? from = null,
        string? to = null,
        string? resolution = null) =>
        Respond(await handler.HandleAsync(new GetPortfolioHistoryQuery(new HistoryRequest(accountId, bucketId, from, to, resolution)), cancellationToken), http);

    private static async Task<Ok<BenchmarkView>> GetBenchmarkAsync(IQueryHandler<GetBenchmarkQuery, BenchmarkView> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetBenchmarkQuery(), cancellationToken));

    private static async Task<Results<Ok<BenchmarkView>, ProblemHttpResult>> SetBenchmarkAsync(
        BenchmarkInput input,
        HttpContext http,
        ICommandHandler<SetBenchmarkCommand, BenchmarkView> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new SetBenchmarkCommand(input, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<PortfolioTrackResult>, ProblemHttpResult>> TrackHoldingsAsync(
        HttpContext http,
        ICommandHandler<TrackPortfolioHoldingsCommand, PortfolioTrackResult> handler,
        CancellationToken cancellationToken,
        Guid? accountId = null,
        Guid? bucketId = null) =>
        Respond(await handler.HandleAsync(new TrackPortfolioHoldingsCommand(accountId, bucketId, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<PortfolioTrackResult>, ProblemHttpResult>> TrackBenchmarkAsync(
        HttpContext http,
        ICommandHandler<TrackBenchmarkCommand, PortfolioTrackResult> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new TrackBenchmarkCommand(http.GetActor()), cancellationToken), http);

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result, HttpContext http) =>
        result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
}
