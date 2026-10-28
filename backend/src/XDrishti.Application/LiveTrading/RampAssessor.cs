using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.LiveTrading;

/// <summary>What the evaluator, the Ramp tab and the owner acts all read: the account at its step, the evidence of the step, the gates, the breach rules and what still holds a hold.</summary>
internal sealed record RampAssessment(
    RampState State,
    DateOnly StepDay,
    RampEvidence Evidence,
    IReadOnlyList<RampGate> Gates,
    GateSnapshot Snapshot,
    IReadOnlyList<Breach> Breaches,
    IReadOnlyList<Breach> HoldConditions,
    double WeeklyLossLimitR);

/// <summary>
/// Reads the stored live record of an account and turns it into the <see cref="RampEvidence"/> of its current step (spec P11-04, ADR 0151), then applies the pure gates and breach rules. Reads only: stored fills, outcomes, executions, runs and halts; it
/// never calls a broker and never writes. A session counts only if it is completed (before today) and after the day the step was set. The cost is the sessions of the step (capped at <see cref="MaxSessions"/>) times a few indexed reads.
/// </summary>
internal sealed class RampAssessor(
    IRampStore ramp,
    ILiveCaptureStore capture,
    ILiveExecutionStore executions,
    ILiveHaltStore halts,
    LiveTicketSource tickets,
    ITradingCalendar calendars,
    IRiskProfileStore profiles,
    ILiveRampSettings settings,
    TimeProvider time)
{
    public const int MaxSessions = 120;
    private const int OutcomePage = 200;

    public async ValueTask<RampAssessment?> AssessAsync(BrokerAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        var state = await ramp.CurrentAsync(account.Id, cancellationToken);
        if (state is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var today = IndiaTime.Today(now);
        var history = await ramp.HistoryAsync(account.Id, 200, cancellationToken);
        var stepEvent = history.FirstOrDefault(e => e.Kind is RampEventKinds.SetStep or RampEventKinds.AutoStepUp or RampEventKinds.StepDown);
        var stepDay = stepEvent is null ? today.AddDays(-1) : IndiaTime.Today(stepEvent.CreatedAt);
        var calendar = await calendars.GetAsync(cancellationToken);
        var dates = new List<DateOnly>();
        for (var d = stepDay.AddDays(1); d < today; d = d.AddDays(1))
        {
            if (calendar.IsTradingDay(d))
            {
                dates.Add(d);
            }
        }

        if (dates.Count > MaxSessions)
        {
            dates = dates[^MaxSessions..];
        }

        var t = settings.Thresholds;
        var evidence = await EvidenceAsync(account.Id, dates, stepDay, today, now, t, cancellationToken);
        var gates = RampGates.Evaluate(evidence, state.Step, t);
        var profile = account.Trading.RiskProfileId is { } id ? (await profiles.ListAsync(cancellationToken)).FirstOrDefault(p => p.Id == id) : null;
        var weeklyR = profile is null ? 5.0 : (double)profile.WeeklyLossR;
        return new RampAssessment(state, stepDay, evidence, gates, GateSnapshot.From(state.Step, gates), BreachRules.Evaluate(evidence, t, weeklyR), BreachRules.HoldConditions(evidence, t), weeklyR);
    }

    private async ValueTask<RampEvidence> EvidenceAsync(Guid accountId, List<DateOnly> dates, DateOnly stepDay, DateOnly today, DateTimeOffset now, RampThresholds t, CancellationToken cancellationToken)
    {
        var last10 = dates.TakeLast(10).ToHashSet();
        var lastCool = dates.TakeLast(t.HoldCooldownSessions).ToHashSet();
        var last5 = dates.TakeLast(5).ToHashSet();

        // Capture health and the settle's agreement with the trade book, per session.
        var healthy = 0;
        var bad5 = 0;
        var gap5 = 0;
        var disagree10 = 0;
        foreach (var date in dates)
        {
            var runs = await capture.RunsAsync(accountId, date, cancellationToken);
            var health = CaptureHealth.Evaluate(runs, date, now);
            if (health == CaptureHealth.Healthy)
            {
                healthy++;
            }

            if (last5.Contains(date) && health != CaptureHealth.Healthy)
            {
                bad5++;
                gap5 += health == CaptureHealth.Gap ? 1 : 0;
            }

            if (last10.Contains(date) && runs.Any(r => r.Kind == OutcomeCodes.KindSettle && r.Outcome == OutcomeCodes.RunPartial))
            {
                disagree10++;
            }
        }

        // Outcomes and executions of the step.
        var outcomes = new List<StoredOutcome>();
        if (dates.Count > 0)
        {
            for (var page = 0; ; page++)
            {
                var read = await capture.ListOutcomesAsync(accountId, dates[0], dates[^1], page * OutcomePage, OutcomePage, cancellationToken);
                outcomes.AddRange(read.Items);
                if (read.Items.Count < OutcomePage)
                {
                    break;
                }
            }
        }

        var window = dates.ToHashSet();
        var done = (await executions.ExecutionsUntilAsync(today, cancellationToken)).Where(x => x.Row.AccountId == accountId && window.Contains(x.Row.ForDate)).OrderBy(x => x.Row.ForDate).ThenBy(x => x.Row.OutcomeId).ToList();
        var byOutcome = done.ToDictionary(x => x.Row.OutcomeId);

        var allHalts = await halts.ListAsync(accountId, stepDay.AddDays(-7), today, cancellationToken);
        var weeklyRecent = (await halts.ListAsync(accountId, today.AddDays(-56), today, cancellationToken)).Count(h => h.Kind == HaltCodes.Weekly && h.Event == HaltCodes.Opened);
        var inStep = allHalts.Where(h => h.Event == HaltCodes.Opened && h.PeriodStart > stepDay.AddDays(-7)).ToList();

        // Trips: a closed entry; R from the plan risk per share, uncertain when it cannot be fixed.
        var trips = new List<TripFact>();
        foreach (var x in done.Where(x => x.Row.ExitQty is not null))
        {
            var r = x.Row;
            double? gross = null;
            if (r.RiskPerShare is > 0 && r.ExitVwap is { } exit && r.ExitQty == r.FillQty)
            {
                gross = (double)((r.Side == "buy" ? 1m : -1m) * (exit - r.FillVwap) / r.RiskPerShare.Value);
            }

            double? share = r.LiveCharges is { } c && r.RiskPerShare is > 0 ? (double)(c / (r.RiskPerShare.Value * r.FillQty)) : null;
            trips.Add(new TripFact(gross, gross is null, share));
        }

        double? drawdown = null;
        double run = 0, peak = 0, worst = 0;
        var any = false;
        foreach (var r in trips.Where(x => x.R is not null).Select(x => x.R!.Value))
        {
            any = true;
            run += r;
            peak = Math.Max(peak, run);
            worst = Math.Max(worst, peak - run);
        }

        if (any)
        {
            drawdown = worst;
        }

        var kinds = done.Where(x => x.Row.ReferenceKind != ReferenceKind.Unavailable && x.Row.SlipTotalR is not null).GroupBy(x => x.Row.ReferenceKind).OrderByDescending(g => g.Count()).ThenBy(g => g.Key == ReferenceKind.TicketLevel ? 0 : 1).FirstOrDefault();

        // Discipline from the outcomes, and the discretionary symbol-days from re-matching each session's fills (the same rule the capture screen uses).
        var entries = outcomes.Where(o => o.Outcome.EntryQty > 0).ToList();
        var intraday = entries.Where(o => byOutcome.TryGetValue(o.Id, out var x) && x.Row.Style == "intraday").ToList();
        var discretionary = 0;
        foreach (var date in dates)
        {
            var fills = await capture.FillsAsync(accountId, date, cancellationToken);
            if (fills.Count == 0)
            {
                continue;
            }

            var approved = await tickets.ForDateAsync(date, accountId, cancellationToken);
            discretionary += LiveFillMatcher.Match(date, [.. approved.Select(a => a.Journal)], fills).Where(l => l.Role == JournalRole.Discretionary).Select(l => l.Fill.Symbol).Distinct(StringComparer.Ordinal).Count();
        }

        bool AfterHalt(StoredOutcome o) => o.Outcome.FirstEntryAt is { } at && inStep.Any(h =>
            at > h.CreatedAt && (h.Kind == HaltCodes.Daily ? h.PeriodStart == o.ForDate : h.Action == HaltCodes.StopNew && SameWeek(h.PeriodStart, o.ForDate)));
        bool OverAlloc(StoredOutcome o) => o.Outcome.Outcome == OutcomeCodes.OverAllocation || o.Outcome.Flags.Contains(OutcomeCodes.FlagOverAllocation);
        bool Unprotected(StoredOutcome o) => o.Outcome.Protection == OutcomeCodes.Unprotected && byOutcome.TryGetValue(o.Id, out var x) && x.Row.Style == "intraday";

        return new RampEvidence
        {
            Sessions = dates.Count,
            Trips = trips,
            SlippageR = kinds is null ? [] : [.. kinds.Select(x => x.Row.SlipTotalR!.Value)],
            SlippageKind = kinds?.Key,
            DiscretionaryTrades = discretionary,
            OverAllocations = entries.Count(OverAlloc),
            ChasedEntries = entries.Count(o => o.Outcome.Flags.Contains(OutcomeCodes.FlagChasedEntry)),
            Entries = entries.Count,
            IntradayEntries = intraday.Count,
            ProtectedIntraday = intraday.Count(o => o.Outcome.Protection == OutcomeCodes.Protected),
            ProtectionUnavailable = intraday.Count(o => o.Outcome.Protection is OutcomeCodes.Unavailable),
            ProtectionUnknown = entries.Count(o => !intraday.Contains(o) && o.Outcome.Protection == OutcomeCodes.ProtectionUnknown),
            DrawdownR = drawdown,
            AfterHaltFills = entries.Count(AfterHalt),
            DailyHalts = inStep.Count(h => h.Kind == HaltCodes.Daily && dates.Contains(h.PeriodStart)),
            HealthySessions = healthy,
            CaptureSessions = dates.Count,
            BadCaptureLast5 = bad5,
            GapLast5 = gap5,
            HistoryDisagreementsLast10 = disagree10,
            AfterHaltLast10 = entries.Count(o => last10.Contains(o.ForDate) && AfterHalt(o)),
            OverAllocationLast10 = entries.Count(o => last10.Contains(o.ForDate) && OverAlloc(o)),
            UnprotectedLast10 = entries.Count(o => last10.Contains(o.ForDate) && Unprotected(o)),
            AfterHaltLast5 = entries.Count(o => lastCool.Contains(o.ForDate) && AfterHalt(o)),
            OverAllocationLast5 = entries.Count(o => lastCool.Contains(o.ForDate) && OverAlloc(o)),
            UnprotectedLast5 = entries.Count(o => lastCool.Contains(o.ForDate) && Unprotected(o)),
            WeeklyHaltsLast8Weeks = weeklyRecent,
        };
    }

    /// <summary>The Monday of the ISO week of a date (the period of a weekly halt).</summary>
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static bool SameWeek(DateOnly a, DateOnly b) => WeekStart(a) == WeekStart(b);
}
