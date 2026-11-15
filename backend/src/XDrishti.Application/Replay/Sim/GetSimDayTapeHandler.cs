using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay.Sim;

/// <summary>
/// One page of a settled day's tape (SR-04 part B, ADR 0293; AC 5 and 6). Built only from the stored day (snapshot, or the settled cursor day) and the stored 1-minute bars of the day, read through the replay's hold-out clamp (a run inside block 1 reads there only with its look stored, as settling does).
/// No bar after <c>to</c> is read and nothing is computed beyond the settled day. Read only.
/// </summary>
internal sealed class GetSimDayTapeHandler(ISimDaysStore store, IReplayFacts facts) : IQueryHandler<GetSimDayTapeQuery, Result<SimDayTapeView>>
{
    public async ValueTask<Result<SimDayTapeView>> HandleAsync(GetSimDayTapeQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await store.GetAsync(query.Id, cancellationToken) is not { } row)
        {
            return Result.Failure<SimDayTapeView>(SimDays.NotFound);
        }

        var day = query.Session;
        var (open, close) = (SimTape.MarketOpen(day), SimTape.MarketClose(day));
        var from = SimTape.Minute(query.From ?? open);
        var to = SimTape.Minute(query.To ?? close);
        if (IndiaTime.Today(from) != day || IndiaTime.Today(to) != day || to < from)
        {
            return Result.Failure<SimDayTapeView>(SimDays.Invalid("from and to are minutes of the day, from not after to"));
        }

        var snapshots = await store.SnapshotsAsync(row.Id, cancellationToken);
        string recordJson, tradesJson;
        if (snapshots.FirstOrDefault(s => s.Session == day) is { } stored)
        {
            (recordJson, tradesJson) = (stored.RecordJson, stored.TradesJson);
        }
        else if (row.Pending is { } pending && pending.Session == day)
        {
            (recordJson, tradesJson) = (pending.RecordJson, pending.TradesJson);
        }
        else if (day == row.Cursor && SimDays.IsActive(row.Phase))
        {
            return Result.Failure<SimDayTapeView>(Error.Conflict(SimDays.NotSettledCode, "The day is not settled yet: settle it first."));
        }
        else
        {
            return Result.Failure<SimDayTapeView>(Error.NotFound("sim.day_not_found", "That day is not settled (yet)."));
        }

        // the trades held into the day: their latest stored state before it, still open at its open
        var known = new Dictionary<long, SimDayTrade>();
        var equityOpen = row.Capital;
        foreach (var s in snapshots.Where(s => s.Session < day).OrderBy(s => s.Session))
        {
            foreach (var t in ReplayJson.Read<List<SimDayTrade>>(s.TradesJson) ?? [])
            {
                known[t.TradeNo] = t;
            }

            equityOpen = s.EquityClose;
        }

        foreach (var t in ReplayJson.Read<List<SimDayTrade>>(tradesJson) ?? [])
        {
            known[t.TradeNo] = t;
        }

        var trades = known.Values.Where(t => t.EntrySession <= day && (t.ExitSession is null || t.ExitSession >= day)).OrderBy(t => t.TradeNo).ToList();
        var record = ReplayJson.Read<ReplaySessionRecord>(recordJson);
        var picked = record?.Fills.Where(f => f.Session == day).Select(f => f.Symbol) ?? [];
        var symbols = trades.Select(t => t.Symbol).Concat(picked).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        if (row.ReadThrough is { } through && !facts.ReadInsideHoldOut(through))
        {
            return Result.Failure<SimDayTapeView>(Error.Conflict("sim.holdout", "The run reads inside the hold-out block, but the bars there cannot be read."));
        }

        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var t in trades.Where(t => t.InstrumentId is not null))
        {
            ids[t.Symbol] = t.InstrumentId!.Value;
        }

        if (symbols.Any(s => !ids.ContainsKey(s)) && ReplayJson.Read<ReplayParams>(row.ParamsJson) is { } parameters)
        {
            foreach (var m in (await facts.UniverseAsync(parameters, day, cancellationToken)).Members)
            {
                ids.TryAdd(m.Symbol, m.Id);
            }
        }

        var bars = new List<SimTapeBar>();
        try
        {
            foreach (var symbol in symbols)
            {
                if (ids.TryGetValue(symbol, out var id))
                {
                    var end = to.AddSeconds(59); // the bar that starts at the minute "to" is in; none after it
                    bars.AddRange((await facts.DayMinuteBarsAsync(id, day, end, cancellationToken)).Where(b => b.EpochSeconds <= to.ToUnixTimeSeconds())
                        .Select(b => new SimTapeBar(symbol, b.Start.ToOffset(IndiaTime.Offset), b.Open, b.High, b.Low, b.Close, b.Volume)));
                }
            }
        }
        catch (ReplayHoldOutException e)
        {
            return Result.Failure<SimDayTapeView>(Error.Conflict("sim.holdout", e.Message));
        }

        var board = SimTape.BoardAt(to, equityOpen, trades, bars);
        var events = SimTape.Events(day, trades).Where(e => e.At >= from && e.At <= to).ToList();
        var page = bars.Where(b => b.At >= from).OrderBy(b => b.At).ThenBy(b => b.Symbol, StringComparer.Ordinal).ToList();
        return Result.Success(new SimDayTapeView(row.Id, day, from, to, open, close, symbols, page, events, board, SimDaysSupport.CostsLabel));
    }
}
