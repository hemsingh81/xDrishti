using System.Runtime.CompilerServices;
using XDrishti.Domain.Market.Context;
using XDrishti.Indicators;

namespace XDrishti.Simulation.Scanning;

/// <summary>
/// Builds the <see cref="ScanContext"/> the frames read from the stored context days (P3-lift, ADR 0145), once per list instance: a run reads the days once and hands the same list to every instrument, so the context is built once per run and nothing is global.
/// Pure: the same days give the same context. A set that the library refuses (a row its validation rejects) gives no context.
/// </summary>
internal static class ContextReader
{
    private static readonly ConditionalWeakTable<IReadOnlyList<ContextDayRecord>, Built> Cache = new();

    /// <summary>The context of the days, or null when there are none or the library refuses them.</summary>
    public static ScanContext? Of(IReadOnlyList<ContextDayRecord>? days)
    {
        if (days is null || days.Count == 0)
        {
            return null;
        }

        return Cache.GetValue(days, static d => new Built(Build(d))).Context;
    }

    private static ScanContext? Build(IReadOnlyList<ContextDayRecord> days)
    {
        var result = MarketContext.Create(days.Select(r => new ContextDay(r.Date, r.IndexHigh, r.IndexLow, r.IndexClose, r.VixClose, r.Breadth)));
        return result.IsSuccess ? new ScanContext(result.Value) : null;
    }

    private sealed class Built(ScanContext? context)
    {
        public ScanContext? Context { get; } = context;
    }
}
