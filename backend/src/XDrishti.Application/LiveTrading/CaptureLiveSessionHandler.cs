using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// One poll of the owner's day at Dhan (spec P11-01, ADR 0149): for each manual-live account the trade book (every fill of every product) and, when <c>Live:Capture:OrderBook:Enabled</c> is on, the order book are read - GET only, nothing is placed, modified or
/// cancelled - and stored insert-only (the same trade seen by ten polls is one row; an order's status revision is stored once). Not outside the window (09:10 to 15:40 India time) or on a non-trading day, unless the owner polls by hand. A rate limit is a quiet
/// retry, an expired token is <c>token_expired</c>, never an empty day. The run is recorded: <c>ok</c> when both books were read, <c>partial</c> when the order book was not (switch off or failed: every order-dependent field is then unavailable).
/// </summary>
internal sealed class CaptureLiveSessionHandler(
    IAccountStore accounts,
    ILiveAccountAccess access,
    IBrokerTradeBook tradeBook,
    IBrokerOrderBook orderBook,
    ILiveCaptureStore store,
    ITradeReferenceData reference,
    ITradingCalendar calendars,
    ILiveCaptureSettings settings,
    TimeProvider time,
    ILogger<CaptureLiveSessionHandler> logger) : ICommandHandler<CaptureLiveSessionCommand, LiveCaptureOutcome>
{
    public async ValueTask<Result<LiveCaptureOutcome>> HandleAsync(CaptureLiveSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = time.GetUtcNow();
        var today = IndiaTime.Today(now);
        if (!command.Manual)
        {
            var calendar = await calendars.GetAsync(cancellationToken);
            var clock = TimeOnly.FromDateTime(IndiaTime.ToIst(now).DateTime);
            if (!calendar.IsTradingDay(today) || clock < CaptureHealth.WindowStart || clock > CaptureHealth.WindowEnd)
            {
                LiveCaptureLog.Skipped(logger, "outside_window");
                return Result.Success(new LiveCaptureOutcome(0, 0, 0, 0, 0, "Outside the capture window (a trading day, 09:10 to 15:40 India time)"));
            }
        }

        var live = (await accounts.ListActiveAsync(cancellationToken)).Where(a => a.Trading is { Enabled: true, Mode: TradingMode.ManualLive } && (command.AccountId is null || a.Id == command.AccountId)).ToList();
        int ok = 0, failed = 0, fillsNew = 0, ordersNew = 0;
        var problems = new List<string>();
        foreach (var account in live)
        {
            var (outcome, f, o) = await PollAsync(account, today, cancellationToken);
            fillsNew += f;
            ordersNew += o;
            if (outcome is OutcomeCodes.RunOk or OutcomeCodes.RunPartial)
            {
                ok++;
            }
            else
            {
                failed++;
                problems.Add($"{outcome}: {account.Name}");
            }

            LiveCaptureLog.Poll(logger, outcome, f, o);
        }

        if (live.Count == 0)
        {
            return Result.Success(new LiveCaptureOutcome(0, 0, 0, 0, 0, "No manual-live account"));
        }

        var summary = $"{live.Count} {(live.Count == 1 ? "account" : "accounts")} polled: {fillsNew} new fills, {ordersNew} new order revisions" + (problems.Count > 0 ? "; " + string.Join("; ", problems) : string.Empty);
        return Result.Success(new LiveCaptureOutcome(live.Count, ok, failed, fillsNew, ordersNew, summary));
    }

    private async ValueTask<(string Outcome, int FillsNew, int OrdersNew)> PollAsync(BrokerAccount account, DateOnly today, CancellationToken cancellationToken)
    {
        var started = time.GetUtcNow();
        string outcome;
        int fillsSeen = 0, fillsNew = 0, ordersSeen = 0, ordersNew = 0;
        var (token, reason) = await access.TokenAsync(account.Id, cancellationToken);
        if (token is null)
        {
            outcome = RunOutcome(reason ?? BrokerFailureKind.Rejected);
        }
        else
        {
            var trades = await tradeBook.ReadAsync(token, cancellationToken);
            if (!trades.IsSuccess)
            {
                outcome = RunOutcome(trades.Failure!.Kind);
            }
            else
            {
                fillsSeen = trades.Value.Seen;
                if (trades.Value.Dropped > 0)
                {
                    LiveCaptureLog.Dropped(logger, "trades", trades.Value.Dropped, trades.Value.Seen);
                }

                var (fills, _) = await LiveBookMapping.FillsAsync(reference, account.Id, trades.Value.Rows, cancellationToken);
                fillsNew = await store.InsertFillsAsync(fills, "trade_book", cancellationToken);
                outcome = OutcomeCodes.RunPartial;
                if (settings.OrderBookEnabled)
                {
                    var orders = await orderBook.ReadAsync(token, cancellationToken);
                    if (orders.IsSuccess)
                    {
                        ordersSeen = orders.Value.Seen;
                        if (orders.Value.Dropped > 0)
                        {
                            LiveCaptureLog.Dropped(logger, "orders", orders.Value.Dropped, orders.Value.Seen);
                        }

                        var (mapped, _) = await LiveBookMapping.OrdersAsync(reference, account.Id, orders.Value.Rows, cancellationToken);
                        ordersNew = await store.InsertOrdersAsync(mapped, cancellationToken);
                        outcome = OutcomeCodes.RunOk;
                    }
                }
            }
        }

        await store.InsertRunAsync(new NewCaptureRun(account.Id, OutcomeCodes.KindPoll, today, started, time.GetUtcNow(), outcome, fillsSeen, fillsNew, ordersSeen, ordersNew), cancellationToken);
        return (outcome, fillsNew, ordersNew);
    }

    internal static string RunOutcome(BrokerFailureKind kind) => kind switch
    {
        BrokerFailureKind.Unauthorized => OutcomeCodes.RunTokenExpired,
        BrokerFailureKind.RateLimited => OutcomeCodes.RunRateLimited,
        BrokerFailureKind.Rejected => OutcomeCodes.RunRejected,
        _ => OutcomeCodes.RunUnavailable,
    };
}
