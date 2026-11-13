using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Paper;
using XDrishti.Application.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Shadow;

namespace XDrishti.Application.Shadow;

/// <summary>
/// Writes the shadow record (spec CF-08, ADR 0211). For each session of a stored plan that has not opened it turns every plan item of any group into a row from the STORED plan record (levels, group, and the advisory figure when the plan holds one; no recomputation),
/// and chains the session's digest after the newest one. A session that has opened is never written: it gets a gap row, and nothing is back-filled. Idempotent: a session already stored is left alone. With <c>paper.shadow.enabled</c> OFF it writes nothing at all.
/// The first session of an edge epoch registers the epoch's evidence set first (spec AM-01 GAP-09: the versions of the eligible cells, <c>auto</c>, in the same transaction, before the entries); with no set stored and none nameable it writes no session at all (fail closed, logged).
/// It reads the plan record and the paper epoch and writes only <c>paper.shadow_*</c> (and the set with its ledger rows): no ticket moves, no order, no broker call, nothing the plan or the paper count reads changes.
/// </summary>
internal sealed class WriteShadowHandler(
    IShadowSettings settings,
    IPlanStore plans,
    IShadowStore store,
    IEvidenceSetStore evidenceSets,
    IPaperStore paper,
    IGovernanceStore governance,
    IConfigSnapshotSource configuration,
    ITradingCalendar calendars,
    ShadowScorer scorer,
    ShadowOptions options,
    TimeProvider time,
    ILogger<WriteShadowHandler> logger) : ICommandHandler<WriteShadowCommand, ShadowWriteOutcome>
{
    private const int PlanLookback = 20;

    // shortcut: a stored plan dated more than 10 days after today is ignored when choosing the sessions to look at (a guard against listing years of days for a mistaken plan date); real plans are for the next session.
    private const int MaxDaysAhead = 10;

    public async ValueTask<Result<ShadowWriteOutcome>> HandleAsync(WriteShadowCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await settings.IsEnabledAsync(cancellationToken))
        {
            ShadowLog.SwitchedOff(logger);
            return Result.Success(new ShadowWriteOutcome(false, 0, 0, 0, 0, 0));
        }

        var now = time.GetUtcNow();
        var calendar = await calendars.GetAsync(cancellationToken);
        if (command.Session is { } asked && !calendar.IsTradingDay(asked))
        {
            return Result.Failure<ShadowWriteOutcome>(PaperErrors.Invalid("session", "A shadow record can be written only for a trading session."));
        }

        var head = await store.HeadAsync(cancellationToken);
        var sessions = await SessionsAsync(command.Session, head, now, calendar, cancellationToken);
        var total = new Totals();
        string? previous = head?.Digest;
        Context? context = null;
        var setMissing = false;
        foreach (var date in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NewShadowSession? session;
            if (ShadowCutoff.IsOpened(date, now))
            {
                session = new NewShadowSession(date, "gap", previous, ShadowDigest.Next(previous, date, "gap", []), []);
            }
            else
            {
                context ??= await ReadContextAsync(cancellationToken);
                if (!context.SetAvailable)
                {
                    // Fail closed: without an evidence set no session is written, so nothing is counted either; a later run tries again, and a session that opens meanwhile is recorded as a gap.
                    setMissing = true;
                    ShadowLog.EvidenceSetMissing(logger);
                    break;
                }

                session = await BuildAsync(date, previous, calendar, context, total, cancellationToken);
            }

            // The chain only moves forward, so a session that cannot be stored now ends the run: a later one stored first would leave this one (still unopened, so it cannot be a gap) without a row for ever.
            if (session is null || !await store.WriteSessionAsync(session, cancellationToken))
            {
                break;
            }

            if (session.EvidenceSet is { } registered && context is not null)
            {
                context.PendingSet = null; // only the epoch's first session carries the set
                // The store inserts it only when the epoch has none yet (an owner's set stored meanwhile wins), so say "registered" only when the stored set is ours.
                if (await evidenceSets.CurrentAsync(registered.EdgeKey, cancellationToken) is { Mode: EvidenceSetRules.ModeAuto } stored && stored.SetHash == registered.SetHash)
                {
                    ShadowLog.EvidenceSetRegistered(logger, registered.Versions.Count);
                }
            }

            previous = session.Digest;
            if (session.Status == "gap")
            {
                total.Gaps++;
            }
            else
            {
                total.Sessions++;
                total.Rows += session.Entries.Count;
                total.EdgeRows += session.Entries.Count(e => e.CountsForEdge);
            }
        }

        ShadowLog.Written(logger, total.Sessions, total.Rows, total.EdgeRows, total.Gaps, total.Dropped);
        return Result.Success(new ShadowWriteOutcome(true, total.Sessions, total.Rows, total.EdgeRows, total.Gaps, total.Dropped, setMissing));
    }

    /// <summary>
    /// The trading sessions to look at, oldest first, with none left out between the newest stored session and the last one wanted: each that has opened becomes a gap, each that has not opened is written from its plan, and the run stops at the first that cannot be written
    /// (a session the chain skipped could never be added). A run without a date looks as far as the furthest stored plan that has not opened. A first run has no history to be a gap in: it starts at the first session that has not opened, or at the asked one when that has opened.
    /// </summary>
    private async Task<List<DateOnly>> SessionsAsync(DateOnly? asked, ShadowHead? head, DateTimeOffset now, TradingCalendar calendar, CancellationToken cancellationToken)
    {
        var today = IndiaTime.Today(now);
        var last = asked ?? today;
        if (asked is null)
        {
            foreach (var plan in await plans.ListAsync(null, null, PlanLookback, cancellationToken))
            {
                if (!ShadowCutoff.IsOpened(plan.Plan.ForDate, now) && plan.Plan.ForDate <= today.AddDays(MaxDaysAhead) && plan.Plan.ForDate > last)
                {
                    last = plan.Plan.ForDate;
                }
            }
        }

        DateOnly first;
        if (head is not null)
        {
            first = head.SessionDate.AddDays(1);
        }
        else if (asked is { } one && ShadowCutoff.IsOpened(one, now))
        {
            first = one;
        }
        else
        {
            first = today;
            while (first <= last && !(calendar.IsTradingDay(first) && !ShadowCutoff.IsOpened(first, now)))
            {
                first = first.AddDays(1);
            }
        }

        var result = new List<DateOnly>();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            if (calendar.IsTradingDay(day))
            {
                result.Add(day);
            }
        }

        return result;
    }

    private async Task<Context> ReadContextAsync(CancellationToken cancellationToken)
    {
        // Read only: the epoch in force and the frozen fingerprint of the newest full snapshot; neither is created or changed here.
        var epoch = await paper.CurrentEpochAsync(cancellationToken);
        var snapshot = await governance.LatestSnapshotAsync(true, cancellationToken);
        var key = EvidenceSetRules.EpochKey(epoch?.Id);
        if (await evidenceSets.CurrentAsync(key, cancellationToken) is not null)
        {
            return new Context(epoch?.Id, snapshot?.Hash, true, null); // the epoch has a set (an owner's, or the automatic one of an earlier session)
        }

        // No set yet: the default rule, from a fresh configuration reading (no stored snapshot is needed), fixed before any forward result of the epoch exists. Nothing nameable, or a partial reading, means no write.
        var versions = await EvidenceSetDefault.ReadAsync(configuration, cancellationToken);
        return versions is null
            ? new Context(epoch?.Id, snapshot?.Hash, false, null)
            : new Context(epoch?.Id, snapshot?.Hash, true, new NewEvidenceSet(key, versions, EvidenceSetRules.SetHash(versions), EvidenceSetRules.ModeAuto, EvidenceSetRules.AutoActor, null));
    }

    private async Task<NewShadowSession?> BuildAsync(DateOnly date, string? previous, TradingCalendar calendar, Context context, Totals total, CancellationToken cancellationToken)
    {
        var plan = (await plans.ListAsync(date, null, PlanLookback, cancellationToken))
            .Where(p => p.Plan.ForDate == date && p.State != PlanState.Draft)
            .OrderByDescending(p => p.Plan.Version).ThenByDescending(p => p.Id)
            .FirstOrDefault();
        if (plan is null)
        {
            return null; // no plan to shadow; if none arrives before the open the next run records the gap
        }

        var entries = new List<ShadowEntry>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in ShadowPlanFacts.InKeyOrder(await plans.ItemsAsync(plan.Id, cancellationToken)))
        {
            var item = row.Item;
            var cell = CellId.TryParse(item.Evidence.CellKey);
            var decisionDay = cell is not null && TicketReplayer.IsDaily(cell) ? TicketReplayer.Before(calendar, date) : date;
            var key = ShadowTradeKey.For(item.StrategyId, item.Symbol, item.Side, decisionDay);
            if (!keys.Add(key))
            {
                total.Dropped++;
                continue;
            }

            // Features, model id and the feature version are null: the plan record holds none (ADR 0211); the probability is the plan's own advisory figure when it has one.
            entries.Add(new ShadowEntry(
                date, plan.Id, row.Id, key, item.Group.Code(), item.Evidence.CellKey, item.StrategyVersionHash, item.Symbol, item.Side,
                item.Levels?.EntryRef, item.Levels?.Stop, item.Levels?.Target, item.Validity, context.ConfigHash, null, ShadowPlanFacts.AdvisoryPWin(item), null, null,
                ShadowPlanFacts.CountsForEdge(item), context.EdgeEpoch));
        }

        // CF-10e: the models' scores of the rows go in the same transaction; a scoring that fails must not cost the session (the chain cannot be back-filled), so the session is then written without scores
        IReadOnlyList<NewShadowScore> scores = [];
        if (time.GetUtcNow() >= ShadowCutoff.For(date).AddMinutes(-options.ScoringDeadlineMinutesBeforeCutoff))
        {
            ShadowLog.ScoringTooLate(logger, options.ScoringDeadlineMinutesBeforeCutoff);
        }
        else
        {
            // scoring runs against the deadline itself, not only against its start: a slow night is cut off at the deadline and the session is written without scores
            using var deadline = new CancellationTokenSource(ShadowCutoff.For(date).AddMinutes(-options.ScoringDeadlineMinutesBeforeCutoff) - time.GetUtcNow(), time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            try
            {
                scores = await scorer.ScoreAsync(date, calendar, entries, linked.Token);
            }
#pragma warning disable CA1031 // The scores are extra evidence; the session itself must still be written before the open. A real shutdown (the caller's token) is rethrown by the filter; the deadline and any other failure leave the session without scores. Only a count-free event is logged (the shadow log carries counts only).
            catch (Exception failure) when (failure is not OutOfMemoryException && !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                ShadowLog.ScoringFailed(logger);
            }
        }

        return new NewShadowSession(date, "ok", previous, ShadowDigest.Next(previous, date, "ok", entries.Select(e => e.RowHash)), entries, scores, context.PendingSet);
    }

    private sealed class Context(long? edgeEpoch, string? configHash, bool setAvailable, NewEvidenceSet? pendingSet)
    {
        public long? EdgeEpoch { get; } = edgeEpoch;

        public string? ConfigHash { get; } = configHash;

        /// <summary>Whether the epoch has a set or can be given the default one.</summary>
        public bool SetAvailable { get; } = setAvailable;

        /// <summary>The automatic set the next ok session stores with its entries; null once stored or when the epoch already has one.</summary>
        public NewEvidenceSet? PendingSet { get; set; } = pendingSet;
    }

    private sealed class Totals
    {
        public int Sessions { get; set; }

        public int Rows { get; set; }

        public int EdgeRows { get; set; }

        public int Gaps { get; set; }

        public int Dropped { get; set; }
    }
}
