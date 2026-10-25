using System.Diagnostics;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>
/// Plan versus actual (P8-04): joins the approved ticket lines of the plans in a range to the journal's fill grades, the plan-price grades and the approval rows, and hands plain lines to the pure <see cref="PlanVsActual"/>. Reads only; no new port.
/// </summary>
internal sealed class GetPlanVsActualHandler(IPlanStore plans, IJournalStore journal, IGradeStore grades, PlanVsActualSettings settings, ILogger<GetPlanVsActualHandler> logger)
    : IQueryHandler<GetPlanVsActualQuery, Result<PlanVsActualView>>
{
    /// <summary>The most plans one read joins.</summary>
    public const int MaxPlans = 200;

    public const string BasisText = "realised fills, not simulated, R after the charges the sync recorded, beside simulated on plan prices";

    private static readonly string[] Limits =
    [
        "No graded real trade exists before paper and live trading (P10/P11): until then the chain is empty and the plan-price grades are simulations on plan prices, not fills.",
        "The remainder 'not attributed' is exit slippage and anything else (partial exits, timing); the plan grade stores no simulated exit price, so it is not split.",
        "Charges in R use the planned risk per share; a modified size or a moved stop changes their meaning, and modified tickets are graded at the original plan prices.",
        "Automatic approval counts rely on stored approval rows; a plan approved before automatic approval existed has none.",
    ];

    public async ValueTask<Result<PlanVsActualView>> HandleAsync(GetPlanVsActualQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.From is { } f && query.To is { } t && (f > t || t.DayNumber - f.DayNumber > settings.MaxRangeDays))
        {
            return Result.Failure<PlanVsActualView>(ReportErrors.Invalid(f > t ? "From is after to." : $"The range is longer than {settings.MaxRangeDays} days."));
        }

        var started = Stopwatch.GetTimestamp();
        var listed = await plans.ListAsync(null, null, MaxPlans, cancellationToken);
        var inRange = listed
            .Where(p => (query.From is null || p.Plan.ForDate >= query.From) && (query.To is null || p.Plan.ForDate <= query.To))
            .GroupBy(p => p.Plan.ForDate)
            .Select(g => g.OrderByDescending(p => p.Plan.Version).First())
            .OrderBy(p => p.Plan.ForDate)
            .ToList();
        var lines = new List<PlanVsActualLine>();
        var approvals = new Dictionary<string, int> { ["owner"] = 0, ["auto"] = 0 };
        foreach (var plan in inRange)
        {
            lines.AddRange(await LinesAsync(plan, approvals, cancellationToken));
        }

        var result = PlanVsActual.Build(lines, new PlanVsActualOptions(settings.MinTickets));
        var limits = listed.Count >= MaxPlans ? [.. Limits, $"Only the newest {MaxPlans} plans are read; an older part of the range may be missing."] : Limits;
        var view = new PlanVsActualView(
            query.From,
            query.To,
            inRange.Count,
            result.Counts,
            [new ApprovalCountView("owner", approvals["owner"]), new ApprovalCountView("auto", approvals["auto"])],
            [.. result.Chain.Select(s => new PlanVsActualStepView(s.Step, s.Label, ReportViews.Metric(s.Metric), s.N))],
            new PlanVsActualMissedView(result.Missed.Count, ReportViews.Metric(result.Missed.TotalPlanR)),
            result.NotDecomposed,
            [.. result.ByStrategy.Select(s => new PlanVsActualStrategyView(s.StrategyId, s.N, ReportViews.Metric(s.PlanR), ReportViews.Metric(s.ActualR), ReportViews.Metric(s.EntrySlippageR)))],
            result.MinTickets,
            new PlanVsActualEvidenceView(BasisText, result.Counts.GradedOnFills, inRange.Count == 0 ? null : inRange[0].Plan.ForDate, inRange.Count == 0 ? null : inRange[^1].Plan.ForDate, inRange.Any(p => p.Plan.Provisional), limits));
        var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        ReportLog.Read(logger, "plan_vs_actual", inRange.Count, lines.Count, elapsed);
        return Result.Success(view);
    }

    private async ValueTask<List<PlanVsActualLine>> LinesAsync(PlanRow plan, Dictionary<string, int> approvals, CancellationToken cancellationToken)
    {
        var items = await plans.ItemsAsync(plan.Id, cancellationToken);
        var entries = await journal.EntriesAsync(plan.Id, cancellationToken);
        var stored = await grades.ForPlansAsync([plan.Id], cancellationToken);
        var approved = (await plans.ApprovalsAsync(plan.Id, cancellationToken)).Where(a => a.Decision == "approve").ToList();
        foreach (var a in approved)
        {
            if (approvals.TryGetValue(a.Kind, out var n))
            {
                approvals[a.Kind] = n + 1;
            }
        }

        var kinds = approved.GroupBy(a => a.ItemId).ToDictionary(g => g.Key, g => g.OrderBy(a => a.Id).Last().Kind);
        var byKey = entries.GroupBy(e => (e.ItemId, e.AccountId)).ToDictionary(g => g.Key, g => g.First());
        var lines = new List<PlanVsActualLine>();
        foreach (var item in items.Where(i => i.Item.Group != TicketGroup.Rejected && i.State is TicketState.Approved or TicketState.Armed or TicketState.Triggered or TicketState.Closed))
        {
            var plan1 = stored.TryGetValue(item.Id, out var s) ? s.Grade.Grade : null;
            var triggered = plan1 is { Status: GradeStatus.Graded };
            foreach (var allocation in item.Item.Allocations)
            {
                byKey.TryGetValue((item.Id, allocation.AccountId), out var entry);
                var g = entry?.Grade;
                var status = entry is null ? PlanVsActual.StatusNone : g is null ? PlanVsActual.StatusNotFinished : g.Status switch
                {
                    FillGrade.Graded => PlanVsActual.StatusGraded,
                    FillGrade.Uncertain => PlanVsActual.StatusUncertain,
                    _ => PlanVsActual.StatusNoPlanLevels,
                };
                lines.Add(new PlanVsActualLine(
                    plan.Id, item.Id, item.Item.StrategyId, kinds.GetValueOrDefault(item.Id), status,
                    g?.PlanR, g?.SlippageR, g is null ? null : (double)g.Charges, g?.Qty, g?.PlanEntryPrice, g?.PlanStopPrice, g?.R,
                    triggered, triggered ? plan1?.R : null));
            }
        }

        return lines;
    }
}
