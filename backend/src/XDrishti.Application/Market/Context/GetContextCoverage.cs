using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Market.Context;

/// <summary>What the stored market context makes readable, and what is missing (P3-01d). A read: it writes nothing.</summary>
public sealed record GetContextCoverageQuery : IQuery<ContextCoverageView>;

internal sealed class GetContextCoverageHandler(
    IContextSettings settings,
    IContextBarSource bars,
    IContextDayStore store,
    ITradingCalendar tradingCalendar,
    TimeProvider clock) : IQueryHandler<GetContextCoverageQuery, ContextCoverageView>
{
    public const int MaxSkippedListed = 50;

    public const string SourceNote = "derived from 1-minute bars; the official daily close can differ slightly";

    public async ValueTask<ContextCoverageView> HandleAsync(GetContextCoverageQuery query, CancellationToken cancellationToken)
    {
        var records = await store.ListAsync(cancellationToken);
        var coverage = ContextCoverage.Of(records);
        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var lastClosed = calendar.LastClosedSession(clock.GetUtcNow());
        var health = ContextHealth.Of(records.Select(r => r.Date).ToHashSet(), calendar, lastClosed);

        string? missing = null;
        string? fix = null;
        ContextLegs? legs = null;
        IReadOnlyList<SkippedContextDay> skipped = [];
        if (!settings.Enabled)
        {
            missing = ContextPrerequisites.Disabled;
            fix = "Data:Context:Enabled is false.";
        }
        else
        {
            legs = await bars.ReadAsync(settings.IndexSymbols, settings.VixSymbols, cancellationToken);
            if (ContextPrerequisites.Missing(legs) is { } unmet)
            {
                (missing, fix) = unmet;
            }
            else
            {
                skipped = ContextDerivation.Derive(
                    legs.IndexLegs, legs.VixLegs, calendar, lastClosed, records.Select(r => r.Date).ToHashSet(), new ContextDerivationOptions(settings.VixGraceSessions, settings.MinDayFraction)).Skipped;
            }
        }

        var breadth = Breadth(await store.ListBreadthAsync(cancellationToken));
        return new ContextCoverageView(
            settings.Enabled,
            missing,
            fix,
            legs?.Index?.Symbol,
            legs?.Vix?.Symbol,
            coverage.Days,
            coverage.First,
            coverage.Last,
            health?.AgeSessions,
            health?.Stale ?? false,
            coverage.IndexTrendReadableFrom,
            coverage.RegimeReadableFrom,
            coverage.VixPctReadableFrom,
            coverage.RegimeWithVixReadableFrom,
            coverage.VixDays,
            coverage.VixMissingDays,
            coverage.VixWindowSpanDays,
            coverage.VixWindowTooWide,
            health?.GapSessions ?? 0,
            skipped.Count,
            [.. skipped.Take(MaxSkippedListed).Select(s => new ContextSkippedView(s.Date, s.Reason.ToCode(), s.Bars))],
            SourceNote,
            breadth);
    }

    private static ContextBreadthView Breadth(IReadOnlyList<StoredBreadth> rows)
    {
        var computed = rows.Where(r => r.State is not null).ToList();
        var thin = computed.Where(r => r.State == BreadthResult.Insufficient).Select(r => r.Date).ToList();
        var latest = computed.Count > 0 ? computed[^1] : null;
        return new ContextBreadthView(
            latest?.Date,
            latest?.Value,
            latest?.State,
            latest?.Voters,
            latest?.Universe,
            latest?.Excluded,
            computed.Count,
            rows.Count - computed.Count,
            thin.Count,
            [.. thin.TakeLast(MaxSkippedListed)],
            BreadthWording.Label(latest?.Universe));
    }
}
