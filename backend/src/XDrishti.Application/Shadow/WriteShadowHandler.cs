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
/// It reads the plan record and the paper epoch and writes only <c>paper.shadow_*</c>: no ticket moves, no order, no broker call, nothing the plan or the paper count reads changes.
/// </summary>
internal sealed class WriteShadowHandler(
    IShadowSettings settings,
    IPlanStore plans,
    IShadowStore store,
    IPaperStore paper,
    IGovernanceStore governance,
    ITradingCalendar calendars,
    TimeProvider time,
    ILogger<WriteShadowHandler> logger) : ICommandHandler<WriteShadowCommand, ShadowWriteOutcome>
{
    private const int PlanLookback = 20;

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
        var headDate = head?.SessionDate;
        Context? context = null;
        foreach (var date in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (headDate is { } h && date <= h)
            {
                continue; // the chain only moves forward: an earlier session is never added
            }

            NewShadowSession? session;
            if (ShadowCutoff.IsOpened(date, now))
            {
                session = new NewShadowSession(date, "gap", previous, ShadowDigest.Next(previous, []), []);
            }
            else
            {
                context ??= await ReadContextAsync(cancellationToken);
                session = await BuildAsync(date, previous, calendar, context, total, cancellationToken);
            }

            if (session is null || !await store.WriteSessionAsync(session, cancellationToken))
            {
                continue;
            }

            previous = session.Digest;
            headDate = date;
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
        return Result.Success(new ShadowWriteOutcome(true, total.Sessions, total.Rows, total.EdgeRows, total.Gaps, total.Dropped));
    }

    /// <summary>The sessions to look at, oldest first: for a run without a date, every trading session that opened after the newest stored one (each is a gap) and every session of a recent stored plan that has not opened.</summary>
    private async Task<List<DateOnly>> SessionsAsync(DateOnly? asked, ShadowHead? head, DateTimeOffset now, TradingCalendar calendar, CancellationToken cancellationToken)
    {
        if (asked is { } one)
        {
            return [one];
        }

        var result = new SortedSet<DateOnly>();
        if (head is not null)
        {
            // A first run has no history to be a gap in: the record starts with the first session it writes.
            for (var day = head.SessionDate.AddDays(1); day <= IndiaTime.Today(now); day = day.AddDays(1))
            {
                if (calendar.IsTradingDay(day) && ShadowCutoff.IsOpened(day, now))
                {
                    result.Add(day);
                }
            }
        }

        foreach (var plan in await plans.ListAsync(null, null, PlanLookback, cancellationToken))
        {
            if (!ShadowCutoff.IsOpened(plan.Plan.ForDate, now) && calendar.IsTradingDay(plan.Plan.ForDate))
            {
                result.Add(plan.Plan.ForDate);
            }
        }

        return [.. result];
    }

    private async Task<Context> ReadContextAsync(CancellationToken cancellationToken)
    {
        // Read only: the epoch in force and the frozen fingerprint of the newest full snapshot; neither is created or changed here.
        var epoch = await paper.CurrentEpochAsync(cancellationToken);
        var snapshot = await governance.LatestSnapshotAsync(true, cancellationToken);
        return new Context(epoch?.Id, snapshot?.Hash);
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

        return new NewShadowSession(date, "ok", previous, ShadowDigest.Next(previous, entries.Select(e => e.RowHash)), entries);
    }

    private sealed record Context(long? EdgeEpoch, string? ConfigHash);

    private sealed class Totals
    {
        public int Sessions { get; set; }

        public int Rows { get; set; }

        public int EdgeRows { get; set; }

        public int Gaps { get; set; }

        public int Dropped { get; set; }
    }
}
