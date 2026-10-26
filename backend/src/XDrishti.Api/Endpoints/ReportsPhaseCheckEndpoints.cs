using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The P8 phase check (spec P8-17, ADR 0126). One GET, computed on read; nothing is stored, started or changed.</summary>
internal sealed class ReportsPhaseCheckEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        api.MapGroup("/reports").WithTags("Reports")
            .MapGet("/phase-check", CheckAsync)
            .WithName("GetReportsPhaseCheck")
            .WithSummary("The P8 exit judged from what can be read: criteria 1 to 10 of P8-00, each MET, NOT MET or NOT EVALUABLE with its detail (a caveat beside a MET that rests on a convention); the answer the What-works view gives on the default set (a ranked cell with its n and lower bound, or the nothing-has-enough-evidence sentence with the counts); the cold and warm time of each report read; the honest limits. A criterion that could not be read is never MET. The interaction test, the owner's stopwatch note, the bundle size and the browser pass are files on the owner's machine and are read by scripts/p8-phase-check.sh, not here. Takes no parameter. Read only; the check runs the reports once more than a screen would and is not a polling target.")
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }

    private static async Task<Results<Ok<ReportsPhaseCheckView>, ProblemHttpResult>> CheckAsync(
        HttpContext http,
        IQueryHandler<EvaluateReportsPhaseCheckQuery, Result<ReportsPhaseCheckView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new EvaluateReportsPhaseCheckQuery(), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ProblemResults.From(result.Error, http);
    }
}
