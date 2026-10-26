using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Paper;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The paper phase check (spec P10-06, ADR 0123). One GET, computed on read; nothing is stored, nothing is placed.</summary>
internal sealed class PaperPhaseCheckEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        api.MapGroup("/paper").WithTags("Paper")
            .MapGet("/phase-check", CheckAsync)
            .WithName("GetPaperPhaseCheck")
            .WithSummary("The P10 exit judged from stored records for one epoch (default the newest): duration and coverage, counted trades, expectancy within the backtest band after costs, drawdown within the 90th percentile and capture integrity, each MET, NOT MET or NOT EVALUABLE with its facts and blockers; the caveats sit in the label of a MET (exitCriterion); the week lines, every epoch (only the current one is judged), the discipline lines, the P7 phase check, the services and settings, what is never judged here and the honest limits. The report and the printable text. Optional through (rounded down to a trading session; default the last closed session) and epoch. Read only; simulated, no order exists. 404 paper.not_found, 422 paper.invalid")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<Results<Ok<PaperPhaseCheckView>, ProblemHttpResult>> CheckAsync(
        DateOnly? through,
        long? epoch,
        HttpContext http,
        IQueryHandler<EvaluatePaperPhaseCheckQuery, Result<PaperPhaseCheckView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new EvaluatePaperPhaseCheckQuery(through, epoch), cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        return result.Error.Code == PaperErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, result.Error.Message, result.Error.Code))
            : ProblemResults.From(result.Error, http);
    }
}
