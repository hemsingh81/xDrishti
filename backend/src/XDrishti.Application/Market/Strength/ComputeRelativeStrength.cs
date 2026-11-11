using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Market.Strength;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Market.Strength;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Market.Strength;

/// <summary>
/// Computes the relative-strength rows (BK-09) of the missing sessions: without dates the newest sessions up to the last closed one (at most the catch-up setting), with a range those sessions of it. Insert-once: a day that has rows is left as it is and
/// reported, never rewritten. Reads the bars of every member once, whatever the number of days.
/// </summary>
public sealed record ComputeRelativeStrengthCommand(DateOnly? From = null, DateOnly? To = null) : ICommand;

/// <summary>What a run did: days written, days that already had rows (left alone), rows written, and why nothing ran (null when it did).</summary>
public sealed record RelativeStrengthComputed(IReadOnlyList<DateOnly> Written, IReadOnlyList<DateOnly> Existing, int Rows, string? Missing);

internal sealed class ComputeRelativeStrengthHandler(
    RelativeStrengthSettings settings,
    IContextSettings contextSettings,
    IContextBarSource contextBars,
    IBasketQueries baskets,
    IBarQueries bars,
    ICorporateActionQueries actions,
    IRelativeStrengthStore store,
    ITradingCalendar tradingCalendar,
    TimeProvider clock,
    Microsoft.Extensions.Logging.ILogger<ComputeRelativeStrengthHandler> logger) : ICommandHandler<ComputeRelativeStrengthCommand, RelativeStrengthComputed>
{
    public const string Disabled = "disabled";

    public const string NoBaskets = "no_pre_registered_basket_found";

    public const string Dated = "dated";

    public const string SingleSnapshot = "single_snapshot";

    private const int LookbackDays = 420;

    private const int HistoryTake = 20_000;

    public async ValueTask<Result<RelativeStrengthComputed>> HandleAsync(ComputeRelativeStrengthCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!settings.Enabled)
        {
            return Result.Success(new RelativeStrengthComputed([], [], 0, Disabled));
        }

        var started = clock.GetTimestamp();
        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var lastClosed = calendar.LastClosedSession(clock.GetUtcNow());
        var to = command.To is { } t && t < lastClosed ? t : lastClosed;
        var sessions = new List<DateOnly>();
        if (command.From is { } from)
        {
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                if (calendar.IsTradingDay(d))
                {
                    sessions.Add(d);
                }
            }
        }
        else
        {
            for (var d = to; sessions.Count < settings.CatchUpSessions && d > to.AddDays(-(settings.CatchUpSessions * 4) - 10); d = d.AddDays(-1))
            {
                if (calendar.IsTradingDay(d))
                {
                    sessions.Insert(0, d);
                }
            }
        }

        if (sessions.Count == 0)
        {
            return Result.Success(new RelativeStrengthComputed([], [], 0, null));
        }

        var have = await store.DatesWithRowsAsync(sessions[0], sessions[^1], cancellationToken);
        var existing = sessions.Where(have.Contains).ToList();
        var todo = sessions.Where(s => !have.Contains(s)).ToList();
        if (todo.Count == 0)
        {
            return Result.Success(new RelativeStrengthComputed([], existing, 0, null));
        }

        var registered = await ResolveBasketsAsync(cancellationToken);
        if (registered.Count == 0)
        {
            StrengthLog.PrerequisiteMissing(logger, NoBaskets);
            return Result.Success(new RelativeStrengthComputed([], existing, 0, NoBaskets));
        }

        var options = settings.ToOptions();
        var membership = new Dictionary<(Guid Basket, DateOnly Day), IReadOnlyList<(Guid Id, string Symbol, string? Sector)>>();
        var members = new Dictionary<Guid, (string Symbol, string? Sector)>();
        foreach (var (_, id, _, _) in registered)
        {
            foreach (var day in todo)
            {
                var list = await baskets.MembersAsync(id, BasketMember.CutoffAfter(day), day, cancellationToken);
                var items = list?.Items.Select(i => (i.Instrument.Id, i.Instrument.Symbol, i.Instrument.Sector)).ToList() ?? [];
                membership[(id, day)] = items;
                foreach (var i in items)
                {
                    members[i.Id] = (i.Symbol, i.Sector);
                }
            }
        }

        var series = new Dictionary<Guid, MemberSeries>();
        foreach (var (id, (symbol, _)) in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            series[id] = new MemberSeries(symbol, await ClosesAsync(id, todo[0], todo[^1], cancellationToken), [], false, false);
        }

        var index = (await contextBars.ReadAsync(contextSettings.IndexSymbols, [], cancellationToken)).Index;
        IReadOnlyList<DailyClose> indexCloses = index is null ? [] : await ClosesAsync(index.Id, todo[0], todo[^1], cancellationToken);
        var exDates = new Dictionary<Guid, IReadOnlySet<DateOnly>>();
        foreach (var group in (await actions.ListKeysAsync([.. members.Keys], cancellationToken)).GroupBy(k => k.InstrumentId))
        {
            exDates[group.Key] = group.Select(k => k.ExDate).ToHashSet();
        }

        var written = new List<DateOnly>();
        var rowCount = 0;
        foreach (var day in todo)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = new List<StrengthRow>();
            var seen = new HashSet<Guid>();
            foreach (var (_, id, _, basis) in registered)
            {
                var today = membership[(id, day)];
                foreach (var lookback in StrengthOptions.Lookbacks)
                {
                    rows.AddRange(RankRows(day, id, basis, lookback, today, series, exDates, options));
                }

                foreach (var m in today)
                {
                    seen.Add(m.Id);
                }
            }

            foreach (var id in seen.Order())
            {
                var metric = IndexRelative.Compute(series[id].Closes, indexCloses, day, StrengthOptions.IndexWindow);
                rows.Add(new StrengthRow(day, id, Guid.Empty, StrengthOptions.IndexWindow, 0, metric.Number, null, 0, State(metric), "none"));
            }

            var sectors = seen.Where(i => !string.IsNullOrWhiteSpace(members[i].Sector))
                .GroupBy(i => members[i].Sector!.Trim(), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<MemberSeries>)[.. g.Select(i => series[i])], StringComparer.Ordinal);
            var sectorRows = SectorStrength.Compute(sectors, day, StrengthOptions.SectorWindows, options)
                .Select(s => new SectorRow(day, s.Sector, s.Window, s.Return.Number, s.Rank, s.Members, State(s.Return))).ToList();

            if (await store.InsertDayAsync(day, rows, sectorRows, cancellationToken))
            {
                written.Add(day);
                rowCount += rows.Count + sectorRows.Count;
            }
            else
            {
                existing.Add(day);
            }
        }

        var ms = (long)clock.GetElapsedTime(started).TotalMilliseconds;
        StrengthLog.Computed(logger, written.Count, existing.Count, rowCount, ms);
        return Result.Success(new RelativeStrengthComputed(written, existing, rowCount, null));
    }

    private static List<StrengthRow> RankRows(
        DateOnly day, Guid basketId, string basis, int lookback, IReadOnlyList<(Guid Id, string Symbol, string? Sector)> today,
        Dictionary<Guid, MemberSeries> series, Dictionary<Guid, IReadOnlySet<DateOnly>> exDates, StrengthOptions options)
    {
        var inputs = new List<MomentumInput>(today.Count);
        var rows = new List<StrengthRow>(today.Count);
        var bySymbol = today.ToDictionary(m => m.Symbol, m => m.Id, StringComparer.Ordinal);
        foreach (var m in today)
        {
            var (value, why) = RelativeStrengthRank.Momentum(series[m.Id], day, lookback, StrengthOptions.Skip, exDates.GetValueOrDefault(m.Id));
            if (value is not null)
            {
                inputs.Add(value);
            }
            else
            {
                rows.Add(new StrengthRow(day, m.Id, basketId, lookback, StrengthOptions.Skip, null, null, 0, State(why!), basis));
            }
        }

        var ranked = RelativeStrengthRank.Rank(inputs, options);
        foreach (var r in ranked.Rows)
        {
            rows.Add(new StrengthRow(day, bySymbol[r.Symbol], basketId, lookback, StrengthOptions.Skip, r.Score.Number, r.PctRank.Number, ranked.Ranked, State(r.PctRank), basis));
        }

        return rows;
    }

    private static string State(Metric metric) => metric.State switch
    {
        MetricState.Value or MetricState.Provisional => "value",
        MetricState.Insufficient => "insufficient",
        MetricState.NotEvaluable when metric.Reason == RelativeStrengthRank.CorporateActionUnresolved => RelativeStrengthRank.CorporateActionUnresolved,
        MetricState.NotEvaluable => "not_evaluable",
        _ when metric.Reason?.StartsWith("short_history", StringComparison.Ordinal) == true => "short_history",
        _ => "unavailable",
    };

    private async ValueTask<List<(string Name, Guid Id, int Code, string Basis)>> ResolveBasketsAsync(CancellationToken cancellationToken)
    {
        var all = await baskets.ListAsync(false, cancellationToken);
        var found = new List<(string, Guid, int, string)>();
        for (var code = 1; code <= StrengthBaskets.Names.Count; code++)
        {
            var name = StrengthBaskets.Names[code - 1];
            var basket = StrengthBasketNames.Find(all, name);
            if (basket is null)
            {
                continue;
            }

            var history = await baskets.HistoryAsync(basket.Id, HistoryTake, cancellationToken);
            // as in the profile (BK-07): dated when there are changes on at least two days, else the members are those of one snapshot
            var days = history?.Items.Select(e => IndiaTime.Today(e.At)).Distinct().Count() ?? 0;
            found.Add((name, basket.Id, code, days >= 2 ? Dated : SingleSnapshot));
        }

        return found;
    }

    private async ValueTask<IReadOnlyList<DailyClose>> ClosesAsync(Guid instrumentId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await bars.ReadDailyBarsAsync(instrumentId, from.AddDays(-LookbackDays), to.AddDays(1), cancellationToken);
        return [.. rows.Where(r => r.Complete).Select(r => new DailyClose(IndiaTime.Today(r.Ts), r.Close))];
    }
}
