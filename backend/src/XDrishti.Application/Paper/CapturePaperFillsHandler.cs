using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Paper;

/// <summary>
/// Captures the paper trades of armed tickets (spec P10-01, ADR 0116). For each closed session of the epoch it takes the tickets whose trail has an <c>armed</c> row, walks each through the SAME replay as grading (<see cref="TicketReplayer"/>), and per allocation
/// (account) stores an entry at the owner's quantity, its exit once the replay closes the trade, or a skip for a final fact. Pending replays are not stored; the session's row counts them and the next run tries again. Every session from the epoch's start
/// to the last closed one gets a coverage row, empty ones too. Insert-only and idempotent. It reads the plan record and writes only <c>paper.*</c>: no ticket changes state, no order, no broker call, no approval.
/// </summary>
internal sealed class CapturePaperFillsHandler(
    IPlanStore plans,
    IPaperStore paper,
    IEpochGate gate,
    IGradeSettings settings,
    ICostSheetStore costSheets,
    ITradingCalendar calendars,
    TicketReplayer replayer,
    TimeProvider time,
    ILogger<CapturePaperFillsHandler> logger) : ICommandHandler<CapturePaperFillsCommand, PaperCaptureOutcome>
{
    private const int MaxSessionsWithWork = 30;

    private const string CapitalSource = "Planning:Grading:Capital";

    private static readonly TicketState[] Watched = [TicketState.Armed, TicketState.Expired, TicketState.Triggered, TicketState.Closed];

    public async ValueTask<Result<PaperCaptureOutcome>> HandleAsync(CapturePaperFillsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = time.GetUtcNow();
        var calendar = await calendars.GetAsync(cancellationToken);
        var lastClosed = calendar.LastClosedSession(now);
        if (command.Date is { } d && (d > lastClosed || !calendar.IsTradingDay(d)))
        {
            return Result.Failure<PaperCaptureOutcome>(PaperErrors.Invalid("date", "A session can be captured only after it has closed, and only a trading session."));
        }

        var costs = await ReplayCosts.ResolveAsync(settings, costSheets, cancellationToken);
        if (costs.IsFailure)
        {
            PaperLog.SheetMissing(logger, settings.CostSheetId ?? string.Empty);
            return Result.Failure<PaperCaptureOutcome>(PaperErrors.SheetMissing(settings.CostSheetId ?? string.Empty));
        }

        var epoch = (await gate.ResolveAsync([], cancellationToken)).Epoch;
        if (command.Date is { } asked && asked < epoch.StartedOn)
        {
            return Result.Failure<PaperCaptureOutcome>(PaperErrors.Invalid("date", "The session is before the start of the epoch; nothing is back-filled."));
        }

        var sessions = await SessionsAsync(command.Date, epoch.StartedOn, lastClosed, calendar, cancellationToken);
        var statuses = (await gate.ResolveAsync(sessions, cancellationToken)).MeasureStatus;
        var total = new Totals();
        var withWork = 0;
        foreach (var session in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var began = time.GetUtcNow();
            Totals one;
            try
            {
                one = await CaptureSessionAsync(session, epoch.Id, statuses.GetValueOrDefault(session, "measured"), lastClosed, calendar, costs.Value, command.Manual, began, cancellationToken);
            }
#pragma warning disable CA1031 // One bad session is recorded as failed and the others still run; cancellation is rethrown by the filter.
            catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
            {
                var reason = Clip(failure.GetType().Name + ": " + failure.Message);
                PaperLog.SessionFailed(logger, session, reason);
                one = new Totals { Failed = 1 };
                await TryRecordFailureAsync(epoch.Id, session, began, command.Manual, reason, cancellationToken);
            }

            total.Add(one);
            if (one.Seen > 0 && ++withWork >= MaxSessionsWithWork && command.Date is null)
            {
                break;
            }
        }

        PaperLog.Done(logger, total.Sessions, total.Entries, total.Exits, total.Skips, total.Pending, total.Failed);
        return Result.Success(new PaperCaptureOutcome(total.Sessions, total.Seen, total.Entries, total.Exits, total.Skips, total.Pending, total.Integrity, total.Failed));
    }

    private async Task<List<DateOnly>> SessionsAsync(DateOnly? date, DateOnly start, DateOnly lastClosed, TradingCalendar calendar, CancellationToken cancellationToken)
    {
        if (date is { } one)
        {
            return [one];
        }

        var coverage = await paper.CoverageAsync(cancellationToken);
        var result = new List<DateOnly>();
        for (var day = start; day <= lastClosed; day = day.AddDays(1))
        {
            if (calendar.IsTradingDay(day) && (!coverage.Covered.Contains(day) || coverage.Revisit.Contains(day)))
            {
                result.Add(day);
            }
        }

        return result;
    }

    private async Task<Totals> CaptureSessionAsync(DateOnly session, long epochId, string measure, DateOnly lastClosed, TradingCalendar calendar, ResolvedCosts costs, bool manual, DateTimeOffset began, CancellationToken cancellationToken)
    {
        var rows = (await plans.ListAsync(session, null, 20, cancellationToken)).Where(p => p.Plan.ForDate == session).ToList();
        var stored = rows.Count == 0 ? new Dictionary<(long, Guid), StoredPair>() : new Dictionary<(long, Guid), StoredPair>(await paper.PairsAsync([.. rows.Select(r => r.Id)], cancellationToken));
        var entries = new List<NewPaperEntry>();
        var exits = new List<NewPaperExit>();
        var skips = new List<PaperSkip>();
        var accounts = new Dictionary<Guid, EpochAccount>();
        int seen = 0, pending = 0, integrity = 0;
        foreach (var plan in rows)
        {
            var items = (await plans.ItemsAsync(plan.Id, cancellationToken)).Where(i => i.Item.Group != TicketGroup.Rejected && Watched.Contains(i.State)).ToList();
            if (items.Count == 0)
            {
                continue;
            }

            var history = await plans.HistoryAsync(plan.Id, cancellationToken);
            var approvals = await plans.ApprovalsAsync(plan.Id, cancellationToken);
            foreach (var item in items)
            {
                if (history.FirstOrDefault(e => e.ItemId == item.Id && e.To == TicketState.Armed.Code()) is not { } armed)
                {
                    continue;
                }

                TicketReplay? replay = null;
                foreach (var allocation in item.Item.Allocations.OrderBy(a => a.AccountId))
                {
                    seen++;
                    var pair = stored.GetValueOrDefault((item.Id, allocation.AccountId));
                    if (pair?.IsComplete == true)
                    {
                        continue;
                    }

                    var skip = Quantity(plan.Id, item, allocation, approvals, out var qty, out var approval);
                    if (skip is not null)
                    {
                        skips.Add(skip);
                        continue;
                    }

                    replay ??= await replayer.ReplayAsync(session, lastClosed, calendar, item.Item, costs.Choice, cancellationToken);
                    var step = Step(plan.Id, item, allocation, qty, approval!, armed.At, replay, pair, costs.Sheet);
                    pending += step.Pending ? 1 : 0;
                    integrity += step.Integrity ? 1 : 0;
                    if (step.Skip is not null)
                    {
                        skips.Add(step.Skip);
                    }

                    if (step.Entry is not null)
                    {
                        entries.Add(new NewPaperEntry(step.Entry, measure, step.Exit));
                        accounts.TryAdd(allocation.AccountId, new EpochAccount(allocation.AccountId, settings.Capital, CapitalSource));
                    }
                    else if (step.Exit is not null && pair?.EntryId is { } entryId)
                    {
                        exits.Add(new NewPaperExit(entryId, pair.Entry!.EntryAt, step.Exit));
                    }

                    if (step.Pending)
                    {
                        PaperLog.Pending(logger, plan.Id, item.Id, replay.Pending ?? step.Why);
                    }

                    if (step.Integrity)
                    {
                        PaperLog.Integrity(logger, plan.Id, item.Id);
                    }
                }
            }
        }

        var outcome = pending > 0 ? "partial" : "done";
        var finished = time.GetUtcNow();
        var run = new NewCaptureRun(session, began, finished, outcome, seen, pending, integrity, manual, manual ? "manual run" : "scheduled run");
        var written = await paper.WriteSessionAsync(new SessionWrite(epochId, [.. accounts.Values], entries, exits, skips, run), cancellationToken);
        PaperLog.Session(logger, session, seen, written.Entries, written.Exits, written.Skips, pending, integrity);
        return new Totals { Sessions = 1, Seen = seen, Entries = written.Entries, Exits = written.Exits, Skips = written.Skips, Pending = pending, Integrity = integrity };
    }

    /// <summary>The owner's quantity for the allocation, or the skip when the approval is missing or the stored modification cannot be read (never the larger size).</summary>
    private static PaperSkip? Quantity(long planId, PlanItemRow item, NewAllocation allocation, IReadOnlyList<ApprovalRow> approvals, out int qty, out ApprovalRow? approval)
    {
        qty = allocation.Quantity;
        approval = approvals.Where(a => a.ItemId == item.Id && a.Decision == ApprovalDecision.Approve.Code()).MaxBy(a => a.Id);
        if (approval is null)
        {
            return new PaperSkip(planId, item.Id, allocation.AccountId, SkipCodes.ApprovalMissing, "The ticket was armed but has no stored approval row.");
        }

        if (approvals.Where(a => a.ItemId == item.Id && a.Decision == ApprovalDecision.Modify.Code()).MaxBy(a => a.Id) is not { } modified)
        {
            return null;
        }

        var parsed = TicketModification.Parse(modified.Modifications);
        var change = parsed.IsSuccess ? parsed.Value.FirstOrDefault(c => c.AccountId == allocation.AccountId) : null;
        if (parsed.IsFailure || (change is not null && (change.Quantity < 1 || change.Quantity >= allocation.Quantity)))
        {
            return new PaperSkip(planId, item.Id, allocation.AccountId, SkipCodes.ModificationUnreadable, "The stored size change of the ticket cannot be read; the larger size is never used.");
        }

        qty = change?.Quantity ?? allocation.Quantity;
        return null;
    }

    private static StepResult Step(long planId, PlanItemRow item, NewAllocation allocation, int qty, ApprovalRow approval, DateTimeOffset armedAt, TicketReplay replay, StoredPair? pair, CostSheet? sheet)
    {
        PaperSkip Skip(string code, string detail) => new(planId, item.Id, allocation.AccountId, code, Clip(detail, 500));
        var hasEntry = pair?.Entry is not null;
        if (replay.Trade is null && replay.Grade is { } grade)
        {
            // A final fact without a trade: a stored entry that the replay now denies is an integrity fault, never overwritten.
            if (hasEntry)
            {
                return new StepResult { Integrity = true };
            }

            return grade.Status switch
            {
                GradeStatus.NotTriggered => new StepResult { Skip = Skip(SkipCodes.NotTriggered, grade.Detail) },
                _ when grade.Detail == TicketGrader.DroppedDetail => new StepResult { Skip = Skip(SkipCodes.DroppedBySizing, grade.Detail) },
                _ => new StepResult { Skip = Skip(SkipCodes.NotGradable, grade.Detail) },
            };
        }

        if (replay.Trade is not { } trade)
        {
            return new StepResult { Pending = !hasEntry, Why = replay.Pending ?? string.Empty };
        }

        var entryAt = DateTimeOffset.FromUnixTimeSeconds(trade.EntryEpochSeconds);
        var entryPrice = PaperPnl.Price(trade.EntryFilledPrice);
        if (hasEntry)
        {
            if (!pair!.Entry!.SameEntryAs(entryAt, entryPrice))
            {
                return new StepResult { Integrity = true };
            }

            return replay.Grade is null ? new StepResult() : ExitStep(trade, pair.Entry.Qty, sheet);
        }

        if (trade.Legs is not null)
        {
            return new StepResult { Skip = Skip(SkipCodes.LegsNotSupported, "The trade has several legs; only single-fill trades are captured.") };
        }

        if (!PaperEntry.EntersAfterArming(armedAt, entryAt))
        {
            return new StepResult { Skip = Skip(SkipCodes.EnteredBeforeArmed, "The replay's entry minute starts before the ticket was armed.") };
        }

        var cell = CellId.TryParse(item.Item.Evidence.CellKey);
        var entry = new PaperEntry(
            planId, item.Id, allocation.AccountId, item.Item.Symbol, item.Item.Side, item.Item.StrategyId, item.Item.StrategyVersionHash, item.Item.Evidence.CellKey, item.Item.Group.Code(), approval.Id, approval.Kind,
            allocation.Quantity, qty, armedAt, entryAt, entryPrice, PaperPnl.Price(trade.EntryPrice), PaperPnl.Price(trade.StopPrice), trade.TargetPrice is { } t ? PaperPnl.Price(t) : null,
            string.Equals(cell?.Style, "swing", StringComparison.OrdinalIgnoreCase), trade.Key, replay.RunKey ?? string.Empty, replay.EngineVersion ?? string.Empty);
        if (replay.Grade is null)
        {
            return new StepResult { Entry = entry };
        }

        var exit = ExitStep(trade, qty, sheet);
        return exit.Pending ? exit : new StepResult { Entry = entry, Exit = exit.Exit };
    }

    private static StepResult ExitStep(SimTrade trade, int qty, CostSheet? sheet)
    {
        var exit = PaperPnl.Exit(trade, qty, sheet);
        return exit.IsFailure ? new StepResult { Pending = true, Why = "the stored sheet has no rates for a leg's date: " + exit.Error.Code } : new StepResult { Exit = exit.Value };
    }

    private async Task TryRecordFailureAsync(long epochId, DateOnly session, DateTimeOffset began, bool manual, string reason, CancellationToken cancellationToken)
    {
        try
        {
            var run = new NewCaptureRun(session, began, time.GetUtcNow(), "failed", 0, 0, 0, manual, Clip(reason, 500));
            await paper.WriteSessionAsync(new SessionWrite(epochId, [], [], [], [], run), cancellationToken);
        }
#pragma warning disable CA1031 // The failure is already logged and returned; a second failure to record it must not hide the others.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            PaperLog.SessionFailed(logger, session, "the failure could not be recorded: " + failure.GetType().Name);
        }
    }

    private static string Clip(string text, int max = 300) => text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");

    private sealed class StepResult
    {
        public PaperEntry? Entry { get; init; }

        public PaperExit? Exit { get; init; }

        public PaperSkip? Skip { get; init; }

        public bool Pending { get; init; }

        public bool Integrity { get; init; }

        public string Why { get; init; } = string.Empty;
    }

    private sealed class Totals
    {
        public int Sessions { get; set; }

        public int Seen { get; set; }

        public int Entries { get; set; }

        public int Exits { get; set; }

        public int Skips { get; set; }

        public int Pending { get; set; }

        public int Integrity { get; set; }

        public int Failed { get; set; }

        public void Add(Totals o)
        {
            Sessions += o.Sessions;
            Seen += o.Seen;
            Entries += o.Entries;
            Exits += o.Exits;
            Skips += o.Skips;
            Pending += o.Pending;
            Integrity += o.Integrity;
            Failed += o.Failed;
        }
    }
}
