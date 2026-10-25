using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The learning overview of spec P6-18 (ADR 0080): counts and the newest stored verdicts, for the Learning screen. Read only.</summary>
internal sealed class LearningOverviewEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var learning = api.MapGroup("/learning").WithTags("Learning");

        learning.MapGet("/overview", GetAsync)
            .WithName("GetLearningOverview")
            .WithSummary("What the learning engine holds: rows in the newest cell snapshot and its date; and for models, experiments, hypotheses, lessons and the changelog the count of the newest 200 (flagged when there may be more), the split by role, outcome or state, and the newest record with its stored verdict. Nothing is computed or scored; the limits (about 2.1 years, few folds, modelled fills) are listed. Empty before the first record.");

        learning.MapGet("/phase-check", PhaseCheckAsync)
            .WithName("GetLearningPhaseCheck")
            .WithSummary("The P6 phase check from stored records (ADR 0080, amended by ADR 0103): each exit criterion as MET, NOT MET or NOT EVALUABLE with its numbers and caveat, and the honest limits. It re-reads the two runs of the newest experiment and judges them again, so it can take seconds (elapsedMs says how long); it takes no parameter, a client cannot supply a replay time. Read only; writes nothing.");
    }

    private static async Task<Ok<LearningOverviewView>> GetAsync(IQueryHandler<GetLearningOverviewQuery, Result<LearningOverviewView>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetLearningOverviewQuery(), cancellationToken);
        return TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<LearningPhaseCheckView>, ProblemHttpResult>> PhaseCheckAsync(
        HttpContext http,
        IQueryHandler<GetLearningPhaseCheckQuery, Result<LearningPhaseCheckView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetLearningPhaseCheckQuery(), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
