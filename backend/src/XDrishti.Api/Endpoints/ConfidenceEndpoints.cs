using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Confidence;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The confidence programme's reads about a live-forward edge wearing out and about the learning loop (spec CF-12 part 12a, ADR 0218). Two GETs, computed on read; nothing is stored, proposed, paused or changed, and no order exists.</summary>
internal sealed class ConfidenceEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/confidence").WithTags("Confidence");
        group.MapGet("/decay", DecayAsync)
            .WithName("GetConfidenceDecay")
            .WithSummary("The decay monitor of one strategy's forward series (strategy=<id>): non-overlapping windows of graded trades against mu_ref - z x sigma x sqrt(DEFF / n + 1 / N_ref), twice in a row, the P6-09 CUSUM on the same series, a by-quarter table, and the kill-rule evaluations with their numbers (cost stress, drawdown, decay twice in a row, the CF-09 failure boundary as a proposal-only input). The edge series (graded shadow trades) is the primary reading with the P10 behavioural series beside it. Report only: nothing is proposed, stored or paused and no order exists; a figure without a source says unavailable and why. 404 paper.not_found when the strategy has neither a shadow edge candidate nor a counted paper trade in the current epoch, 400 paper.invalid")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapGet("/loop", LoopAsync)
            .WithName("GetConfidenceLoop")
            .WithSummary("Is the learning loop helping? The three arms on the same candidates (frozen, retrained, no model) as paired day-cluster differences, the learning curve, the reliability slope and the decision-quality lift. Report only; each section whose input has no source yet says unavailable and why (today none has one). No order exists.");
    }

    private static async Task<Results<Ok<DecayReportView>, ProblemHttpResult>> DecayAsync(
        string? strategy,
        HttpContext http,
        IQueryHandler<GetDecayReportQuery, Result<DecayReportView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetDecayReportQuery(strategy), cancellationToken);
        if (result.IsFailure)
        {
            return ProblemResults.From(result.Error, http);
        }

        return result.Value.HasSeries
            ? TypedResults.Ok(result.Value)
            : ProblemResults.From(Error.NotFound("paper.not_found", "The strategy has neither a shadow edge candidate nor a counted paper trade in the current paper epoch: there is no series to monitor."), http);
    }

    private static async Task<Results<Ok<LoopReportView>, ProblemHttpResult>> LoopAsync(
        HttpContext http,
        IQueryHandler<GetLoopReportQuery, Result<LoopReportView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetLoopReportQuery(), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
