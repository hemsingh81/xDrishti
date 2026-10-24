using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Learning;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Autonomy;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// The P7 phase check (spec P7-18, ADR 0097). It reads the stored run rows of the last ten trading sessions, the stored plans with their tickets, approvals, grades and journal, the services, the owner's switches and the broker allow-list, and hands the plain
/// facts to the pure <see cref="PlanPhaseCheck"/>. Nothing is written, no test is run and no setting is changed; a read that fails is an error of the call, never a quiet MET. Time comes from the <see cref="TimeProvider"/>.
/// </summary>
internal sealed class EvaluatePlanPhaseCheckHandler(
    ITradingCalendar tradingCalendar,
    IPlanStore plans,
    IGradeStore grades,
    IJournalStore journal,
    IDigestStore digests,
    IAutonomySettingsStore autonomy,
    IServiceScheduleReader services,
    IBrokerAllowList allowList,
    IPlanBuilderSettings builderSettings,
    IOpenBookSettings openBook,
    TimeProvider clock,
    ILogger<EvaluatePlanPhaseCheckHandler> logger) : IQueryHandler<EvaluatePlanPhaseCheckQuery, Result<PlanPhaseCheckView>>
{
    /// <summary>The run rows read for the history and the fail-closed counts (the store's limit is 200).</summary>
    internal const int RunRowsRead = PlanPhaseCheck.RunRowsRead;

    /// <summary>The newest plans whose tickets, approvals, grades and journal are counted.</summary>
    internal const int PlansRead = PlanPhaseCheck.PlansRead;

    /// <summary>A few minutes after the nightly's time before the newest session reads as a gap: the row is written when the run ends.</summary>
    internal static readonly TimeSpan NightlyGrace = TimeSpan.FromMinutes(10);

    /// <summary>How long after the cutoff a ticket may still wait for the cutoff job before it is counted as left undecided.</summary>
    internal static readonly TimeSpan CutoffGrace = TimeSpan.FromHours(2);

    private const int DigestRead = 52;

    private static readonly TimeOnly DefaultNightlyTime = new(18, 30);

    public async ValueTask<Result<PlanPhaseCheckView>> HandleAsync(EvaluatePlanPhaseCheckQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = clock.GetUtcNow();
        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var lastClosed = calendar.LastClosedSession(now);
        var latest = NightlyRules.NextSession(calendar, lastClosed) ?? lastClosed;

        DateOnly through;
        if (query.Through is { } asked)
        {
            if (asked > latest)
            {
                PlanPhaseCheckLog.Refused(logger, PlanErrors.InvalidCode);
                return Result.Failure<PlanPhaseCheckView>(PlanErrors.Invalid([new FieldError("through", $"The newest planned session is {latest:yyyy-MM-dd}; a later date has no run to read.")]));
            }

            if (OnOrBefore(calendar, asked) is not { } found)
            {
                PlanPhaseCheckLog.Refused(logger, PlanErrors.InvalidCode);
                return Result.Failure<PlanPhaseCheckView>(PlanErrors.Invalid([new FieldError("through", "No trading session lies on or before this date.")]));
            }

            through = found;
        }
        else
        {
            through = await NightlyDueAsync(latest, lastClosed, now, cancellationToken) ? latest : lastClosed;
        }

        var window = await ReadWindowAsync(calendar, through, cancellationToken);
        var allRuns = await plans.ListRunsAsync(null, RunRowsRead, cancellationToken);
        var listed = await plans.ListAsync(null, null, PlansRead, cancellationToken);
        var stored = listed.Where(p => p.Plan.ForDate <= through).ToList();
        var autonomyNow = await autonomy.CurrentAsync(cancellationToken);
        var autonomyHistory = await autonomy.HistoryAsync(PlanPhaseCheck.AutonomyRowsRead, cancellationToken);
        var digestCount = (await digests.ListAsync(DigestRead, cancellationToken)).Count;

        var read = await ReadStoredPlansAsync(stored, listed.Count >= PlansRead, autonomyNow, autonomyHistory, digestCount, now, cancellationToken);

        var facts = new PlanPhaseFacts(
            through,
            window,
            FirstRecorded(allRuns),
            FailClosed(allRuns),
            AllowList(),
            read.Evidence,
            read.Review,
            read.Allocation,
            read.Grades,
            read.Journal,
            await SetupAsync(autonomyNow, read.Counts, cancellationToken));

        var report = PlanPhaseCheck.Evaluate(facts);
        var zeroPrimary = report.Sessions.Count(x => x.ZeroPrimary);
        var met = report.Criteria.Count(c => c.Verdict == PlanPhaseVerdicts.Met);
        var notMet = report.Criteria.Count(c => c.Verdict == PlanPhaseVerdicts.NotMet);
        var notEvaluable = report.Criteria.Count(c => c.Verdict == PlanPhaseVerdicts.NotEvaluable);
        PlanPhaseCheckLog.Evaluated(logger, through, report.ExitCriterion, report.Streak, zeroPrimary, met, notMet, notEvaluable);
        return Result.Success(new PlanPhaseCheckView(report, report.ToText()));
    }

    private static DateOnly? OnOrBefore(TradingCalendar calendar, DateOnly date)
    {
        for (var i = 0; i < 30; i++)
        {
            var d = date.AddDays(-i);
            if (calendar.IsTradingDay(d))
            {
                return d;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the newest planned session is already due: its nightly run happens the evening of the last closed session. Before the nightly's time on that evening, with no row yet, the session is not yet judged as a gap.
    /// </summary>
    private async ValueTask<bool> NightlyDueAsync(DateOnly latest, DateOnly lastClosed, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if ((await plans.ListRunsAsync(latest, 1, cancellationToken)).Count > 0)
        {
            return true;
        }

        var time = (await services.GetAsync(NightlyService.Id, cancellationToken))?.TimeIst ?? DefaultNightlyTime;
        var ist = IndiaTime.ToIst(now);
        return !(DateOnly.FromDateTime(ist.DateTime) == lastClosed && ist.TimeOfDay < time.ToTimeSpan() + NightlyGrace);
    }

    private async ValueTask<List<PhaseSessionInput>> ReadWindowAsync(TradingCalendar calendar, DateOnly through, CancellationToken cancellationToken)
    {
        var dates = new List<DateOnly>();
        for (var d = through; dates.Count < PlanPhaseCheck.RequiredSessions && d > through.AddDays(-3660); d = d.AddDays(-1))
        {
            if (calendar.IsTradingDay(d))
            {
                dates.Add(d);
            }
        }

        dates.Reverse();
        var window = new List<PhaseSessionInput>();
        foreach (var date in dates)
        {
            var rows = await plans.ListRunsAsync(date, RunRowsRead, cancellationToken);
            var runs = rows.OrderBy(r => r.Id).Select(r => new PhaseRun(r.Id, r.Attempt, r.Run.Outcome, r.Run.FailedGate, r.Run.Reason, r.Run.Manual, r.Run.StartedAt, r.Run.FinishedAt, r.Run.PlanId)).ToList();
            PhasePlan? plan = null;
            if (runs.Find(r => r.Outcome == "published")?.PlanId is { } planId && await plans.FindAsync(planId, cancellationToken) is { } row)
            {
                plan = new PhasePlan(row.Id, row.Plan.Basis, row.Primary, row.Reserve, row.Rejected);
            }

            window.Add(new PhaseSessionInput(date, runs, plan));
        }

        return window;
    }

    private static DateOnly? FirstRecorded(IReadOnlyList<PlanRunRow> rows) =>
        rows.Count == 0 ? null : rows.Count >= RunRowsRead ? DateOnly.MinValue : rows.Min(r => r.Run.ForDate);

    private static PhaseFailClosedFacts FailClosed(IReadOnlyList<PlanRunRow> rows)
    {
        var failed = rows.Where(r => r.Run.Outcome == "failed_closed").ToList();
        var order = PlanPhaseCheck.GateOrder.ToList();
        var byGate = failed
            .GroupBy(r => r.Run.FailedGate ?? string.Empty, StringComparer.Ordinal)
            .Select(g => new PhaseGateCount(g.Key.Length == 0 ? "(none)" : g.Key, g.Count()))
            .OrderBy(g => order.IndexOf(g.Gate) is var i and >= 0 ? i : int.MaxValue)
            .ThenBy(g => g.Gate, StringComparer.Ordinal)
            .ToList();
        var inconsistent = failed.Count(r => string.IsNullOrWhiteSpace(r.Run.FailedGate) || string.IsNullOrWhiteSpace(r.Run.Reason) || r.Run.PlanId is not null);
        return new PhaseFailClosedFacts(rows.Count, rows.Count >= RunRowsRead, byGate, inconsistent);
    }

    private PhaseAllowListFacts AllowList() =>
        new(allowList.Entries(), PlanPhaseCheck.PinnedAllowList, [.. PlanPhaseCheck.RefusedProbes.Where(p => allowList.Allows(p.Method, p.Url)).Select(p => $"{p.Method} {p.Url}")]);

    private sealed record StoredRead(PhaseEvidenceFacts Evidence, PhaseReviewFacts Review, PhaseAllocationFacts Allocation, PhaseGradeFacts Grades, PhaseJournalFacts Journal, List<PhaseCount> Counts);

    private async ValueTask<StoredRead> ReadStoredPlansAsync(
        List<PlanRow> stored,
        bool plansTruncated,
        AutonomySettings autonomyNow,
        IReadOnlyList<AutonomyChangeRow> autonomyHistory,
        int digestCount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var gradeMap = await grades.ForPlansAsync([.. stored.Select(p => p.Id)], cancellationToken);
        int tickets = 0, noNotice = 0, noLimits = 0, noEvidence = 0;
        int lapsedPlans = 0, withDecision = 0, ownerApproved = 0, ownerModified = 0, ownerRejected = 0, autoApproved = 0, lapsedTickets = 0, undecided = 0, afterCutoff = 0, approvedNoRow = 0, armed = 0;
        int allocations = 0, notPaper = 0;
        int gradedPlans = 0, partly = 0, gradedTickets = 0, ticketsOfGraded = 0;
        int entries = 0, fills = 0, fillGrades = 0, duplicates = 0;
        var seenFills = new HashSet<Guid>();
        int autoBeforeOn = 0;
        var historyTruncated = autonomyHistory.Count >= PlanPhaseCheck.AutonomyRowsRead;
        DateTimeOffset? firstOn = autonomyHistory.Where(h => h.AutoApproveTickets).Select(h => (DateTimeOffset?)h.ChangedAt).Min();

        foreach (var plan in stored)
        {
            var items = await plans.ItemsAsync(plan.Id, cancellationToken);
            var approvals = await plans.ApprovalsAsync(plan.Id, cancellationToken);
            var journalled = await journal.EntriesAsync(plan.Id, cancellationToken);
            var trail = await plans.HistoryAsync(plan.Id, cancellationToken);

            noNotice += plan.Plan.Notice == PlanWording.NoOrder ? 0 : 1;
            noLimits += string.IsNullOrWhiteSpace(plan.Plan.Limits) ? 1 : 0;
            lapsedPlans += plan.State == PlanState.Lapsed ? 1 : 0;
            withDecision += approvals.Count > 0 ? 1 : 0;

            var real = items.Where(i => i.Item.Group != TicketGroup.Rejected).ToList();
            tickets += real.Count;
            noEvidence += real.Count(i => string.IsNullOrWhiteSpace(i.Item.Evidence.DataRange) || string.IsNullOrWhiteSpace(i.Item.Evidence.PWinBasis));
            lapsedTickets += real.Count(i => i.State == TicketState.Lapsed);
            armed += real.Count(i => i.State == TicketState.Armed);
            // a ticket that is, or ever was, approved, armed or triggered needs an approve row, even when it later expired or closed
            string[] approvedCodes = [TicketState.Approved.Code(), TicketState.Armed.Code(), TicketState.Triggered.Code()];
            var needsApproval = real.Where(i => i.State is TicketState.Approved or TicketState.Armed or TicketState.Triggered).Select(i => i.Id)
                .Concat(trail.Where(e => e.ItemId is not null && approvedCodes.Contains(e.To, StringComparer.Ordinal)).Select(e => e.ItemId!.Value))
                .ToHashSet();
            approvedNoRow += needsApproval.Count(id => !approvals.Any(a => a.ItemId == id && a.Decision == "approve"));
            if (plan.State is PlanState.Published or PlanState.InReview && now >= plan.Plan.ReviewCutoff + CutoffGrace)
            {
                undecided += ReviewCutoff.Decide([.. items.Select(i => (i.Item.Group, i.State))]).LapseIndexes.Count;
            }

            ownerApproved += approvals.Count(a => a.Kind == "owner" && a.Decision == "approve");
            ownerModified += approvals.Count(a => a.Kind == "owner" && a.Decision == "modify");
            ownerRejected += approvals.Count(a => a.Kind == "owner" && a.Decision == "reject");
            autoApproved += approvals.Count(a => a.Kind == "auto");
            autoBeforeOn += historyTruncated ? 0 : approvals.Count(a => a.Kind == "auto" && (firstOn is null || a.DecidedAt < firstOn));
            afterCutoff += approvals.Count(a => a.DecidedAt >= plan.Plan.ReviewCutoff);

            var allocs = real.SelectMany(i => i.Item.Allocations).ToList();
            allocations += allocs.Count;
            notPaper += allocs.Count(a => !string.Equals(a.Mode, "paper", StringComparison.OrdinalIgnoreCase));

            var graded = items.Count(i => gradeMap.ContainsKey(i.Id));
            if (graded > 0)
            {
                gradedPlans++;
                gradedTickets += graded;
                ticketsOfGraded += items.Count;
                partly += graded < items.Count ? 1 : 0;
            }

            entries += journalled.Count;
            fillGrades += journalled.Count(e => e.Grade is not null);
            foreach (var fill in journalled.SelectMany(e => e.Fills))
            {
                fills++;
                duplicates += seenFills.Add(fill.TransactionId) ? 0 : 1;
            }
        }

        var counts = new List<PhaseCount>
        {
            new("plans read", stored.Count),
            new("graded tickets", gradedTickets),
            new("journal entries", entries),
            new("weekly digests", digestCount),
            new("armed tickets", armed),
        };
        return new StoredRead(
            new PhaseEvidenceFacts(stored.Count, tickets, noNotice, noLimits, noEvidence),
            new PhaseReviewFacts(stored.Count, lapsedPlans, withDecision, ownerApproved, ownerModified, ownerRejected, autoApproved, lapsedTickets, undecided, afterCutoff, approvedNoRow, autonomyNow.AutoApproveTickets || autonomyHistory.Any(h => h.AutoApproveTickets), autoBeforeOn, PlansRead, plansTruncated, historyTruncated),
            new PhaseAllocationFacts(allocations, notPaper),
            new PhaseGradeFacts(gradedPlans, partly, gradedTickets, ticketsOfGraded),
            new PhaseJournalFacts(entries, fills, fillGrades, duplicates),
            counts);
    }

    private async ValueTask<PhaseSetupFacts> SetupAsync(AutonomySettings autonomyNow, List<PhaseCount> counts, CancellationToken cancellationToken)
    {
        var definitions = new (string Id, string Role, bool Required)[]
        {
            (NightlyService.Id, "the nightly plan", true),
            (EodService.Id, "the end-of-day data the eod gate checks", true),
            (PortfolioBrokerHistoryService.Id, "the portfolio sync the portfolio-sync gate checks", true),
            (SuggestionsService.Id, "the suggestion inbox", false),
            (ReviewCutoffService.Id, "the lapse of unreviewed tickets at the cutoff", false),
            (PreOpenService.Id, "the pre-open check and arming", false),
            (PlanExpiryService.Id, "the expiry of tickets", false),
            (WeeklyService.Id, "the weekly learning digest", false),
        };
        var states = new List<PhaseService>();
        foreach (var (id, role, required) in definitions)
        {
            var info = await services.GetAsync(id, cancellationToken);
            states.Add(new PhaseService(id, role, required, info is null ? "not registered" : info.Enabled ? "enabled" : "paused"));
        }

        var switches = new List<PhaseSwitch>
        {
            new("Planning:Builder:UseSelection", builderSettings.UseSelection, "read by this process (the host CLI when run with xd; the containers may differ): the selection builder; OFF means the nightly publishes only the empty skeleton plan, with zero primary tickets", true),
            new("Planning:OpenBook:AssumedEmpty", openBook.AssumedEmpty, "read by this process (the host CLI when run with xd; the containers may differ): the open book taken as empty; OFF means it is unavailable and the portfolio fill takes nothing", true),
            new(AutonomySettings.AutoApproveKey, autonomyNow.AutoApproveTickets, "auto-approval before the cutoff; OFF (the default) means the owner approves by hand and unreviewed tickets lapse; not part of the exit", false),
            new(AutonomySettings.PromoteFromReserveKey, autonomyNow.PromoteFromReserve, "promotion from the reserve at the pre-open check; OFF (the default) means nothing is armed without the owner's approval; not part of the exit", false),
        };
        return new PhaseSetupFacts(states, switches, counts);
    }
}
