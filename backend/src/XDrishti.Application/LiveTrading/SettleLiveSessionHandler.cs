using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Planning;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The evening settle of the owner's live sessions (spec P11-01, ADR 0149). For each manual-live account and each session (one date, or the last seven days): (1) the history read of the day - delivery trades only, the mapper of ADR 0025 skips intraday ones - for
/// the charges of the fills it holds and for the agreement with the trade book; (2) the charges rows; (3) the plan grades of a finished session (<see cref="GradePlansCommand"/>, idempotent); (4) the matching of the fills to the approved tickets with
/// <see cref="LiveFillMatcher"/> and the one outcome of each ticket with <see cref="TicketOutcomeClassifier"/>, written once and final. A ticket whose plan grade is not stored yet gets no outcome and is tried again by the next settle; after five sessions it is <c>unknown</c>.
/// Order evidence is present only for a session with at least one poll that read both books (<c>ok</c>); otherwise every order-dependent field is <c>unavailable</c>. Idempotent. Nothing here reaches an order endpoint.
/// </summary>
internal sealed class SettleLiveSessionHandler(
    IAccountStore accounts,
    ILiveAccountAccess access,
    IBrokerTrades history,
    ILiveCaptureStore store,
    LiveTicketSource tickets,
    IGradeStore grades,
    ICommandHandler<GradePlansCommand, GradeOutcome> grading,
    ITradingCalendar calendars,
    ILiveCaptureSettings settings,
    TimeProvider time,
    ILogger<SettleLiveSessionHandler> logger) : ICommandHandler<SettleLiveSessionCommand, LiveSettleOutcome>
{
    public const int SweepDays = 7;
    public const int SessionsBeforeUnknown = 5;
    private const int MaxHistoryPages = 5;

    public async ValueTask<Result<LiveSettleOutcome>> HandleAsync(SettleLiveSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = time.GetUtcNow();
        var today = IndiaTime.Today(now);
        if (command.Date is { } d && d > today)
        {
            return Result.Failure<LiveSettleOutcome>(Error.Validation(LiveErrors.Invalid, "date must not be after today"));
        }

        var calendar = await calendars.GetAsync(cancellationToken);
        var dates = command.Date is { } one
            ? new List<DateOnly> { one }
            : [.. Enumerable.Range(0, SweepDays + 1).Select(i => today.AddDays(i - SweepDays)).Where(calendar.IsTradingDay)];
        var live = (await accounts.ListActiveAsync(cancellationToken)).Where(a => a.Trading is { Enabled: true, Mode: TradingMode.ManualLive } && (command.AccountId is null || a.Id == command.AccountId)).ToList();
        int sessions = 0, written = 0, pending = 0, charges = 0, failed = 0;
        var notes = new List<string>();
        foreach (var account in live)
        {
            foreach (var date in dates)
            {
                var started = time.GetUtcNow();
                try
                {
                    var r = await SettleAsync(account, date, today, calendar, cancellationToken);
                    if (r is null)
                    {
                        continue;
                    }

                    sessions++;
                    written += r.Written;
                    pending += r.Pending;
                    charges += r.Charges;
                    notes.Add(r.Note);
                    await store.InsertRunAsync(new NewCaptureRun(account.Id, OutcomeCodes.KindSettle, date, started, time.GetUtcNow(), r.RunOutcome, r.Fills, 0, 0, 0), cancellationToken);
                }
                catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
                {
                    failed++;
                    LiveCaptureLog.Skipped(logger, "settle_failed");
                    await store.InsertRunAsync(new NewCaptureRun(account.Id, OutcomeCodes.KindSettle, date, started, time.GetUtcNow(), OutcomeCodes.RunUnavailable, 0, 0, 0, 0), cancellationToken);
                }
            }
        }

        LiveCaptureLog.Settle(logger, sessions, written, pending, charges);
        var summary = string.Create(CultureInfo.InvariantCulture, $"{sessions} sessions settled: {written} outcomes written, {pending} waiting for a plan grade, {charges} charge rows") + (notes.Count > 0 ? "; " + string.Join("; ", notes.Distinct()) : string.Empty) + (failed > 0 ? $"; {failed} failed" : string.Empty);
        return Result.Success(new LiveSettleOutcome(sessions, written, pending, charges, failed, summary));
    }

    private sealed record SessionResult(string RunOutcome, int Written, int Pending, int Charges, int Fills, string Note);

    private async Task<SessionResult?> SettleAsync(BrokerAccount account, DateOnly date, DateOnly today, TradingCalendar calendar, CancellationToken cancellationToken)
    {
        var approved = await tickets.ForDateAsync(date, account.Id, cancellationToken);
        var fills = await store.FillsAsync(account.Id, date, cancellationToken);
        if (approved.Count == 0 && fills.Count == 0)
        {
            return null;
        }

        var existing = (await store.OutcomesOfDateAsync(account.Id, date, cancellationToken)).Select(o => o.ItemId).ToHashSet();
        var (chargeCount, historyOutcome, note) = await ChargesAsync(account, date, fills, cancellationToken);
        if (date < today)
        {
            var graded = await grading.HandleAsync(new GradePlansCommand(date), cancellationToken); // idempotent: tickets with a grade keep it
            if (graded.IsFailure)
            {
                LiveCaptureLog.Skipped(logger, "grade_refused");
            }
        }

        var open = approved.Where(t => !existing.Contains(t.ItemId)).ToList();
        if (open.Count == 0)
        {
            return new SessionResult(historyOutcome, 0, 0, chargeCount, fills.Count, note);
        }

        var links = LiveFillMatcher.Match(date, [.. approved.Select(t => t.Journal)], fills);
        var runs = await store.RunsAsync(account.Id, date, cancellationToken);
        var evidence = runs.Any(r => r.Kind == OutcomeCodes.KindPoll && r.Outcome == OutcomeCodes.RunOk);
        var orders = evidence ? await store.LatestOrdersAsync(account.Id, date, cancellationToken) : null;
        var stored = await grades.ForPlansAsync([.. open.Select(t => t.PlanId).Distinct()], cancellationToken);
        var expired = calendar.TradingDaysBetween(date, today) >= SessionsBeforeUnknown;
        var batch = new List<NewTicketOutcome>();
        foreach (var ticket in open)
        {
            var entryFills = links.Where(l => l.Role == JournalRole.Entry && l.ItemId == ticket.ItemId).Select(l => l.Fill).ToList();
            var outcome = TicketOutcomeClassifier.Classify(
                Plan(ticket, stored),
                entryFills,
                orders?.Where(o => o.Symbol == ticket.Symbol && o.Side == ticket.Plan.Side).ToList(),
                GradeFacts(stored.GetValueOrDefault(ticket.ItemId)),
                Protection(ticket, entryFills, orders),
                settings.Tolerances,
                expired,
                entryFills.Count > 0 ? FirstEntryAt(entryFills) : null);
            if (outcome is not null)
            {
                batch.Add(new NewTicketOutcome(ticket.ItemId, account.Id, date, ticket.Plan.AllocatedQty, outcome));
            }
        }

        var written = batch.Count > 0 ? await store.InsertOutcomesAsync(batch, cancellationToken) : 0;
        return new SessionResult(historyOutcome, written, open.Count - batch.Count, chargeCount, fills.Count, note);
    }

    private string Protection(LiveTicket ticket, List<LiveFill> entryFills, IReadOnlyList<LiveOrder>? orders)
    {
        if (entryFills.Count == 0)
        {
            return OutcomeCodes.NotApplicable;
        }

        var last = entryFills.OrderBy(f => f.Date).ThenBy(f => f.Time ?? TimeOnly.MaxValue).Last();
        if (last.Time is null)
        {
            return OutcomeCodes.Unavailable;
        }

        var at = new DateTimeOffset(last.Date.ToDateTime(last.Time.Value), IndiaTime.Offset);
        return ProtectionCheck.Evaluate(at, ticket.Plan.Side, last.Product, entryFills.Sum(f => f.Qty), orders?.Where(o => o.Symbol == ticket.Symbol).ToList(), settings.ProtectWithin);
    }

    private static DateTimeOffset? FirstEntryAt(List<LiveFill> entryFills) =>
        entryFills.Where(f => f.Time is not null).OrderBy(f => f.Date).ThenBy(f => f.Time).Select(f => (DateTimeOffset?)new DateTimeOffset(f.Date.ToDateTime(f.Time!.Value), IndiaTime.Offset)).FirstOrDefault();

    private static TicketPlan Plan(LiveTicket ticket, IReadOnlyDictionary<long, StoredGrade> stored)
    {
        // The ticket's own levels first; the stored grade's simulated entry and stop when the ticket had none.
        var grade = stored.GetValueOrDefault(ticket.ItemId)?.Grade.Grade;
        return ticket.Plan with
        {
            PlanEntry = ticket.Plan.PlanEntry ?? (grade?.EntryPrice is { } e ? (decimal)e : null),
            PlanStop = ticket.Plan.PlanEntry is not null ? ticket.Plan.PlanStop : grade?.StopPrice is { } s ? (decimal)s : null,
        };
    }

    private static PlanGradeFacts GradeFacts(StoredGrade? stored) => stored?.Grade.Grade.Status switch
    {
        null => new PlanGradeFacts(PlanGradeFacts.Pending, null),
        GradeStatus.Graded => new PlanGradeFacts(PlanGradeFacts.Graded, stored.Grade.Grade.R),
        GradeStatus.NotTriggered => new PlanGradeFacts(PlanGradeFacts.NotTriggered, null),
        _ => new PlanGradeFacts(PlanGradeFacts.NotGradable, null),
    };

    /// <summary>The history read of one day for the charges and the agreement. A failed read is a partial settle (the charges stay unavailable); it never stops the outcomes.</summary>
    private async Task<(int Written, string RunOutcome, string Note)> ChargesAsync(BrokerAccount account, DateOnly date, IReadOnlyList<LiveFill> fills, CancellationToken cancellationToken)
    {
        var delivery = fills.Where(f => f.Product is "CNC" or "MTF" or "MARGIN").ToList();
        if (delivery.Count == 0)
        {
            return (0, OutcomeCodes.RunOk, string.Empty);
        }

        var (token, reason) = await access.TokenAsync(account.Id, cancellationToken);
        if (token is null)
        {
            LiveCaptureLog.HistoryUnavailable(logger, CaptureLiveSessionHandler.RunOutcome(reason ?? BrokerFailureKind.Rejected));
            return (0, OutcomeCodes.RunPartial, "charges unavailable (no usable token)");
        }

        var trades = new List<BrokerTrade>();
        for (var page = 0; page < MaxHistoryPages; page++)
        {
            var read = await history.FetchPageAsync(token, date, date, page, cancellationToken);
            if (!read.IsSuccess)
            {
                LiveCaptureLog.HistoryUnavailable(logger, CaptureLiveSessionHandler.RunOutcome(read.Failure!.Kind));
                return (0, OutcomeCodes.RunPartial, "charges unavailable (the history read failed)");
            }

            if (read.Value.Rows == 0 || read.Value.NoData)
            {
                break;
            }

            trades.AddRange(read.Value.Trades);
        }

        var byId = trades.GroupBy(t => t.TradeId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var known = await store.ChargesAsync(account.Id, date, cancellationToken);
        var rows = delivery.Where(f => !known.ContainsKey(f.Id) && byId.ContainsKey(f.BrokerTradeId)).Select(f => (f.Id, byId[f.BrokerTradeId].Charges)).ToList();
        var written = rows.Count > 0 ? await store.InsertChargesAsync(rows, cancellationToken) : 0;
        var matched = delivery.Count(f => byId.ContainsKey(f.BrokerTradeId));
        var missing = delivery.Count - matched;
        LiveCaptureLog.HistoryAgreement(logger, matched, missing, written);
        return (written, OutcomeCodes.RunOk, missing == 0 ? "history agrees with the trade book" : $"history misses {missing} of {delivery.Count} delivery fills of the trade book");
    }
}
