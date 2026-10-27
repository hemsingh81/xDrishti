using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>
/// The second read for the trade cards (spec P3-04c, C2). For each instrument that has a chosen trade, in the order of the run's universe and one at a time, it reads the stored bars with the bounds of the run, checks that nothing the run
/// recorded about the instrument has changed (the instrument row, the hash of the bars, the applied actions that change them, the tick), cuts the chosen trade days out of the list and lets the list go before it reads the next instrument. The
/// basket is never resolved again and nothing is merged. The day bars are adjusted by the rule of the replay (<see cref="AdjustmentFactors.FactorAt"/>, <see cref="AdjustmentFactors.Price"/>, <see cref="AdjustmentFactors.Volume"/>),
/// re-stated here because the replay's own type is internal to the simulation library (ADR 0039); an equivalence test pins the two together. The trade is not recomputed here (P3-04d does that).
/// </summary>
internal sealed class BuildTradeCardsHandler(IBarStore bars, IBarQueries barQueries, IAdjustmentFactors adjustments, ISimulationEngine engine) : IQueryHandler<BuildTradeCardsQuery, Result<IReadOnlyList<TradeCardData>>>
{
    private readonly InstrumentInputReader _reader = new(bars, barQueries, adjustments);

    private const int OpenMinute = SessionClock.OpenMinute;

    public async ValueTask<Result<IReadOnlyList<TradeCardData>>> HandleAsync(BuildTradeCardsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Run);
        ArgumentNullException.ThrowIfNull(query.TradeKeys);

        var run = query.Run;
        var chosen = Choose(run, query.TradeKeys, out var problem);
        if (problem is not null)
        {
            return Result.Failure<IReadOnlyList<TradeCardData>>(problem);
        }

        var cards = new Dictionary<string, TradeCardData>(StringComparer.Ordinal);
        foreach (var member in run.Universe.Members)
        {
            if (!chosen.TryGetValue(member.Id, out var trades))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var record = run.Instruments.FirstOrDefault(i => i.InstrumentId == member.Id);
            if (record is null)
            {
                return Result.Failure<IReadOnlyList<TradeCardData>>(SimulationErrors.Invalid([new FieldError("run.instruments", "A member of the universe has no instrument record in the run: " + member.Symbol + ".", null, "missing_instrument_record")]));
            }

            var read = await ReadInstrumentAsync(run, record, trades, query.Basis, cancellationToken);
            if (read.IsFailure)
            {
                return Result.Failure<IReadOnlyList<TradeCardData>>(read.Error);
            }

            foreach (var card in read.Value)
            {
                cards[card.Trade.Trade.Key] = card;
            }
        }

        IReadOnlyList<TradeCardData> ordered = [.. query.TradeKeys.Distinct(StringComparer.Ordinal).Select(k => cards[k])];
        return Result.Success(ordered);
    }

    /// <summary>The chosen trades by instrument; <paramref name="problem"/> is set when a key is not a trade of the run or its instrument is not in the universe.</summary>
    private static Dictionary<Guid, List<RunTrade>> Choose(SimulationRun run, IReadOnlyList<string> keys, out Error? problem)
    {
        problem = null;
        var chosen = new Dictionary<Guid, List<RunTrade>>();
        if (keys.Count == 0)
        {
            return chosen;
        }

        var byKey = new Dictionary<string, RunTrade>(StringComparer.Ordinal);
        foreach (var trade in run.Trades)
        {
            byKey.TryAdd(trade.Trade.Key, trade);
        }

        var members = run.Universe.Members.Select(m => m.Id).ToHashSet();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < keys.Count; i++)
        {
            if (!byKey.TryGetValue(keys[i], out var trade) || !members.Contains(trade.Trade.InstrumentId))
            {
                problem = SimulationErrors.Invalid([new FieldError("trade_keys[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]", "The key is not a trade of this run.", null, "unknown_trade")]);
                return chosen;
            }

            if (!seen.Add(keys[i]))
            {
                continue;
            }

            if (!chosen.TryGetValue(trade.Trade.InstrumentId, out var list))
            {
                chosen[trade.Trade.InstrumentId] = list = [];
            }

            list.Add(trade);
        }

        return chosen;
    }

    /// <summary>Reads one instrument, guards it and cuts the days. The list of bars is local to this call, so it can be collected as soon as the call returns.</summary>
    private async ValueTask<Result<List<TradeCardData>>> ReadInstrumentAsync(SimulationRun run, InstrumentRun record, List<RunTrade> trades, ExplainBasis? basis, CancellationToken cancellationToken)
    {
        var instrument = await _reader.FindAsync(record.InstrumentId, null, cancellationToken);
        if (instrument is null)
        {
            return Stale(record, SimulationErrors.StaleInstrumentMissing, "the instrument is no longer in the master");
        }

        var (raw, factors) = await _reader.ReadAsync(record.InstrumentId, run.To, cancellationToken);

        if (!string.Equals(BarStreamHash.Compute(raw), record.BarsSha256, StringComparison.Ordinal))
        {
            return Stale(record, SimulationErrors.StaleBars, "the stored bars differ from the ones the run read (a backfill, a merge or a deletion since)");
        }

        var applied = raw.Count == 0 ? [] : factors.ActionsAfter(SessionClock.DateOf(raw[0].EpochSeconds));
        if (!SameAdjustments(applied, record.Adjustments))
        {
            return Stale(record, SimulationErrors.StaleAdjustments, "the corporate actions that adjust the bars differ from the ones the run applied");
        }

        var tick = TickSpec.FromMasterPaise(instrument.TickSize);
        if (tick.IsFailure ? record.TickSize is not null : record.TickSize != tick.Value.Size || record.TickFromFallback != tick.Value.FromFallback)
        {
            return Stale(record, SimulationErrors.StaleTick, "the tick of the instrument master differs from the one the run used");
        }

        IReadOnlyDictionary<string, TradeExplanation>? explained = null;
        if (basis is not null)
        {
            // P3-04d: the same input the run had, gross, replayed once with the chosen trades asked for; the answer must be the recorded trades.
            var input = InstrumentInputReader.Build(record.InstrumentId, instrument, raw, factors, run.AcknowledgeCsvAdjusted, basis.Calendar, basis.CsvThrough, run.Options, run.From, run.To, null, basis.ContextDays, basis.ContextDays is { Count: > 0 } ? null : "no_context");
            var requests = trades.Select(t => new ExplainRequest(t.Trade.Key, t.Trade.DecisionEpochSeconds, t.Trade.Side)).ToList();
            var answer = engine.Explain(basis.Strategy, input, requests);
            var mismatch = Verify(record.Symbol, trades, answer);
            if (mismatch is not null)
            {
                return Result.Failure<List<TradeCardData>>(mismatch);
            }

            explained = answer.ByTradeKey;
        }

        var adjusts = applied.Count > 0;
        var days = new Dictionary<DateOnly, Day>();
        var cards = new List<TradeCardData>(trades.Count);
        foreach (var trade in trades)
        {
            var date = trade.Trade.TradeDate;
            if (!days.TryGetValue(date, out var day))
            {
                days[date] = day = CutDay(raw, date, adjusts ? factors.FactorAt(date) : 1.0);
            }

            cards.Add(new TradeCardData(trade, record, day.Bars, day.Opening, day.OpeningRaw, explained?[trade.Trade.Key]));
        }

        return Result.Success(cards);
    }

    /// <summary>The guard of P3-04d (D3, D4): each chosen trade must be among the recomputed ones with every field equal but <c>Costs</c> and <c>Costing</c>, and its decision must hold: setup true, trigger true, invalidate not true.</summary>
    private static Error? Verify(string symbol, List<RunTrade> trades, ExplainResult answer)
    {
        var recomputed = new Dictionary<string, SimTrade>(StringComparer.Ordinal);
        foreach (var trade in answer.Replay.Trades.Concat(answer.Replay.OpenAtEnd))
        {
            recomputed.TryAdd(trade.Key, trade);
        }

        foreach (var chosen in trades)
        {
            var key = chosen.Trade.Key;
            if (!recomputed.TryGetValue(key, out var again))
            {
                return SimulationErrors.CardsMismatch(symbol, "trade", "the replay does not make the trade " + key);
            }

            if (SimTradeDifference.First(chosen.Trade, again) is { } field)
            {
                return SimulationErrors.CardsMismatch(symbol, field, "the replay gives another " + field + " for the trade " + key);
            }

            if (!answer.ByTradeKey.TryGetValue(key, out var explanation))
            {
                return SimulationErrors.CardsMismatch(symbol, "decision", "the replay has no entry decision at the recorded instant for the trade " + key);
            }

            foreach (var (name, want) in new[] { ("setup", TruthState.True), ("trigger", TruthState.True), ("invalidate", TruthState.False) })
            {
                var section = explanation.Sections.FirstOrDefault(s => s.SectionName == name);
                if (section is null)
                {
                    continue;
                }

                var truth = Fold(section);
                var holds = name == "invalidate" ? truth != TruthState.True : truth == want;
                if (!holds)
                {
                    return SimulationErrors.CardsMismatch(symbol, name, "the " + name + " list is " + truth.ToString().ToLowerInvariant() + " at the recorded decision of the trade " + key);
                }
            }
        }

        return null;
    }

    /// <summary>The value of a list (Kleene): all of its items, or any one of them. This mirrors <c>ListFold.Fold</c> used by <c>BoundSide.Evaluate</c> (the engine's own fold, which Application cannot reach, ADR 0039); the guard would report a disagreement as a mismatch, and a test pins the rule.</summary>
    private static TruthState Fold(ConditionSectionTrace section)
    {
        var any = section.Mode == "any";
        var unknown = false;
        foreach (var item in section.Items)
        {
            if (item.Truth == (any ? TruthState.True : TruthState.False))
            {
                return item.Truth;
            }

            unknown |= item.Truth == TruthState.Unknown;
        }

        return unknown ? TruthState.Unknown : any ? TruthState.False : TruthState.True;
    }

    private static Result<List<TradeCardData>> Stale(InstrumentRun record, string difference, string detail) =>
        Result.Failure<List<TradeCardData>>(SimulationErrors.CardsStale(record.Symbol, difference, detail));

    /// <summary>The actions in ex-date, kind, numerator and denominator: the fields of the <c>i=</c> line of the run key.</summary>
    private static bool SameAdjustments(IReadOnlyList<AppliedAction> now, IReadOnlyList<AppliedAdjustment> recorded)
    {
        if (now.Count != recorded.Count)
        {
            return false;
        }

        for (var i = 0; i < now.Count; i++)
        {
            if (now[i].ExDate != recorded[i].ExDate || now[i].Kind != recorded[i].Kind || now[i].Numerator != recorded[i].Numerator || now[i].Denominator != recorded[i].Denominator)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The bars of the India date from 09:15 to 15:29, each adjusted by the factor of the day (as the replay does) unless the factor is 1.</summary>
    private static Day CutDay(IReadOnlyList<Bar> raw, DateOnly date, double factor)
    {
        var open = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, IndiaTime.Offset).ToUnixTimeSeconds() + (OpenMinute * 60L);
        var close = open + (SessionClock.RegularBars * 60L);
        var index = LowerBound(raw, open);
        var day = new List<Bar>();
        Bar? opening = null;
        Bar? openingRaw = null;
        for (; index < raw.Count && raw[index].EpochSeconds < close; index++)
        {
            var bar = factor == 1.0 ? raw[index] : Adjusted(raw[index], factor);
            day.Add(bar);
            if (bar.EpochSeconds == open)
            {
                opening = bar;
                openingRaw = factor == 1.0 ? null : raw[index];
            }
        }

        return new Day(day, opening, openingRaw);
    }

    /// <summary>The position of the first bar at or after the epoch (the list is ascending: the replay checks it).</summary>
    private static int LowerBound(IReadOnlyList<Bar> raw, long epoch)
    {
        var low = 0;
        var high = raw.Count;
        while (low < high)
        {
            var mid = (low + high) >>> 1;
            if (raw[mid].EpochSeconds < epoch)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    private static Bar Adjusted(Bar bar, double factor) => new(
        bar.EpochSeconds,
        AdjustmentFactors.Price(bar.Open, factor),
        AdjustmentFactors.Price(bar.High, factor),
        AdjustmentFactors.Price(bar.Low, factor),
        AdjustmentFactors.Price(bar.Close, factor),
        AdjustmentFactors.Volume(bar.Volume, factor));

    private sealed record Day(List<Bar> Bars, Bar? Opening, Bar? OpeningRaw);
}
