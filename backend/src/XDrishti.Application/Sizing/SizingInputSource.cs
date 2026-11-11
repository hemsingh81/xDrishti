using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Domain.Selection.SizeScaling;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Application.Sizing;

/// <summary>
/// The inputs of the down-only size scaling for a plan (spec BK-10): the owner's settings and budgets, and for each instrument the volatility and the 20-session median traded value from the stored day bars, as of the close of the session (a bar after it is dropped, so
/// there is no look-ahead). Read only. A figure that cannot be computed is null (unavailable), never a default.
/// </summary>
internal sealed class SizingInputSource(ISizingStore store, IBarQueries bars, ITradingCalendar calendars, IHoldingClassifications classifications, CostStressOptions costStress) : ISizingInputs
{
    /// <summary>Calendar days of day bars read: the 20 seed returns, the 252 sessions of the target and room for holidays and halts.</summary>
    internal const int LookBackDays = 520;

    // shortcut: one serial day-bar read per ticket (at most the plan's primary tickets, about 5); upgrade to one batched read if the plan ever lists many.
    public async ValueTask<SizingInputs> ReadAsync(DateOnly session, DateOnly forDate, IReadOnlyList<ContextInstrument> instruments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        var state = await store.CurrentAsync(cancellationToken);
        var budgets = await store.BudgetsAsync(cancellationToken);

        // The one capacity share of the app (ADR 0223) is the ceiling: the owner's value can only be lower.
        state = ClampCapacity(state, costStress);
        return await ComputeAsync(state, budgets, session, forDate, instruments, bars.ReadDailyBarsAsync, await calendars.GetAsync(cancellationToken), classifications, cancellationToken);
    }

    /// <summary>The settings with the capacity share held at the app's ceiling.</summary>
    internal static SizingState ClampCapacity(SizingState state, CostStressOptions costStress) =>
        state with { Settings = state.Settings with { CapacityPct = Math.Min(state.Settings.CapacityPct, (double)costStress.CapacityDailyShare) } };

    /// <summary>The inputs from given settings and budgets and a day-bar reader; the live read and the system replay (SR-01) both use it, so they cannot disagree. Nothing after <paramref name="session"/> is asked of the reader.</summary>
    internal static async ValueTask<SizingInputs> ComputeAsync(
        SizingState state,
        IReadOnlyDictionary<Guid, double> budgets,
        DateOnly session,
        DateOnly forDate,
        IReadOnlyList<ContextInstrument> instruments,
        Func<Guid, DateOnly, DateOnly, CancellationToken, ValueTask<IReadOnlyList<BarRow>>> readDaily,
        TradingCalendar calendar,
        IHoldingClassifications classifications,
        CancellationToken cancellationToken)
    {
        if (!state.Settings.VolTargetEnabled && !state.Settings.CapacityEnabled && budgets.Count == 0)
        {
            // Everything OFF and no budget: nothing would be used, so no bar is read and the plan is what it was.
            return new SizingInputs(state.Settings, state.Version, new Dictionary<string, SymbolSizingInputs>(StringComparer.Ordinal));
        }

        var ids = instruments.Where(i => i.Id is not null).Select(i => i.Id!.Value).Distinct().ToList();
        var baskets = budgets.Count == 0 || ids.Count == 0
            ? new Dictionary<Guid, IReadOnlyList<BasketRef>>()
            : (await classifications.BasketsAsync(ids, cancellationToken)).ToDictionary(p => p.Key, p => p.Value);
        var options = state.Settings.Options;
        var symbols = new Dictionary<string, SymbolSizingInputs>(StringComparer.Ordinal);
        foreach (var instrument in instruments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var refs = instrument.Id is { } bid && baskets.TryGetValue(bid, out var list)
                ? [.. list.Where(b => budgets.ContainsKey(b.Id)).Select(b => new BasketBudgetRef(b.Name, budgets[b.Id]))]
                : (IReadOnlyList<BasketBudgetRef>)[];
            VolReading? reading = null;
            double? adv = null;
            if (instrument.Id is { } id)
            {
                var rows = await readDaily(id, session.AddDays(-LookBackDays), session.AddDays(1), cancellationToken);
                var complete = rows.Where(b => b.Complete).ToList();
                var returns = ReturnSeries.FromCloses(complete.Select(b => (IndiaTime.Today(b.Ts), b.Close)), session);
                reading = VolTarget.Read([.. returns.Select(r => r.Value)], options);
                adv = (double?)LiquidityTiers.ConsecutiveMedian([.. complete.Select(b => new DailyBar(IndiaTime.Today(b.Ts), b.Open, b.High, b.Low, b.Close, b.Volume))], forDate, calendar);
            }

            symbols[instrument.Symbol.ToUpperInvariant()] = new SymbolSizingInputs(reading, adv, refs);
        }

        return new SizingInputs(state.Settings, state.Version, symbols);
    }
}
