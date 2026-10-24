using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Autonomy;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// One pass of the review job (spec P7-10, ADR 0095), for every plan of today's session that is still open (published or in review):
/// <list type="number">
/// <item>Before the cutoff, nothing lapses. In the last <see cref="AutoLeadMinutes"/> minutes before it, and ONLY if the owner's setting <c>autonomy.auto_approve_tickets</c> is ON, <see cref="AutoApprovePlanCommand"/> runs once for the plan (strictly before the cutoff; the database refuses an automatic approval at or after it). With the setting OFF (the default) nothing of the auto path is called.</item>
/// <item>At and after the cutoff, <see cref="ApplyReviewCutoffCommand"/> lapses every ticket nobody decided on and moves the plan to the state its tickets support. Lapsing is the default; nothing here approves.</item>
/// </list>
/// A plan already closed, or none for today, is a success that does nothing. The job places no order and cannot.
/// </summary>
internal sealed class RunReviewCycleHandler(
    IPlanStore store,
    IAutonomySettingsStore settings,
    ICommandHandler<AutoApprovePlanCommand, AutoApprovalOutcome> auto,
    ICommandHandler<ApplyReviewCutoffCommand, PlanMoveOutcome> cutoff,
    TimeProvider time,
    ILogger<RunReviewCycleHandler> logger) : ICommandHandler<RunReviewCycleCommand, ReviewCycleOutcome>
{
    /// <summary>How many minutes before the cutoff the optional automatic pass may run (so the owner has the evening and the early morning first).</summary>
    public const int AutoLeadMinutes = 2;

    /// <summary>The actor of the cutoff's lapses: a service, never the owner.</summary>
    public const string Actor = "system:review-cutoff";

    public async ValueTask<Result<ReviewCycleOutcome>> HandleAsync(RunReviewCycleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = time.GetUtcNow();
        var today = IndiaTime.Today(now);

        // Today's open plans, and any open plan of an earlier session whose cutoff has passed (the job was paused or the machine slept through 09:03 to 09:10): those are lapsed too, never approved.
        var plans = await store.ListAsync(null, null, 50, cancellationToken);
        var open = plans.Where(p => p.State is PlanState.Published or PlanState.InReview && (p.Plan.ForDate == today || now >= p.Plan.ReviewCutoff)).ToList();
        if (open.Count == 0)
        {
            return Result.Success(new ReviewCycleOutcome(0, 0, 0, "No plan is open for review."));
        }

        var autoApproved = 0;
        var lapsed = 0;
        var waiting = 0;
        var failures = new List<string>();
        foreach (var plan in open)
        {
            if (now < plan.Plan.ReviewCutoff)
            {
                waiting++;
                if (now >= plan.Plan.ReviewCutoff.AddMinutes(-AutoLeadMinutes) && (await settings.CurrentAsync(cancellationToken)).AutoApproveTickets)
                {
                    var passed = await auto.HandleAsync(new AutoApprovePlanCommand(plan.Id), cancellationToken);
                    if (passed.IsSuccess)
                    {
                        autoApproved += passed.Value.Approved;
                    }
                }

                continue;
            }

            var lapse = await cutoff.HandleAsync(new ApplyReviewCutoffCommand(plan.Id, Actor), cancellationToken);
            if (lapse.IsFailure)
            {
                // One plan that cannot be closed does not stop the others; the failure is reported at the end.
                ReviewLog.Refused(logger, lapse.Error.Code);
                failures.Add($"plan {plan.Id}: {lapse.Error.Code}");
                continue;
            }

            lapsed += lapse.Value.Lapsed;
        }

        ReviewLog.Cycle(logger, open.Count, autoApproved, lapsed);
        if (failures.Count > 0)
        {
            return Result.Failure<ReviewCycleOutcome>(Error.Conflict("plans.cutoff_failed", $"{failures.Count} plan(s) could not be closed ({string.Join("; ", failures)}); {lapsed} ticket(s) lapsed on the others."));
        }

        var summary = waiting > 0 && lapsed == 0 && autoApproved == 0
            ? $"{waiting} plan(s) wait for the review cutoff; nothing to do yet."
            : $"{open.Count} plan(s): {autoApproved} ticket(s) approved by the rule, {lapsed} lapsed.";
        return Result.Success(new ReviewCycleOutcome(open.Count, autoApproved, lapsed, summary));
    }
}
