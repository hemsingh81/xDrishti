using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Selection;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Market.Context;

/// <summary>Writes the daily context of the gate basket (BK-08) for each of the latest closed sessions that has no row. Insert-once, idempotent, convergent: a day is computed from the closes up to that day and the members as of it, so a later run gives the row an earlier run would have.</summary>
public sealed record ComputeBasketContextCommand : ICommand;

/// <summary>What a run did: rows written, days left for a later run (the last closed session still lacks a member's bar), and why nothing ran (null when it did).</summary>
public sealed record BasketContextComputed(int Written, int Pending, string? Skipped);

internal sealed class ComputeBasketContextHandler(
    IBasketSettingsStore settings,
    IBasketQueries baskets,
    IBarQueries bars,
    IBasketContextStore store,
    ITradingCalendar tradingCalendar,
    TimeProvider clock,
    ILogger<ComputeBasketContextHandler> logger) : ICommandHandler<ComputeBasketContextCommand, BasketContextComputed>
{
    /// <summary>Why nothing ran: the gate reads the tracked set, so there is no gate basket.</summary>
    public const string NoGateBasket = "no_gate_basket";

    /// <summary>Why nothing ran: the chosen basket no longer exists.</summary>
    public const string BasketMissing = "basket_missing";

    /// <summary>Closed sessions looked back over for a day without a row (a missed night is caught up; older history is a separate backfill).</summary>
    public const int RecentSessions = 5;

    /// <summary>Calendar days of daily bars read before a session (252 sessions for the new-high figure, with slack).</summary>
    public const int LookbackDays = 400;

    public async ValueTask<Result<BasketContextComputed>> HandleAsync(ComputeBasketContextCommand command, CancellationToken cancellationToken)
    {
        var state = await settings.CurrentAsync(cancellationToken);
        if (state.Settings.GateBasketId is not { } basketId)
        {
            return Result.Success(new BasketContextComputed(0, 0, NoGateBasket));
        }

        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var lastClosed = calendar.LastClosedSession(clock.GetUtcNow());
        var days = new List<DateOnly>();
        for (var d = lastClosed; days.Count < RecentSessions && d > lastClosed.AddDays(-30); d = d.AddDays(-1))
        {
            if (calendar.IsTradingDay(d))
            {
                days.Add(d);
            }
        }

        if (days.Count == 0)
        {
            return Result.Success(new BasketContextComputed(0, 0, null));
        }

        days.Reverse();
        var have = await store.DatesAsync(basketId, days[0], lastClosed, cancellationToken);
        var written = 0;
        var pending = 0;
        var series = new Dictionary<Guid, IReadOnlyList<DailyClose>>();
        foreach (var day in days.Where(d => !have.Contains(d)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var members = await baskets.MembersAsync(basketId, BasketMember.CutoffAfter(day), day, cancellationToken);
            if (members is null)
            {
                return Result.Success(new BasketContextComputed(written, pending, BasketMissing));
            }

            var inputs = new List<MemberSeries>(members.Items.Count);
            foreach (var m in members.Items)
            {
                if (!series.TryGetValue(m.Instrument.Id, out var closes))
                {
                    var read = await bars.ReadDailyBarsAsync(m.Instrument.Id, lastClosed.AddDays(-LookbackDays), lastClosed.AddDays(1), cancellationToken);
                    closes = [.. read.Where(b => b.Complete).Select(b => new DailyClose(IndiaTime.Today(b.Ts), b.Close))];
                    series[m.Instrument.Id] = closes;
                }

                inputs.Add(new MemberSeries(m.Instrument.Symbol, closes, [], false, false));
            }

            // The last closed session waits until every member has its bar (the same rule as the tracked breadth): a row is written once, so a late bar must not be missed.
            if (day >= lastClosed && inputs.Any(i => i.Closes.All(c => c.Date != day)))
            {
                pending++;
                continue;
            }

            var basis = members.MembersKnownSince is { } since && day >= since ? BasketContextRow.PointInTime : BasketContextRow.SingleSnapshot;
            var row = BasketContextCalculator.Compute(basketId, day, inputs, basis, new BasketProfileOptions());
            if (await store.InsertOnceAsync(row, cancellationToken))
            {
                written++;
                BasketSettingsLog.ContextWritten(logger, basketId, day, row.State);
            }
        }

        return Result.Success(new BasketContextComputed(written, pending, null));
    }
}
