using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay.Reads;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay.Sim;

/// <summary>The cursor stepping over the trading calendar and the words of every answer (SR-04).</summary>
internal static class SimDaysSupport
{
    /// <summary>The cost basis on every answer: gross for ever (ADR 0292).</summary>
    public const string CostsLabel = "gross: before costs (costs are gross for ever, ADR 0292)";

    /// <summary>The first trading session on or after <paramref name="day"/>, not after <paramref name="end"/>; null when none.</summary>
    public static DateOnly? FirstOnOrAfter(TradingCalendar calendar, DateOnly day, DateOnly end)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        for (var d = day; d <= end; d = d.AddDays(1))
        {
            if (calendar.IsTradingDay(d))
            {
                return d;
            }
        }

        return null;
    }

    /// <summary>The trading session after <paramref name="day"/> (holidays and weekends skipped), not after <paramref name="end"/>; null at the end.</summary>
    public static DateOnly? Next(TradingCalendar calendar, DateOnly day, DateOnly end) => FirstOnOrAfter(calendar, day.AddDays(1), end);

    /// <summary>The trading sessions from <paramref name="start"/> to <paramref name="end"/>.</summary>
    public static int Count(TradingCalendar calendar, DateOnly start, DateOnly end)
    {
        var n = 0;
        for (var d = FirstOnOrAfter(calendar, start, end); d is { } day; d = Next(calendar, day, end))
        {
            n++;
        }

        return n;
    }

    public static IReadOnlyList<ReplayLabelView> Labels(SimDaysRunRow row) => ReplayReadSupport.Labels(ReplayJson.Read<ReplayParams>(row.ParamsJson));

    public static SimDaysView View(SimDaysRunRow row, TradingCalendar calendar, SimDaysOptions options) => new(
        row.Id, row.Start, row.End, row.RequestedEnd, row.Cursor, row.Phase, row.Auto, row.Mode, row.Capital, row.LastEquity ?? row.Capital, row.Pending?.EquityClose, row.DaysDone, Count(calendar, row.Start, row.End),
        row.HoldOutNote, row.Waiting, row.FailReason, Labels(row), CostsLabel, options.DefaultSpeedSecondsPerMinute, row.CreatedAt, row.UpdatedAt);

    public static SimDaySnapshotView Snapshot(SimDaysRunRow row, DateOnly session, bool stored, string recordJson, string tradesJson, string hash)
    {
        var r = ReplayJson.Read<ReplaySessionRecord>(recordJson);
        var trades = ReplayJson.Read<List<SimDayTrade>>(tradesJson) ?? [];
        static ReplayCandidateView C(ReplayCandidateRow c) => new(c.Symbol, c.Side, c.CellKey, c.Rank);
        return new SimDaySnapshotView(
            row.Id, session, stored,
            r is null ? [] : [.. r.Taken.Select(C)],
            r is null ? [] : [.. r.Blocked.Select(b => new ReplayBlockedView(b.Symbol, b.CellKey, b.Reason.Code(), b.Detail))],
            r is null ? [] : [.. r.Fills.Select(f => new ReplayFillView(f.Symbol, f.Side, f.CellKey, f.Session, f.Status, f.Qty, f.EntryPrice, f.Detail))],
            trades, r?.Equity ?? 0, r?.DrawdownPct, r?.OpenPositions ?? 0, r?.Notes ?? [], hash, ReplayReadSupport.Labels(ReplayJson.Read<ReplayParams>(row.ParamsJson), r?.Labels), CostsLabel);
    }
}
