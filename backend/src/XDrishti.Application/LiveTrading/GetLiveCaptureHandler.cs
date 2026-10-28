using System.Globalization;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The capture of one session of a manual-live account (spec P11-01): health, runs, one row per approved ticket (with the figures captured so far and, once the settle has written it, the final outcome), the unmatched and discretionary fills and the
/// evidence. Read only. 404 <c>live.not_found</c> for an account that is not a manual-live account, 422 <c>live.invalid</c> for a date in the future.
/// </summary>
internal sealed class GetLiveCaptureHandler(
    IAccountStore accounts,
    ILiveCaptureStore store,
    LiveTicketSource tickets,
    ILiveCaptureSettings settings,
    TimeProvider time) : IQueryHandler<LiveCaptureQuery, Result<LiveCaptureView>>
{
    /// <summary>The limits of the capture, stated on the screen and in the API (spec P11-01 risks).</summary>
    public static readonly IReadOnlyList<string> Limits =
    [
        "A price modification of a pending order does not change its status or filled quantity, so it is not stored: the first-seen price stands.",
        "Forever, super and GTT orders are not read: a swing position's stop reads protection_unknown.",
        "A fill at an instant with no ticket is discretionary by definition, including a correction of an earlier mistake.",
        "Rows are final: a fill that arrives after the settle is not added to an outcome that is already written.",
    ];

    public async ValueTask<Result<LiveCaptureView>> HandleAsync(LiveCaptureQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = time.GetUtcNow();
        var today = IndiaTime.Today(now);
        var date = query.Date ?? today;
        if (date > today)
        {
            return Result.Failure<LiveCaptureView>(Error.Validation(LiveErrors.Invalid, "date must not be after today"));
        }

        var account = await accounts.FindReadOnlyAsync(query.AccountId, cancellationToken);
        if (account is null || account.Trading.Mode != TradingMode.ManualLive)
        {
            return Result.Failure<LiveCaptureView>(Error.NotFound(LiveErrors.NotFound, "No such live account"));
        }

        var approved = await tickets.ForDateAsync(date, account.Id, cancellationToken);
        var fills = await store.FillsAsync(account.Id, date, cancellationToken);
        var runs = await store.RunDetailsAsync(account.Id, date, cancellationToken);
        var stored = (await store.OutcomesOfDateAsync(account.Id, date, cancellationToken)).ToDictionary(o => o.ItemId);
        var evidence = runs.Any(r => r.Kind == OutcomeCodes.KindPoll && r.Outcome == OutcomeCodes.RunOk);
        var orders = evidence ? await store.LatestOrdersAsync(account.Id, date, cancellationToken) : [];
        var links = LiveFillMatcher.Match(date, [.. approved.Select(t => t.Journal)], fills);
        var health = CaptureHealth.Evaluate([.. runs.Select(r => new CaptureRun(r.Kind, r.StartedAt, r.FinishedAt, r.Outcome))], date, now);
        var rows = approved.Select(t => Row(t, links, stored.GetValueOrDefault(t.ItemId), evidence, orders)).ToList();
        return Result.Success(new LiveCaptureView(
            date,
            health,
            [.. runs.Select(r => new LiveCaptureRunView(r.Kind, r.StartedAt, r.Outcome, r.FillsNew, r.OrdersNew))],
            rows,
            [.. links.Where(l => l.IsUnmatched).Select(l => FillView(l.Fill, l.Reason))],
            [.. links.Where(l => l.Role == JournalRole.Discretionary).Select(l => FillView(l.Fill, l.Reason))],
            new LiveCaptureEvidenceView(evidence ? OutcomeCodes.EvidencePresent : OutcomeCodes.EvidenceUnavailable, runs.Count(r => r.Kind == OutcomeCodes.KindPoll), fills.Count, orders.Count, settings.OrderBookEnabled, Limits)));
    }

    private static LiveCaptureTicketView Row(LiveTicket ticket, IReadOnlyList<LiveFillLink> links, StoredOutcome? outcome, bool evidence, IReadOnlyList<LiveOrder> orders)
    {
        var entry = links.Where(l => l.Role == JournalRole.Entry && l.ItemId == ticket.ItemId).Select(l => l.Fill).ToList();
        var qty = entry.Sum(f => f.Qty);
        decimal? avg = qty > 0 ? entry.Sum(f => f.Price * f.Qty) / qty : null;
        var ordered = evidence ? orders.Where(o => o.Symbol == ticket.Symbol && o.Side == ticket.Plan.Side).Select(o => (long?)o.Qty).Max() : null;
        var side = ticket.Plan.Side == Side.Buy ? "buy" : "sell";
        var o2 = outcome?.Outcome;
        return new LiveCaptureTicketView(
            ticket.ItemId,
            ticket.Symbol,
            side,
            ticket.Plan.AllocatedQty,
            o2?.OrderedQty ?? ordered,
            qty,
            avg is { } a ? Math.Round(a, 4) : null,
            o2?.Outcome,
            o2?.Flags ?? [],
            o2?.MissedR,
            o2?.OrderEvidence ?? (evidence ? OutcomeCodes.EvidencePresent : OutcomeCodes.EvidenceUnavailable),
            o2?.Protection ?? OutcomeCodes.Unavailable,
            o2?.RejectText);
    }

    private static LiveCaptureFillView FillView(LiveFill f, string reason) =>
        new(f.Symbol, f.Side == Side.Buy ? "buy" : "sell", f.Product, f.Qty, f.Price, f.Time?.ToString("HH:mm:ss", CultureInfo.InvariantCulture), reason);
}
