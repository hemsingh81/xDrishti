using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Market.Actions;

/// <summary>What the detection found for one instrument: the candidates, and how many were new suggestions (the others exist already, ignored ones included).</summary>
internal readonly record struct InstrumentDetection(int Candidates, int Suggested);

/// <summary>What a detection over several instruments found.</summary>
internal readonly record struct DetectionTotals(int Instruments, int Candidates, int Suggested);

/// <summary>
/// Runs <see cref="ActionDetector"/> over stored day bars and stores what it finds as <c>suggested</c> actions — never applied, never twice (no suggestion for an instrument and ex-date that has any action,
/// ignored ones included; the store also refuses duplicates). After a data job it looks only at the days the job changed (with the sessions before them as context and everything after them);
/// on request it looks at the whole history of every share and ETF with day bars.
/// </summary>
internal sealed class CorporateActionDetection(IDailySeries series, ICorporateActionQueries queries, ICorporateActionStore store, TimeProvider clock)
{
    /// <summary>Calendar days read before the first changed day: well over the 20 sessions the volume median needs.</summary>
    private const int ContextDays = 45;

    /// <summary>Looks at one instrument. <paramref name="changedFrom"/>: the first changed day (null: the whole series).</summary>
    public async ValueTask<InstrumentDetection> DetectAsync(Guid instrumentId, DateOnly? changedFrom, CancellationToken cancellationToken)
    {
        var read = changedFrom is { } day ? Max(CorporateAction.MinDate, day.AddDays(-ContextDays)) : CorporateAction.MinDate;
        var bars = await series.ReadAsync(instrumentId, read, cancellationToken);
        var candidates = ActionDetector.Detect(bars, changedFrom ?? CorporateAction.MinDate);
        if (candidates.Count == 0)
        {
            return new InstrumentDetection(0, 0);
        }

        var known = await queries.ListExDatesAsync(instrumentId, cancellationToken);
        var now = clock.GetUtcNow();
        var fresh = candidates
            .Where(c => !known.Contains(c.ExDate))
            .Select(c => CorporateAction.Suggest(instrumentId, c.ExDate, c.Kind, c.Numerator, c.Denominator, c.Note, now))
            .ToList();
        var stored = fresh.Count == 0 ? 0 : await store.AddSuggestionsAsync(fresh, cancellationToken);
        return new InstrumentDetection(candidates.Count, stored);
    }

    /// <summary>Looks at the whole history of every share and ETF with day bars.</summary>
    public async ValueTask<DetectionTotals> DetectAllAsync(CancellationToken cancellationToken)
    {
        var instruments = await series.ListInstrumentsAsync(cancellationToken);
        var candidates = 0;
        var suggested = 0;
        foreach (var instrument in instruments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var found = await DetectAsync(instrument.Id, null, cancellationToken);
            candidates += found.Candidates;
            suggested += found.Suggested;
        }

        return new DetectionTotals(instruments.Count, candidates, suggested);
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a >= b ? a : b;
}
