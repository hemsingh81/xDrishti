using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Paper;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>Backtest versus paper (spec P10-04, ADR 0122, R14). One GET, computed on read; nothing is stored, nothing is placed.</summary>
internal sealed class PaperCompareEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        api.MapGroup("/paper").WithTags("Paper")
            .MapGet("/backtest-vs-paper", CompareAsync)
            .WithName("GetBacktestVsPaper")
            .WithSummary("The counted paper trades of an epoch against a composition-matched bootstrap of the backtest trades known before the epoch started: the paper mean with its own 90% interval against the 5th to 95th percentile band (below, within or above), the paper's largest fall against the 90th percentile of the same number of backtest trades, the two exit statistics as verdicts (met, not_met, not_evaluable: they need 60 counted trades and costs the owner confirmed), an early warning, the fan of cumulative R, the cells and the plan-price sensitivity beside. Every figure is a state and a number only for a value or provisional figure. Optional epoch (default the newest) and draws 1000 to 100000 (default 10000). Deterministic, read only; simulated, no order exists. 404 paper.not_found, 422 paper.invalid")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<Results<Ok<BacktestVsPaperView>, ProblemHttpResult>> CompareAsync(
        long? epoch,
        int? draws,
        HttpContext http,
        IQueryHandler<GetBacktestVsPaperQuery, Result<BacktestVsPaperView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetBacktestVsPaperQuery(epoch, draws), cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        return result.Error.Code == PaperErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, result.Error.Message, result.Error.Code))
            : ProblemResults.From(result.Error, http);
    }
}
