using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The small-live phase check (spec P11-06, ADR 0153): reads the window's stored records (one read per store per session, capped at <see cref="MaxSessions"/> sessions) and the P11-03 summary, hands the counted facts to the pure
/// <see cref="LivePhaseCheck"/>. Reads only: nothing is written, no setting is changed, no ramp step moves, no broker is called. A source that cannot be read gives a null fact, which reads NOT EVALUABLE, never MET.
/// </summary>
internal sealed class EvaluateLivePhaseCheckHandler(
    IAccountStore accounts,
    IRampStore ramp,
    ILiveCaptureStore capture,
    ILiveExecutionStore executions,
    ILiveHaltStore halts,
    IComplianceStore compliance,
    LiveTicketSource tickets,
    ITradingCalendar calendars,
    IQueryHandler<LiveVsPaperQuery, Result<LiveVsPaperView>> vsPaper,
    IBrokerAllowList allowList,
    IServiceScheduleReader services,
    ILiveCaptureSettings captureSettings,
    ILiveSettingsStore liveSettings,
    ILiveRampSettings rampSettings,
    TimeProvider time,
    ILogger<EvaluateLivePhaseCheckHandler> logger) : IQueryHandler<LivePhaseCheckQuery, Result<LivePhaseCheckView>>
{
    public const int MaxSessions = 150;
    private static readonly string[] ServiceIds = ["live-capture", "live-settle", "live-estimates", "paper-capture", "nightly", "preopen", "plan-expiry"];

    public async ValueTask<Result<LivePhaseCheckView>> HandleAsync(LivePhaseCheckQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var clock = Stopwatch.StartNew();
        var now = time.GetUtcNow();
        var live = (await accounts.ListActiveAsync(cancellationToken)).Where(a => a.Trading is { Enabled: true, Mode: TradingMode.ManualLive }).ToList();
        var account = query.AccountId is { } id ? live.Find(a => a.Id == id) : live.Count == 1 ? live[0] : null;
        if (account is null)
        {
            return Result.Failure<LivePhaseCheckView>(query.AccountId is null && live.Count > 1
                ? Error.Validation(LiveErrors.Invalid, "several live accounts exist: choose one with accountId")
                : Error.NotFound(LiveErrors.NotFound, "No live account"));
        }

        var calendar = await calendars.GetAsync(cancellationToken);
        var lastClosed = calendar.LastClosedSession(now);
        var through = lastClosed;
        if (query.Through is { } asked)
        {
            if (asked > lastClosed)
            {
                return Result.Failure<LivePhaseCheckView>(Error.Validation(LiveErrors.Invalid, "through is after the last closed session"));
            }

            through = asked;
            while (!calendar.IsTradingDay(through) && through > asked.AddDays(-10))
            {
                through = through.AddDays(-1);
            }
        }

        var history = await ramp.HistoryAsync(account.Id, 200, cancellationToken);
        var firstStep = history.Where(e => e.Kind == RampEventKinds.SetStep).OrderBy(e => e.Id).FirstOrDefault();
        if (firstStep is null)
        {
            return Result.Success(Build(Empty(through, await SetupAsync(cancellationToken))));
        }

        var floor = IndiaTime.Today(firstStep.CreatedAt);
        var dates = new List<DateOnly>();
        // shortcut: the scan reads every session from the first step to the last closed one, capped at MaxSessions (150, about 7 months), one read per store per session, so that a `through` before the first live session can be refused;
        // upgrade to one ranged query per store (and a window-start lookup) when the live phase outgrows that, or when p95 passes 1 s.
        for (var d = floor; d <= lastClosed; d = d.AddDays(1))
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

        var read = await ReadSessionsAsync(account.Id, dates, history, now, cancellationToken);
        var first = read.FirstOrDefault(s => s.Fills.Count > 0 || s.Executions.Count > 0);
        if (first is null)
        {
            return Result.Success(Build(Empty(through, await SetupAsync(cancellationToken))));
        }

        if (through < first.Date)
        {
            return Result.Failure<LivePhaseCheckView>(Error.Validation(LiveErrors.Invalid, "through is before the first live session"));
        }

        var window = read.Where(s => s.Date >= first.Date && s.Date <= through).ToList();
        var facts = await FactsAsync(account, window, history, through, calendar.IsTradingDay(through), live.Count == 1, now, cancellationToken);
        var view = Build(LivePhaseCheck.Evaluate(facts));
        var met = view.Report.Criteria.Count(c => c.Verdict == LiveVerdicts.Met);
        var notMet = view.Report.Criteria.Count(c => c.Verdict == LiveVerdicts.NotMet);
        LivePhaseLog.Checked(logger, through, view.Report.ExitVerdict, view.Report.Sessions, met, notMet, clock.ElapsedMilliseconds);
        return Result.Success(view);
    }

    private static LivePhaseCheckView Build(LivePhaseReport report) => new(report.Sessions == 0 ? "empty" : "value", report, LivePhaseText.Render(report));

    private static LivePhaseReport Empty(DateOnly through, IReadOnlyList<LiveSetupItem> setup) =>
        LivePhaseCheck.Evaluate(new LivePhaseFacts { Through = through, Setup = setup });

    private sealed record SessionRead(
        DateOnly Date,
        string Capture,
        IReadOnlyList<LiveFill> Fills,
        IReadOnlyList<StoredOutcome> Outcomes,
        IReadOnlyList<CaptureRun> Runs,
        IReadOnlyList<(double Scale, long RampEventId)> Scales,
        IReadOnlyList<StoredLiveExecution> Executions,
        int Discretionary);

    private async ValueTask<List<SessionRead>> ReadSessionsAsync(Guid accountId, List<DateOnly> dates, IReadOnlyList<StoredRampEvent> history, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // shortcut: ExecutionsUntilAsync returns every account's executions up to the date and the account filter runs in memory; fine for one owner and tens of live trades,
        // upgrade to an account-and-range query on the store when executions pass a few thousand rows.
        var all = (await executions.ExecutionsUntilAsync(dates.Count == 0 ? IndiaTime.Today(now) : dates[^1], cancellationToken)).Where(x => x.Row.AccountId == accountId).ToLookup(x => x.Row.ForDate);
        var list = new List<SessionRead>();
        foreach (var date in dates)
        {
            var runs = await capture.RunsAsync(accountId, date, cancellationToken);
            var fills = await capture.FillsAsync(accountId, date, cancellationToken);
            var outcomes = await capture.OutcomesOfDateAsync(accountId, date, cancellationToken);
            var scales = await tickets.ScalesForDateAsync(date, accountId, cancellationToken);
            var discretionary = 0;
            if (fills.Count > 0)
            {
                var approved = await tickets.ForDateAsync(date, accountId, cancellationToken);
                discretionary = LiveFillMatcher.Match(date, [.. approved.Select(a => a.Journal)], fills).Where(l => l.Role == JournalRole.Discretionary).Select(l => l.Fill.Symbol).Distinct(StringComparer.Ordinal).Count();
            }

            list.Add(new SessionRead(date, CaptureHealth.Evaluate(runs, date, now), fills, outcomes, runs, scales, [.. all[date]], discretionary));
        }

        return list;
    }

    private async ValueTask<LivePhaseFacts> FactsAsync(BrokerAccount account, List<SessionRead> window, IReadOnlyList<StoredRampEvent> history, DateOnly through, bool throughIsSession, bool oneLive, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var start = window[0].Date;
        var state = await ramp.CurrentAsync(account.Id, cancellationToken);
        var allHalts = (await halts.ListAsync(account.Id, start.AddDays(-7), through, cancellationToken)).Where(h => h.Event == HaltCodes.Opened).ToList();
        var factors = rampSettings.StepFactors;
        var stepEvents = history.Where(e => e.Kind is RampEventKinds.SetStep or RampEventKinds.AutoStepUp or RampEventKinds.StepDown).OrderBy(e => e.Id).ToList();
        int StepAt(DateOnly d) => stepEvents.LastOrDefault(e => IndiaTime.Today(e.CreatedAt) <= d)?.ToStep ?? 0;
        var eventStep = history.ToDictionary(e => e.Id, e => e.ToStep);
        var clears = history.Where(e => e.Kind == RampEventKinds.ClearHold).Select(e => e.CreatedAt).ToList();

        bool AfterHalt(StoredOutcome o) => o.Outcome.FirstEntryAt is { } at && allHalts.Any(h =>
            at > h.CreatedAt && (h.Kind == HaltCodes.Daily ? h.PeriodStart == o.ForDate : h.Action == HaltCodes.StopNew && RampAssessor.WeekStart(h.PeriodStart) == RampAssessor.WeekStart(o.ForDate)));
        bool OverAlloc(StoredOutcome o) => o.Outcome.Outcome == OutcomeCodes.OverAllocation || o.Outcome.Flags.Contains(OutcomeCodes.FlagOverAllocation);

        var sessions = new List<LiveSessionFact>();
        int afterHaltUncleared = 0, intraday = 0, evidenceUnavailable = 0, evidenceSessions = 0, unknownOld = 0, doubled = 0, disagreements = 0, discretionary = 0;
        var captureRan = false;
        for (var i = 0; i < window.Count; i++)
        {
            var s = window[i];
            var entries = s.Outcomes.Where(o => o.Outcome.EntryQty > 0).ToList();
            var byOutcome = s.Executions.ToDictionary(x => x.Row.OutcomeId);
            var day = entries.Where(o => byOutcome.TryGetValue(o.Id, out var x) && x.Row.Style == "intraday").ToList();
            intraday += day.Count;
            var missing = day.Count(o => o.Outcome.Protection == OutcomeCodes.Unavailable || o.Outcome.OrderEvidence == OutcomeCodes.EvidenceUnavailable);
            evidenceUnavailable += missing;
            evidenceSessions += missing > 0 ? 1 : 0;
            var after = entries.Where(AfterHalt).ToList();
            afterHaltUncleared += after.Count(o => !clears.Any(c => c > o.Outcome.FirstEntryAt));
            var breaches = entries.Count(o => AfterHalt(o) || OverAlloc(o) || o.Outcome.Flags.Contains(OutcomeCodes.FlagUnprotected));
            unknownOld += i <= window.Count - 1 - LivePhaseLimits.UnknownGraceSessions ? s.Outcomes.Count(o => o.Outcome.Outcome == OutcomeCodes.Unknown) : 0;
            doubled += s.Fills.GroupBy(f => f.BrokerTradeId, StringComparer.Ordinal).Count(g => g.Count() > 1);
            captureRan |= s.Runs.Count > 0;
            var settle = s.Runs.Where(r => r.Kind == OutcomeCodes.KindSettle).OrderBy(r => r.StartedAt).LastOrDefault();
            disagreements += settle is not null && settle.Outcome != OutcomeCodes.RunOk ? 1 : 0;
            discretionary += s.Discretionary;

            var scaleSteps = s.Scales.Select(x => eventStep.GetValueOrDefault(x.RampEventId)).Where(x => x > 0).ToList();
            var step = scaleSteps.Count > 0 ? scaleSteps.Max() : StepAt(s.Date);
            double? factor = scaleSteps.Count == s.Scales.Count && scaleSteps.Count > 0 ? scaleSteps.Select(st => st <= factors.Count ? factors[st - 1] : RampSteps.Ceiling(st)).Min() : null;
            var holds = history.Count(e => e.Kind is RampEventKinds.Hold or RampEventKinds.StepDown && IndiaTime.Today(e.CreatedAt) == s.Date);
            var trips = s.Executions.Count(x => IsGraded(x.Row));
            var outcomes = s.Outcomes.GroupBy(o => o.Outcome.Outcome, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            sessions.Add(new LiveSessionFact(s.Date, s.Outcomes.Count, s.Fills.Count, s.Capture, [.. s.Scales.Select(x => x.Scale)], factor, step, outcomes, trips, allHalts.Count(h => h.PeriodStart == s.Date), holds, breaches));
        }

        var stepDowns = history.Where(e => e.Kind == RampEventKinds.StepDown && IndiaTime.Today(e.CreatedAt) >= start).Select(e => IndiaTime.Today(e.CreatedAt)).ToList();
        int Ago(DateOnly d)
        {
            var idx = sessions.FindLastIndex(s => s.Date <= d);
            return idx < 0 ? int.MaxValue : sessions.Count - 1 - idx;
        }

        var done = window.SelectMany(s => s.Executions).ToList();
        var graded = done.Where(x => IsGraded(x.Row)).ToList();
        var kindGroup = done.Where(x => x.Row.ReferenceKind != ReferenceKind.Unavailable && x.Row.SlipTotalR is not null).GroupBy(x => x.Row.ReferenceKind)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key == ReferenceKind.TicketLevel ? 0 : 1).FirstOrDefault();
        var allSettled = window.SelectMany(s => s.Outcomes).ToList();
        var pairs = await PairsAsync(account.Id, start, through, cancellationToken);
        var weeksWithEstimates = await EstimateWeeksAsync(start, through, cancellationToken);
        var capacity = await executions.CapacityScoresAsync(null, cancellationToken);
        var latest = await compliance.LatestPerKindAsync(cancellationToken);
        var setup = await SetupAsync(cancellationToken);

        var printed = Printed(graded, kindGroup, allSettled, discretionary, history, start, allHalts.Count(h => start <= h.PeriodStart), capacity.Rows);
        return new LivePhaseFacts
        {
            Through = through,
            WindowStart = start,
            ThroughIsTradingSession = throughIsSession,
            OneLiveAccount = oneLive,
            Sessions = sessions,
            GradedRoundTrips = graded.Count,
            Pairs = pairs,
            SlippageR = kindGroup is null ? [] : [.. kindGroup.Select(x => x.Row.SlipTotalR!.Value)],
            SlippageKind = kindGroup?.Key,
            HoldActive = state?.Hold ?? false,
            StepDownSessionsAgo = [.. stepDowns.Select(Ago)],
            StepDownsOrHolds = history.Count(e => e.Kind is RampEventKinds.Hold or RampEventKinds.StepDown && IndiaTime.Today(e.CreatedAt) >= start),
            AfterHaltUncleared = afterHaltUncleared,
            IntradayEntries = intraday,
            OrderEvidenceUnavailable = evidenceUnavailable,
            OrderEvidenceMissingSessions = evidenceSessions,
            WeeksWithEstimates = weeksWithEstimates,
            CapacityState = capacity.Week is null ? "empty" : capacity.Rows.FirstOrDefault(r => r.Scope == LiveEstimateMath.AccountScope)?.State,
            Compliance = [.. ComplianceKinds.All.Select(k => latest.FirstOrDefault(r => r.Record.Kind == k) is { } r ? new LiveComplianceFact(k, r.Record.DecidedOn, r.Record.NextReviewOn) : new LiveComplianceFact(k, null, null))],
            CaptureRan = captureRan,
            Disagreements = disagreements,
            UnknownOutcomesOld = unknownOld,
            DoubleJournaled = doubled,
            AllowList = AllowList(),
            Printed = printed,
            Setup = setup,
        };
    }

    private static bool IsGraded(NewLiveExecution r) => r.RiskPerShare is > 0 && r.ExitVwap is not null && r.ExitQty == r.FillQty;

    private LiveAllowListFact AllowList()
    {
        var pinned = PlanPhaseCheck.PinnedAllowList;
        var current = allowList.Entries();
        var added = current.Except(pinned, StringComparer.Ordinal).Count();
        var removed = pinned.Except(current, StringComparer.Ordinal).Count();
        var open = PlanPhaseCheck.RefusedProbes.Count(p => allowList.Allows(p.Method, p.Url));
        var ok = added == 0 && removed == 0 && open == 0;
        return new LiveAllowListFact(ok, ok
            ? $"the {pinned.Count.ToString(CultureInfo.InvariantCulture)} pinned entries and no other; all {PlanPhaseCheck.RefusedProbes.Count.ToString(CultureInfo.InvariantCulture)} order, position and fund probes refused"
            : $"{added.ToString(CultureInfo.InvariantCulture)} entries added, {removed.ToString(CultureInfo.InvariantCulture)} removed, {open.ToString(CultureInfo.InvariantCulture)} probes allowed");
    }

    private async ValueTask<LivePairFacts?> PairsAsync(Guid accountId, DateOnly start, DateOnly through, CancellationToken cancellationToken)
    {
        var result = await vsPaper.HandleAsync(new LiveVsPaperQuery(accountId, null, start, through, "gross", null, 1, LiveReportRules.MaxPageSize), cancellationToken);
        if (result.IsFailure || result.Value.State == "unavailable")
        {
            return null;
        }

        var v = result.Value;
        var s = v.Summary;
        var distinct = v.Pairs.Select(p => p.PlanItemId).Distinct().Count() == v.Pairs.Count && v.PairsTotal == v.Pairs.Count;
        return new LivePairFacts(
            v.PairsTotal, s.Verdict, s.Mean.Number, s.Lo95.Number, s.Hi95.Number, s.UpperOneSided95.Number, v.Excluded.Count,
            v.Excluded.Where(e => e.Reason == LiveReportRules.LiveOnly).Select(e => e.Date).Distinct().Count(), s.ChargesDiffMean.State == "value", distinct);
    }

    private async ValueTask<int> EstimateWeeksAsync(DateOnly start, DateOnly through, CancellationToken cancellationToken)
    {
        // The estimates run on Saturday, so the weeks that are due are those before the week of the check date.
        var current = RampAssessor.WeekStart(through);
        var count = 0;
        for (var i = 1; i <= LivePhaseLimits.EstimateWeeks; i++)
        {
            var week = current.AddDays(-7 * i);
            if (week.AddDays(6) < start)
            {
                break;
            }

            var (stored, rows) = await executions.SlippageEstimatesAsync(week, cancellationToken);
            count += stored == week && rows.Count > 0 ? 1 : 0;
        }

        return count;
    }

    private async ValueTask<List<LiveSetupItem>> SetupAsync(CancellationToken cancellationToken)
    {
        var list = new List<LiveSetupItem>();
        foreach (var id in ServiceIds)
        {
            var info = await services.GetAsync(id, cancellationToken);
            list.Add(new LiveSetupItem(id, info?.Enabled ?? false, "service"));
        }

        var current = await liveSettings.CurrentAsync(cancellationToken);
        list.Add(new LiveSetupItem("Live:Capture:Enabled", captureSettings.Enabled, "switch"));
        list.Add(new LiveSetupItem("Live:Capture:OrderBook:Enabled", captureSettings.OrderBookEnabled, "switch"));
        list.Add(new LiveSetupItem("live.auto_approve_tickets", current.AutoApproveTickets, "switch", false));
        list.Add(new LiveSetupItem("live.auto_ramp_up", current.AutoRampUp, "switch", false));
        return list;
    }

    private static List<LivePrintedLine> Printed(
        List<StoredLiveExecution> graded,
        IGrouping<string, StoredLiveExecution>? kind,
        List<StoredOutcome> outcomes,
        int discretionary,
        IReadOnlyList<StoredRampEvent> history,
        DateOnly start,
        int halts,
        IReadOnlyList<NewCapacityScore> capacity)
    {
        var lines = new List<LivePrintedLine>();
        var r = graded.Select(x => (double)((x.Row.Side == "buy" ? 1m : -1m) * (x.Row.ExitVwap!.Value - x.Row.FillVwap) / x.Row.RiskPerShare!.Value)).ToList();
        var bounds = OneSidedT.Bounds(r);
        lines.Add(new("realised live R, gross (never a criterion)", r.Count == 0 ? "empty: no graded round trip" : bounds is { } b ? $"n = {r.Count.ToString(CultureInfo.InvariantCulture)}, mean {F(b.Mean, 3)} R, one-sided 95 % bounds {F(b.Lower, 3)} to {F(b.Upper, 3)}" : $"n = {r.Count.ToString(CultureInfo.InvariantCulture)}, insufficient for an interval"));
        var charges = graded.Select(x => x.Row.LiveCharges is { } c ? (double?)((double)c / (double)(x.Row.RiskPerShare!.Value * x.Row.FillQty)) : null).ToList();
        var known = charges.Where(c => c is not null).Select(c => c!.Value).Order().ToList();
        lines.Add(new("charges per round trip as a share of 1 R", known.Count == 0 ? "unavailable: no recorded charges" : $"median {F(known[known.Count / 2], 3)} on {known.Count.ToString(CultureInfo.InvariantCulture)} of {graded.Count.ToString(CultureInfo.InvariantCulture)} round trips"));
        lines.Add(new("discretionary share", graded.Count + discretionary == 0 ? "empty" : $"{discretionary.ToString(CultureInfo.InvariantCulture)} symbol-days of {(graded.Count + discretionary).ToString(CultureInfo.InvariantCulture)}"));
        foreach (var cls in new[] { OutcomeCodes.MissedNoOrder, OutcomeCodes.MissedUnfilled, OutcomeCodes.Rejected, OutcomeCodes.MissedUnknown })
        {
            var rows = outcomes.Where(o => o.Outcome.Outcome == cls).ToList();
            var missed = rows.Where(o => o.Outcome.MissedR is not null).Sum(o => o.Outcome.MissedR!.Value);
            lines.Add(new($"missed trigger {cls}", OutcomeCodes.CarriesMissedR(cls) ? $"{rows.Count.ToString(CultureInfo.InvariantCulture)} tickets, {F(missed, 2)} plan R" : $"{rows.Count.ToString(CultureInfo.InvariantCulture)} tickets, cost in plan R not measured for this class"));
        }

        var moves = history.Where(e => IndiaTime.Today(e.CreatedAt) >= start || e.Kind == RampEventKinds.SetStep).OrderBy(e => e.Id).Select(e => $"{e.Kind} S{e.ToStep.ToString(CultureInfo.InvariantCulture)} {IndiaTime.Today(e.CreatedAt):yyyy-MM-dd}");
        lines.Add(new("ramp history", string.Join("; ", moves)));
        lines.Add(new("halts opened in the window", halts.ToString(CultureInfo.InvariantCulture)));
        var cap = capacity.FirstOrDefault(c => c.Scope == LiveEstimateMath.AccountScope);
        lines.Add(new("capacity score", cap is null ? "empty" : cap.Score is { } sc ? $"{F(sc, 3)} (full {cap.NFull.ToString(CultureInfo.InvariantCulture)}, partial {cap.NPartial.ToString(CultureInfo.InvariantCulture)}, rejected {cap.NRejected.ToString(CultureInfo.InvariantCulture)}, queue miss {cap.NQueueMiss.ToString(CultureInfo.InvariantCulture)}, price miss {cap.NPriceMiss.ToString(CultureInfo.InvariantCulture)} of {cap.NOrders.ToString(CultureInfo.InvariantCulture)} orders)" : $"{cap.State}: {cap.NOrders.ToString(CultureInfo.InvariantCulture)} orders"));
        if (kind is not null)
        {
            foreach (var g in kind.GroupBy(x => x.Row.Bucket, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var v = g.Select(x => x.Row.SlipTotalR!.Value).ToList();
                var bb = OneSidedT.Bounds(v);
                lines.Add(new($"slippage bucket {g.Key} ({kind.Key})", bb is { } x ? $"n = {v.Count.ToString(CultureInfo.InvariantCulture)}, mean {F(x.Mean, 3)} R, one-sided 95 % bounds {F(x.Lower, 3)} to {F(x.Upper, 3)}" : $"n = {v.Count.ToString(CultureInfo.InvariantCulture)}, insufficient"));
            }
        }

        return lines;
    }

    private static string F(double v, int digits) => v.ToString("0." + new string('0', digits), CultureInfo.InvariantCulture);
}
