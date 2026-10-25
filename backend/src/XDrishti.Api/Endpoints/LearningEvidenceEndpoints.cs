using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>Smart-rule and loss-tag evidence of spec P8-13 (ADR 0108). Read only: nothing is stored, applied or switched.</summary>
internal sealed class LearningEvidenceEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var learning = api.MapGroup("/learning").WithTags("Learning");

        learning.MapGet("/smart-rules", SmartRulesAsync)
            .WithName("GetSmartRulesEvidence")
            .WithSummary("What the stored plans say about each smart rule over the newest 1 to 60 planned sessions (plans, default 30): plans on which the rule was unavailable, candidates it moved to reserve by reason, and plans flagged for review. 'Ran on' is never derived (a plan row stores no code for a rule that ran), and every outcome is not_evaluable with its reason and the command that will fill it. 422 learning.invalid. Read only.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        learning.MapGet("/loss-tags", LossTagsAsync)
            .WithName("GetLossTags")
            .WithSummary("Why trades lost, on the report population: per cell the loss count, untagged count, count, share and R lost per tag, growing tags, the worst losses, the number of comparisons with the expected chance flags, and what is not built. Repeat run=KEY:STYLE:EXIT (1 to 50) to name runs; without it the default run set of the reports is used for the runs that record a style and exit. Computed on read. 422 learning.invalid or reports.too_large; 404 learning.run_not_found; 409 learning.cell_duplicate or learning.mixed_basis. Read only.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Results<Ok<SmartRulesView>, ProblemHttpResult>> SmartRulesAsync(
        int? plans,
        HttpContext http,
        IQueryHandler<GetSmartRulesEvidenceQuery, Result<SmartRulesView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetSmartRulesEvidenceQuery(plans ?? 30), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<LossTagsView>, ProblemHttpResult>> LossTagsAsync(
        string[]? run,
        HttpContext http,
        IQueryHandler<GetLossTagsQuery, Result<LossTagsView>> handler,
        CancellationToken cancellationToken)
    {
        var sources = new List<CellSource>();
        foreach (var text in run ?? [])
        {
            // KEY:STYLE:EXIT, the exit label being the rest (a label may hold ':' or ',').
            var parts = text.Split(':', 3);
            if (parts.Length != 3)
            {
                return Problem(LearningErrors.Invalid([new FieldError("run", "Write a run as KEY:STYLE:EXIT.")]), http);
            }

            sources.Add(new CellSource(parts[0], parts[1], parts[2]));
        }

        var result = await handler.HandleAsync(new GetLossTagsQuery(sources), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422 as in the other learning reads; a missing run is 404 and a duplicate 409, through the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Kind == ErrorKind.Validation
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
