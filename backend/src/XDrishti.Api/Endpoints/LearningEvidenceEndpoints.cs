using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>Smart-rule and loss-tag evidence of spec P8-13 (ADR 0111). Read only: nothing is stored, applied or switched.</summary>
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
            .WithSummary("Why trades lost, on the report population: per cell the loss count, untagged count, count, share and R lost per tag, growing tags, the worst losses, the number of comparisons with the expected chance flags, and what is not built. Repeat run=KEY:STYLE:EXIT (1 to 50) to name runs; without it the default run set of the reports is used for the runs that record a style and exit. Computed on read from the run headers and the learning cell reader, not through the P8-06 report loader's population; at most 150,000 closed trades per request (reports.too_large). Two runs of one strategy or one cell are refused as 409 learning.cell_duplicate (not reports.run_overlap). 422 learning.invalid or reports.too_large; 404 learning.run_not_found; 409 learning.cell_duplicate or learning.mixed_basis. Read only.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        learning.MapGet("/entry-windows", EntryWindowsAsync)
            .WithName("GetEntryWindows")
            .WithSummary("A stored entry-window study (id=), or the newest: options, trades, range, folds used, per cell the status, window HH:mm to HH:mm, trades and share kept, shrunk rate against the cell rate, lift, mean R inside and outside, folds held of evaluable and required, pooled out-of-sample lift, reasons, limits, basis and sources. A status other than Proposed is the answer. Stored by xd learn windows --store; nothing is computed here. 404 learning.entry_windows_not_found. Read only.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        learning.MapGet("/meta-policy", MetaPolicyAsync)
            .WithName("GetMetaPolicy")
            .WithSummary("A stored meta-labelling policy evaluation (id=), or the newest: outcome (NoPolicy is a result), reasons, folds, pooled and recent kept and skipped totals with mean R, per-group thresholds, limits, basis and sources. Stored by xd learn policy --store; nothing is computed here. 404 learning.meta_policy_not_found. Read only.")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<EntryWindowReportView>, ProblemHttpResult>> EntryWindowsAsync(
        long? id,
        HttpContext http,
        IQueryHandler<GetEntryWindowsQuery, Result<EntryWindowReportView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetEntryWindowsQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<MetaPolicyReportView>, ProblemHttpResult>> MetaPolicyAsync(
        long? id,
        HttpContext http,
        IQueryHandler<GetMetaPolicyQuery, Result<MetaPolicyReportView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetMetaPolicyQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
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
            if (CellSource.TryParse(text) is not { } source)
            {
                return Problem(LearningErrors.Invalid([new FieldError("run", "Write a run as KEY:STYLE:EXIT.")]), http);
            }

            sources.Add(source);
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
