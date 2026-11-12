using System.Globalization;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Quality;
using XDrishti.Domain.Research;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Replay.Reads;

/// <summary>One mark on the replay-trade chart: the entry, the stop level or the exit.</summary>
public sealed record ReplayChartMarkerView(string Kind, DateTimeOffset? At, decimal Price);

/// <summary>
/// One replay trade on its bars (SR-02, D4): the P8-11 <c>chart</c> block (bars, coherence), the markers and the trade's own figures. The bars are the 1-minute bars as the replay read them (through the hold-out clamp), not adjusted for later corporate actions.
/// <see cref="ExitPriceDerived"/> is true because the exit price is not stored: it is the entry plus the gross result over the quantity (exact for gross figures).
/// </summary>
public sealed record ReplayTradeChartView(
    IReadOnlyList<ReplayLabelView> Hindsight,
    Guid RunId,
    long TradeNo,
    string Symbol,
    string Side,
    int Qty,
    DateOnly EntrySession,
    DateTimeOffset EntryAt,
    decimal EntryPrice,
    decimal StopPrice,
    DateOnly? ExitSession,
    DateTimeOffset? ExitAt,
    decimal? ExitPrice,
    bool ExitPriceDerived,
    decimal? GrossPnl,
    decimal? NetPnl,
    string CostsLabel,
    ReplayChartView Chart,
    IReadOnlyList<ReplayChartMarkerView> Markers,
    IReadOnlyList<string> Limits);

/// <summary>The chart of one trade of a replay run (SR-02, part C).</summary>
public sealed record GetReplayTradeChartQuery(Guid RunId, long TradeNo, string? Timeframe, int? Before, int? After) : IQuery<Result<ReplayTradeChartView>>;

/// <summary>
/// Reads the window of <see cref="TradeReplayWindow"/> around a replay trade through the hold-out clamp of the replay's facts: the instrument is the symbol's member of the universe as of the entry session, a window past the clamp is cut at it and says so, and a trade with no bars answers <c>no_bars</c>.
/// Read only: nothing is written and no order is referenced.
/// </summary>
internal sealed class GetReplayTradeChartHandler(IReplayReads reads, IReplayFacts facts, IInstrumentQueries instruments) : IQueryHandler<GetReplayTradeChartQuery, Result<ReplayTradeChartView>>
{
    private static readonly string[] BaseLimits =
    [
        "A replayed trade on replayed history with modelled fills: the chart shows the bars the replay read, not a fill you got.",
        "The prices are as stored, not adjusted for corporate actions applied later.",
        "The exit price is derived from the gross result (the entry plus the result over the quantity); it is not stored.",
    ];

    public async ValueTask<Result<ReplayTradeChartView>> HandleAsync(GetReplayTradeChartQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await reads.GetRunAsync(query.RunId, cancellationToken) is not { } run || ReplayReadSupport.ParamsOf(run) is not { } p)
        {
            return Result.Failure<ReplayTradeChartView>(ReplayErrors.NotFound);
        }

        var trade = (await reads.TradesAsync(run.Id, cancellationToken)).FirstOrDefault(t => t.TradeNo == query.TradeNo);
        if (trade is null)
        {
            return Result.Failure<ReplayTradeChartView>(Error.NotFound("replay.not_found", "No such replay trade."));
        }

        var timeframe = Timeframe.M5;
        if (query.Timeframe is not null && (!Timeframes.TryParse(query.Timeframe, out timeframe) || !TradeReplayWindow.IsAllowed(timeframe)))
        {
            return Invalid("timeframe is one of 1m, 5m, 15m, 1h, 1D.");
        }

        var max = TradeReplayWindow.MaxSessions(timeframe);
        var before = query.Before ?? TradeReplayWindow.DefaultBefore(timeframe);
        var after = query.After ?? TradeReplayWindow.DefaultAfter(timeframe);
        if (before < 0 || before > max || after < 0 || after > max)
        {
            return Invalid(string.Create(CultureInfo.InvariantCulture, $"before and after are 0 to {max} sessions for {timeframe.ToCode()}."));
        }

        var calendar = await facts.CalendarAsync(cancellationToken);
        DateOnly last;
        try
        {
            last = await facts.LastReadableSessionAsync(cancellationToken);
        }
        catch (ReplayHoldOutException)
        {
            return HoldOut();
        }

        var limits = new List<string>(BaseLimits);
        var exitDay = trade.ExitSession ?? run.CursorSession ?? trade.EntrySession;
        var window = TradeReplayWindow.Compute(calendar, trade.EntrySession, exitDay, before, after);
        var toDate = window.ToDate;
        var toEpoch = window.ToEpoch;
        if (toDate > last)
        {
            toDate = last;
            toEpoch = MinuteAggregator.MidnightOf(last) + ((calendar.SessionOf(last)?.ToMinute ?? SessionClock.CloseMinute) * 60L) - 60;
            limits.Add("The window was cut at the hold-out clamp: the last readable session is " + last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".");
        }

        try
        {
            return await ReadAsync(query, run, p, trade, timeframe, calendar, last, window, toDate, toEpoch, limits, cancellationToken);
        }
        catch (ReplayHoldOutException)
        {
            return HoldOut(); // the clamp refused a read: the same answer as the start of a run past it
        }
    }

    private async ValueTask<Result<ReplayTradeChartView>> ReadAsync(GetReplayTradeChartQuery query, ReplayRunRow run, ReplayParams p, ReplayTradeRow trade, Timeframe timeframe, XDrishti.Domain.Market.Calendar.TradingCalendar calendar, DateOnly last, ReplayWindow window, DateOnly toDate, long toEpoch, List<string> limits, CancellationToken cancellationToken)
    {
        var symbol = (await facts.UniverseAsync(p, trade.EntrySession, cancellationToken)).Members.FirstOrDefault(m => string.Equals(m.Symbol, trade.Symbol, StringComparison.OrdinalIgnoreCase));
        List<Bar> minutes = [];
        double tick = (double)TickSpec.FallbackSize;
        if (symbol is not null)
        {
            // shortcut: the facts return the 400-day lookback up to the cut and the window is cut out of it; a window never reaches back that far (at most 60 sessions before the entry), so nothing is lost. Read the range only if that changes.
            minutes = [.. (await facts.MinuteBarsAsync(symbol.Id, toDate, cancellationToken)).Where(b => b.EpochSeconds >= window.FromEpoch && b.EpochSeconds <= toEpoch)];
            var spec = TickSpec.FromMasterPaise((await instruments.GetAsync(symbol.Id, cancellationToken))?.TickSize);
            tick = (double)(spec.IsSuccess ? spec.Value.Size : TickSpec.FallbackSize);
        }
        else
        {
            limits.Add("The symbol has no instrument in the universe at the entry session, so there are no bars.");
        }

        var bars = ReplayBars.Aggregate(minutes, timeframe);
        if (bars.Count > TradeReplayWindow.MaxBars)
        {
            return Result.Failure<ReplayTradeChartView>(Error.Validation("replay.window_too_large", string.Create(CultureInfo.InvariantCulture, $"the window has {bars.Count} bars; at most {TradeReplayWindow.MaxBars} are shown. Ask for a larger timeframe or fewer sessions.")));
        }

        var entryEpoch = trade.EntryAt.ToUnixTimeSeconds();
        var coherence = ReplayCoherence.Check(minutes, entryEpoch, (double)trade.EntryPrice, tick, false);
        decimal? exit = trade.GrossPnl is { } gross ? ReplayExitPrice.Derive(trade.Side, trade.Qty, trade.EntryPrice, gross) : null;
        List<ReplayChartMarkerView> markers = [new("entry", trade.EntryAt, trade.EntryPrice), new("stop", null, trade.StopPrice)];
        if (exit is { } exitPrice)
        {
            markers.Add(new("exit", trade.ExitAt, exitPrice));
        }

        var chart = new ReplayChartView(
            timeframe.ToCode(), false, null, DateTimeOffset.FromUnixTimeSeconds(window.FromEpoch), DateTimeOffset.FromUnixTimeSeconds(toEpoch), bars.Count, bars,
            new ReplayCoherenceView(ReplayNames.Name(coherence.State), coherence.EntryStored, coherence.EntryOnBars, coherence.Detail));
        return Result.Success(new ReplayTradeChartView(
            ReplayReadSupport.Labels(p, await reads.LabelsAsync(run.Id, cancellationToken)), run.Id, trade.TradeNo, trade.Symbol, trade.Side, trade.Qty, trade.EntrySession, trade.EntryAt, trade.EntryPrice, trade.StopPrice,
            trade.ExitSession, trade.ExitAt, exit, exit is not null, trade.GrossPnl, trade.NetPnl, ReplayReadSupport.CostsLabel(p), chart, markers, limits));
    }

    private static Result<ReplayTradeChartView> HoldOut() => Result.Failure<ReplayTradeChartView>(Error.Conflict(ReplayDriver.HoldOutCode, "The window reaches the hold-out; the replay may not read it."));

    private static Result<ReplayTradeChartView> Invalid(string message) => Result.Failure<ReplayTradeChartView>(Error.Validation(ReplayDriver.InvalidCode, message));
}

/// <summary>The exit price of a replay trade, which is not stored (SR-02, D4): the entry plus the gross result over the quantity and the side. Pure.</summary>
internal static class ReplayExitPrice
{
    public static decimal Derive(string side, int quantity, decimal entry, decimal gross)
    {
        var sign = string.Equals(side, "SELL", StringComparison.OrdinalIgnoreCase) ? -1m : 1m;
        return Math.Round(entry + (gross / (quantity * sign)), 4);
    }
}
